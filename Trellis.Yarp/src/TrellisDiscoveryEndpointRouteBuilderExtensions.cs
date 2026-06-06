namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// <see cref="IEndpointRouteBuilder"/> extensions that publish the gateway's OIDC
/// discovery document and JWKS so downstream services using
/// <c>AddJwtBearer(o =&gt; o.Authority = gatewayUrl)</c> can fetch the active signing
/// keys without manual configuration. Pair with
/// <see cref="TrellisActorForwardingServiceCollectionExtensions.AddTrellisActorForwarding"/>.
/// </summary>
public static class TrellisDiscoveryEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Default OIDC discovery path served by <see cref="MapTrellisDiscoveryEndpoint"/>.
    /// Per RFC 8414, OIDC discovery is conventionally hosted at
    /// <c>{issuer}/.well-known/openid-configuration</c>.
    /// </summary>
    public const string DefaultOidcDiscoveryPath = "/.well-known/openid-configuration";

    /// <summary>
    /// Default JWKS path served by <see cref="MapTrellisDiscoveryEndpoint"/>. The exact
    /// path does not matter to downstream consumers (they always read the JWKS URL from
    /// the OIDC discovery document's <c>jwks_uri</c>) but the literal default is the
    /// commonly-deployed convention.
    /// </summary>
    public const string DefaultJwksPath = "/.well-known/jwks.json";

    /// <summary>
    /// Publishes the gateway's OIDC discovery document and JWKS as anonymous, cacheable
    /// HTTP endpoints. The discovery document advertises <see cref="TrellisActorForwardingOptions.Issuer"/>
    /// as the issuer and <see cref="TrellisActorForwardingOptions.PublicBaseUrl"/> joined
    /// with <paramref name="jwksPath"/> as the <c>jwks_uri</c>. The JWKS document contains
    /// every key in the active rotation ring (current
    /// <see cref="TrellisActorForwardingOptions.SigningCredentials"/> +
    /// <see cref="TrellisActorForwardingOptions.PreviousSigningKeys"/>).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="oidcPath">Path the discovery document is served from. Defaults to
    /// <see cref="DefaultOidcDiscoveryPath"/>.</param>
    /// <param name="jwksPath">Path the JWKS document is served from. Defaults to
    /// <see cref="DefaultJwksPath"/>. Used verbatim to build the <c>jwks_uri</c> in the
    /// discovery document.</param>
    /// <remarks>
    /// <para>
    /// <b>Anonymous endpoints.</b> Both endpoints are intentionally anonymous — JWKS
    /// MUST be reachable by downstream services before they have ever validated a token
    /// (chicken-and-egg). The published material is exclusively the asymmetric public
    /// keys and metadata about them; the corresponding private keys NEVER leave the
    /// gateway process.
    /// </para>
    /// <para>
    /// <b>Symmetric-key defense in depth.</b> The JWKS builder refuses to serialize any
    /// symmetric key, even though startup validation already rejects them. The two-layer
    /// check survives a future refactor that loosens validation (the JWKS endpoint
    /// continues to fail-closed) and matches the v1-only-asymmetric contract.
    /// </para>
    /// <para>
    /// <b>URLs come from options, never request context.</b> <c>HttpRequest.Host</c> is
    /// spoofable behind reverse proxies and could inject attacker-controlled discovery
    /// URLs into the published document. Both endpoints construct their absolute URLs
    /// exclusively from <see cref="TrellisActorForwardingOptions.PublicBaseUrl"/>
    /// (startup-validated absolute) joined with the literal paths supplied here.
    /// </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapTrellisDiscoveryEndpoint(
        this IEndpointRouteBuilder endpoints,
        string? oidcPath = null,
        string? jwksPath = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var oidc = oidcPath ?? DefaultOidcDiscoveryPath;
        var jwks = jwksPath ?? DefaultJwksPath;

        var oidcEndpoint = endpoints.MapGet(oidc, (HttpContext context) =>
            Results.Json(
                BuildDiscoveryDocument(
                    context.RequestServices.GetRequiredService<IOptions<TrellisActorForwardingOptions>>().Value,
                    jwks),
                contentType: "application/json"))
            .AllowAnonymous();

        var jwksEndpoint = endpoints.MapGet(jwks, (HttpContext context) =>
            Results.Json(
                BuildJwks(context.RequestServices.GetRequiredService<IOptions<TrellisActorForwardingOptions>>().Value),
                contentType: "application/json"))
            .AllowAnonymous();

        // Return a composite builder that applies any further conventions
        // (.WithTags(...), .RequireHost(...), caching metadata, etc.) to BOTH endpoints.
        // Returning only the OIDC builder would silently miss the JWKS endpoint for
        // any chained call — a surprise-partial-configuration footgun.
        return new CompositeEndpointConventionBuilder([oidcEndpoint, jwksEndpoint]);
    }

    internal static OidcDiscoveryDocument BuildDiscoveryDocument(
        TrellisActorForwardingOptions options,
        string jwksPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(jwksPath);

        var jwksUri = new Uri(options.PublicBaseUrl, jwksPath).ToString();

        // Advertise only the active signing algorithm — keeps the discovery document
        // truthful about what the gateway actually mints. Downstream consumers using
        // ValidAlgorithms = ["RS256"] (Recipe 33 recommendation) need the published
        // alg list to match exactly. v1 assumes rotation is within a single algorithm
        // family; if the active key's algorithm changes mid-rotation, redeploy with
        // the new alg or list both at the SigningCredentials.Algorithm level.
        var algorithm = options.SigningCredentials.Algorithm;

        return new OidcDiscoveryDocument
        {
            Issuer = options.Issuer,
            JwksUri = jwksUri,
            IdTokenSigningAlgValuesSupported = [algorithm],
        };
    }

    internal static JsonObject BuildJwks(TrellisActorForwardingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var keys = new JsonArray();

        // v1 normalizes the JWKS "alg" field to the active SigningCredentials.Algorithm
        // for every key in the ring (including PreviousSigningKeys). The contract assumes
        // rotation stays within a single algorithm family — if it ever doesn't, the
        // discovery document's id_token_signing_alg_values_supported (also single-valued
        // from the active alg) would disagree with the JWKS, so we keep them in lock-step.
        var activeAlgorithm = options.SigningCredentials.Algorithm;

        AppendKey(keys, options.SigningCredentials.Key, activeAlgorithm);
        foreach (var previous in options.PreviousSigningKeys)
            AppendKey(keys, previous, activeAlgorithm);

        return new JsonObject { ["keys"] = keys };
    }

    private static void AppendKey(JsonArray keys, SecurityKey? key, string activeAlgorithm)
    {
        // Defense in depth: a null entry in PreviousSigningKeys would crash
        // JsonWebKeyConverter.ConvertFromSecurityKey(null) with NullReferenceException and
        // 500 the JWKS endpoint. Startup validation rejects null entries, but a runtime
        // mutation of IOptionsMonitor (or a future refactor that loosens validation) could
        // still produce one. Silent-skip is the correct fail-closed posture: the missing
        // key isn't published, downstream validation for tokens signed by it fails — but
        // tokens signed by OTHER keys in the ring still validate, so the impact is bounded
        // to the misbehaving key rather than the whole endpoint.
        if (key is null)
            return;

        // Defense in depth: even though TrellisActorForwardingOptionsValidator rejects
        // symmetric keys at startup (including JsonWebKey { Kty: "oct" } wrappers), refuse
        // to publish one if it somehow reaches this point. Publishing symmetric key
        // material would leak the signing secret. Mirror the validator's broader check
        // so a future refactor that loosens validation does not silently start publishing
        // symmetric key material.
        if (key is SymmetricSecurityKey)
            return;
        if (key is JsonWebKey octJwk && string.Equals(octJwk.Kty, JsonWebAlgorithmsKeyTypes.Octet, StringComparison.Ordinal))
            return;

        // Defense in depth: JsonWebKeyConverter.ConvertFromSecurityKey throws
        // NotSupportedException for some SecurityKey subclasses it does not know how to
        // serialize — notably JsonWebKey input (the converter's INPUT is the concrete
        // CLR subclasses; JsonWebKey is its OUTPUT format). X509SecurityKey behaves
        // differently: the converter succeeds but produces a JWK that's missing the
        // public-component fields the builder emits (n/e or crv/x/y) — that key is
        // serialized but downstream consumers can't use it for signature validation.
        // Both failure modes are rejected at startup by IsSupportedAsymmetricKey, but if
        // a future refactor loosens validation OR a caller mutates IOptionsMonitor at
        // runtime, the catch here ensures the JWKS endpoint stays up. A 500 here
        // cascades to "every downstream service can't validate any token."
        JsonWebKey jwk;
        try
        {
            jwk = JsonWebKeyConverter.ConvertFromSecurityKey(key);
        }
        catch (NotSupportedException)
        {
            return;
        }

        // Default the "use" hint when the converter does not set it. Some IdentityModel
        // versions leave it empty for raw RsaSecurityKey / ECDsaSecurityKey.
        if (string.IsNullOrEmpty(jwk.Use))
            jwk.Use = "sig";

        // Always set "alg" to the active SigningCredentials.Algorithm (not derived from key
        // TYPE). The previous behavior defaulted to RS256/ES256 based on the key class,
        // which would silently disagree with the discovery document's published
        // id_token_signing_alg_values_supported (also single-valued from the active alg)
        // whenever the consumer configures RS384 / RS512 / ES384 / ES512.
        jwk.Alg = activeAlgorithm;

        var jwkObject = new JsonObject();
        SetIfPresent(jwkObject, "kty", jwk.Kty);
        SetIfPresent(jwkObject, "use", jwk.Use);
        SetIfPresent(jwkObject, "alg", jwk.Alg);
        SetIfPresent(jwkObject, "kid", jwk.Kid);
        SetIfPresent(jwkObject, "n", jwk.N);
        SetIfPresent(jwkObject, "e", jwk.E);
        SetIfPresent(jwkObject, "crv", jwk.Crv);
        SetIfPresent(jwkObject, "x", jwk.X);
        SetIfPresent(jwkObject, "y", jwk.Y);
        // Intentionally NOT serialized: d (RSA private exponent), p, q, dp, dq, qi, k (symmetric
        // key material). The JsonWebKeyConverter.ConvertFromSecurityKey path only populates
        // the public components for asymmetric keys, but we filter explicitly here as
        // defense-in-depth in case a future IdentityModel revision starts populating private
        // components and a consumer accidentally hands the minter the private key.

        keys.Add(jwkObject);
    }

    private static void SetIfPresent(JsonObject obj, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            obj[name] = value;
    }
}

/// <summary>
/// Minimal OIDC discovery document advertising the gateway's issuer and JWKS URL.
/// The full RFC 8414 / OIDC Discovery 1.0 spec lists many optional fields; the
/// gateway only emits what downstream <c>AddJwtBearer.Authority</c> actually consumes
/// to fetch JWKS and validate tokens.
/// </summary>
internal sealed class OidcDiscoveryDocument
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public required IReadOnlyList<string> IdTokenSigningAlgValuesSupported { get; init; }
}
