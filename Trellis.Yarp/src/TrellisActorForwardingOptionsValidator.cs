namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// Validates <see cref="TrellisActorForwardingOptions"/> at host start. Catches the
/// security-critical mis-configurations (symmetric signing keys, missing <c>kid</c>,
/// lifetime outside the allowed window, non-absolute discovery URL) before the first
/// request is served.
/// </summary>
/// <remarks>
/// Registered via <c>services.AddOptions&lt;TrellisActorForwardingOptions&gt;()
/// .ValidateOnStart()</c> from the <c>AddTrellisActorForwarding</c> extension. Failing
/// validation throws <see cref="OptionsValidationException"/> during host startup,
/// before the YARP pipeline accepts any traffic.
/// </remarks>
internal sealed class TrellisActorForwardingOptionsValidator
    : IValidateOptions<TrellisActorForwardingOptions>
{
    private static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TrellisActorForwardingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add($"{nameof(options.Issuer)} must be a non-empty string (typically a URL identifying the gateway, e.g. \"https://gateway.internal\").");

        if (options.PublicBaseUrl is null)
        {
            failures.Add($"{nameof(options.PublicBaseUrl)} is required.");
        }
        else if (!options.PublicBaseUrl.IsAbsoluteUri)
        {
            failures.Add($"{nameof(options.PublicBaseUrl)} ('{options.PublicBaseUrl}') must be an absolute URI; the discovery endpoint builder uses it verbatim to construct jwks_uri and other discovery URLs and MUST NOT infer them from request context (HttpRequest.Host is spoofable behind reverse proxies).");
        }

        ValidateSigningCredential(options.SigningCredentials, nameof(options.SigningCredentials), failures);

        if (options.PreviousSigningKeys is null)
        {
            failures.Add($"{nameof(options.PreviousSigningKeys)} must not be null (use an empty list when no rotation is in flight).");
        }
        else
        {
            var seenKids = new HashSet<string>(StringComparer.Ordinal);

            if (options.SigningCredentials?.Key?.KeyId is { Length: > 0 } activeKid)
                seenKids.Add(activeKid);

            for (var i = 0; i < options.PreviousSigningKeys.Count; i++)
            {
                var prev = options.PreviousSigningKeys[i];
                ValidatePreviousKey(prev, i, failures);
                if (prev?.KeyId is { Length: > 0 } prevKid && !seenKids.Add(prevKid))
                    failures.Add($"{nameof(options.PreviousSigningKeys)}[{i}] uses kid '{prevKid}' which collides with another key in the rotation ring (the active SigningCredentials.Key or a previous entry); every kid in the ring must be unique so JWKS lookup and audit correlation stay unambiguous.");
            }
        }

        if (options.Lifetime <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(options.Lifetime)} ({options.Lifetime}) must be positive.");
        }
        else if (options.Lifetime < MinLifetime || options.Lifetime > MaxLifetime)
        {
            failures.Add($"{nameof(options.Lifetime)} ({options.Lifetime}) must be in [{MinLifetime}, {MaxLifetime}] — short-lived tokens are the contract, the cap is defense against accidental mis-configuration that would expand the post-compromise spoof window.");
        }

        if (options.AudiencePerCluster is null)
            failures.Add($"{nameof(options.AudiencePerCluster)} must not be null; the default returns ClusterConfig.ClusterId.");

        if (options.ProjectPermissionsFor is null)
            failures.Add($"{nameof(options.ProjectPermissionsFor)} must not be null; the default is pass-through.");

        if (options.ProjectForbiddenFor is null)
            failures.Add($"{nameof(options.ProjectForbiddenFor)} must not be null; the default is pass-through. Note the contract integrity invariant — even when the projection returns an empty set, the count is still emitted so downstream services can distinguish empty from absent.");

        if (options.ProjectAttributes is null)
            failures.Add($"{nameof(options.ProjectAttributes)} must not be null; the default is pass-through.");

        if (options.ActorIdResolver is null)
            failures.Add($"{nameof(options.ActorIdResolver)} must not be null; the default returns actor.Id.Value, but consumers fronting multiple IdPs MUST override to namespace the sub claim (e.g. \"{{issuer}}|{{tenant}}|{{externalSub}}\") so the resulting sub is globally unique.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSigningCredential(SigningCredentials? credentials, string member, List<string> failures)
    {
        if (credentials is null)
        {
            failures.Add($"{member} is required — an asymmetric SigningCredentials with a non-empty Kid (typically RsaSecurityKey or ECDsaSecurityKey).");
            return;
        }

        if (credentials.Key is null)
        {
            failures.Add($"{member}.Key is required.");
            return;
        }

        if (string.IsNullOrEmpty(credentials.Key.KeyId))
            failures.Add($"{member}.Key.KeyId (the 'kid') must be set to a non-empty string — every key in the rotation ring must be identifiable so JWKS lookup and audit correlation work. The transform emits 'kid' in the JWT header; downstream JwtBearerHandler and air-gapped static-key-ring consumers MUST resolve the right key by kid during rotation.");

        if (IsSymmetric(credentials.Key))
            failures.Add($"{member}.Key is a symmetric key ({credentials.Key.GetType().Name}{DescribeJwkKty(credentials.Key)}); v1 rejects symmetric signing keys because publishing them in JWKS would leak the signing secret, AND refusing to publish them in JWKS silently breaks the 'downstream uses AddJwtBearer.Authority' discovery story. Use RsaSecurityKey or ECDsaSecurityKey instead.");

        if (IsSymmetricAlgorithm(credentials.Algorithm))
            failures.Add($"{member}.Algorithm '{credentials.Algorithm}' is an HMAC (symmetric) algorithm; v1 rejects HMAC algorithms because the security model assumes the public verification material can be safely published in JWKS. Use RS256/RS384/RS512 (RSA) or ES256/ES384/ES512 (ECDSA) instead.");

        if (!IsSymmetric(credentials.Key) && !IsSupportedAsymmetricKey(credentials.Key))
            failures.Add($"{member}.Key is a {credentials.Key.GetType().Name}; v1 supports RsaSecurityKey and ECDsaSecurityKey. X509SecurityKey is rejected because the JWKS builder does not yet emit x5c/x5t. JsonWebKey wrappers are rejected because Microsoft.IdentityModel's JsonWebKeyConverter throws NotSupportedException for JsonWebKey input (JsonWebKey is the converter's OUTPUT format, not its input). The JWKS endpoint's defense-in-depth catch would SILENTLY SKIP the unsupported key — but the consequence is that the gateway's active signing key would then be ABSENT from the published JWKS, breaking ALL downstream JwtBearer token validation that uses Authority-based key discovery (the downstream resolves keys by kid from JWKS; missing kid = signature validation fails for every minted token). Unwrap to RsaSecurityKey via cert.GetRSAPrivateKey() (or ECDsaSecurityKey via cert.GetECDsaPrivateKey()) before passing it as the signing key — signing requires the PRIVATE key; using the public-key unwrap would pass validation but fail at runtime when minting.");
    }

    private static void ValidatePreviousKey(SecurityKey? key, int index, List<string> failures)
    {
        if (key is null)
        {
            failures.Add($"{nameof(TrellisActorForwardingOptions.PreviousSigningKeys)}[{index}] is null; remove the entry or replace it with a non-null asymmetric key.");
            return;
        }

        if (string.IsNullOrEmpty(key.KeyId))
            failures.Add($"{nameof(TrellisActorForwardingOptions.PreviousSigningKeys)}[{index}].KeyId (the 'kid') must be set to a non-empty string — same rationale as the active key: downstream services resolve by kid during rotation.");

        if (IsSymmetric(key))
            failures.Add($"{nameof(TrellisActorForwardingOptions.PreviousSigningKeys)}[{index}] is a symmetric key ({key.GetType().Name}{DescribeJwkKty(key)}); v1 rejects symmetric signing keys in the rotation ring (the JWKS endpoint refuses to publish them and the discovery story would silently break).");

        if (!IsSymmetric(key) && !IsSupportedAsymmetricKey(key))
            failures.Add($"{nameof(TrellisActorForwardingOptions.PreviousSigningKeys)}[{index}] is a {key.GetType().Name}; v1 supports RsaSecurityKey and ECDsaSecurityKey in the rotation ring. X509SecurityKey and JsonWebKey are rejected — unwrap to RsaSecurityKey / ECDsaSecurityKey before adding to PreviousSigningKeys.");
    }

    /// <summary>
    /// Recognizes symmetric keys including the <see cref="JsonWebKey"/> wrapper case
    /// where the underlying material is HMAC (<c>kty: "oct"</c>). Without the JWK check,
    /// a consumer could bypass the asymmetric-only contract by passing
    /// <c>new SigningCredentials(new JsonWebKey(octKtyJson), SecurityAlgorithms.HmacSha256)</c>
    /// through validation.
    /// </summary>
    private static bool IsSymmetric(SecurityKey key) =>
        key is SymmetricSecurityKey ||
        (key is JsonWebKey jwk && string.Equals(jwk.Kty, JsonWebAlgorithmsKeyTypes.Octet, StringComparison.Ordinal));

    /// <summary>
    /// HMAC algorithms (HS256/HS384/HS512) are symmetric regardless of the key wrapper
    /// type. Checking <see cref="SigningCredentials.Algorithm"/> directly defends
    /// against a future <see cref="SecurityKey"/> subclass that confuses the
    /// <see cref="IsSymmetric"/> structural check.
    /// </summary>
    private static bool IsSymmetricAlgorithm(string? algorithm) =>
        algorithm is SecurityAlgorithms.HmacSha256
            or SecurityAlgorithms.HmacSha384
            or SecurityAlgorithms.HmacSha512;

    /// <summary>
    /// v1 supports an allow-list of well-known asymmetric key types:
    /// <see cref="RsaSecurityKey"/> and <see cref="ECDsaSecurityKey"/>.
    /// <see cref="X509SecurityKey"/> is rejected explicitly because
    /// <see cref="JsonWebKeyConverter.ConvertFromSecurityKey"/> does not populate the
    /// public-key fields the JWKS builder currently emits (<c>n</c>/<c>e</c> for RSA,
    /// <c>crv</c>/<c>x</c>/<c>y</c> for EC) — the X509 path needs <c>x5c</c>/<c>x5t</c>,
    /// which v1 does not yet emit. The recommended unwrap is
    /// <c>new RsaSecurityKey(cert.GetRSAPrivateKey())</c> (or
    /// <see cref="ECDsaSecurityKey"/> via <c>cert.GetECDsaPrivateKey()</c>) — signing
    /// requires the PRIVATE key; using the public-key unwrap would pass validation but
    /// fail at runtime when minting.
    /// <see cref="JsonWebKey"/> wrappers are likewise rejected: in IdentityModel 8.x,
    /// <c>JsonWebKeyConverter.ConvertFromSecurityKey(jsonWebKey)</c> throws
    /// <see cref="NotSupportedException"/> (the converter's input set is the concrete
    /// SecurityKey subclasses; JsonWebKey is the converter's OUTPUT format, not its
    /// input). Reject at startup so the consumer is pointed at the workaround rather
    /// than producing a JWKS endpoint that throws at runtime on its first request.
    /// </summary>
    private static bool IsSupportedAsymmetricKey(SecurityKey key) =>
        key is RsaSecurityKey or ECDsaSecurityKey;

    private static string DescribeJwkKty(SecurityKey key)
        => key is JsonWebKey jwk && !string.IsNullOrEmpty(jwk.Kty)
            ? $", kty=\"{jwk.Kty}\""
            : string.Empty;
}
