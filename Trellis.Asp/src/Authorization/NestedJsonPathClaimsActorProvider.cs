namespace Trellis.Asp.Authorization;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Configuration options for <see cref="NestedJsonPathClaimsActorProvider"/>.
/// Maps an identity provider's nested-JSON claim shape (Auth0 <c>app_metadata.roles</c>,
/// Azure B2C <c>extension_*</c>, Okta nested claims) onto <see cref="Actor.Id"/> and
/// <see cref="Actor.Permissions"/> via a dotted JSON path.
/// </summary>
/// <remarks>
/// <para>
/// Configure with a <see cref="ContainerClaim"/> naming the top-level claim that carries the
/// JSON payload, plus optional <see cref="ActorIdPath"/> and <see cref="PermissionsPath"/>
/// dotted paths. When <see cref="ActorIdPath"/> is empty the provider falls back to the
/// flat <see cref="ClaimsActorOptions.ActorIdClaim"/> on the base class. When
/// <see cref="PermissionsPath"/> is empty the provider falls back to the base-class
/// flat-claim resolution.
/// </para>
/// <para>
/// Path traversal is JSON-aware: each dot segment indexes the matching object property; the
/// terminal segment may resolve to a string (single value), an array of strings (multiple
/// permissions), or an object whose own property names become the values (the Auth0
/// roles-as-object shape). Non-string scalar values are skipped — only string-shaped values
/// flow into <see cref="Actor.Permissions"/>.
/// </para>
/// </remarks>
public sealed class NestedJsonPathClaimsActorOptions : ClaimsActorOptions
{
    /// <summary>
    /// The claim type on the <see cref="ClaimsIdentity"/> that carries the JSON payload. The
    /// claim's <see cref="Claim.Value"/> must be a valid JSON document. Required when either
    /// <see cref="ActorIdPath"/> or <see cref="PermissionsPath"/> is non-empty.
    /// </summary>
    /// <remarks>
    /// Common values: <c>"app_metadata"</c> (Auth0), <c>"extension_attributes"</c>
    /// (Azure B2C), <c>"custom_claims"</c> (Okta). Inspect the JWT payload to determine the
    /// correct name for your identity provider.
    /// </remarks>
    public string ContainerClaim { get; set; } = "";

    /// <summary>
    /// Dotted JSON path traversed inside <see cref="ContainerClaim"/> to resolve the actor's
    /// unique identifier. Empty by default — when empty, the provider falls back to the
    /// inherited flat <see cref="ClaimsActorOptions.ActorIdClaim"/> resolution.
    /// </summary>
    /// <example>
    /// <code>
    /// // JWT payload: { "app_metadata": { "user_id": "auth0|abc123" } }
    /// options.ContainerClaim = "app_metadata";
    /// options.ActorIdPath = "user_id";
    /// </code>
    /// </example>
    public string ActorIdPath { get; set; } = "";

    /// <summary>
    /// Dotted JSON path traversed inside <see cref="ContainerClaim"/> to resolve the actor's
    /// permissions. Empty by default — when empty, the provider falls back to the inherited
    /// flat <see cref="ClaimsActorOptions.PermissionsClaim"/> resolution.
    /// </summary>
    /// <remarks>
    /// The terminal element may be a single string (one permission), an array of strings
    /// (multiple permissions), or an object whose own property names are emitted as
    /// permissions (the Auth0 roles-as-object shape).
    /// </remarks>
    /// <example>
    /// <code>
    /// // JWT payload: { "app_metadata": { "roles": ["orders:read", "orders:write"] } }
    /// options.ContainerClaim = "app_metadata";
    /// options.PermissionsPath = "roles";
    /// </code>
    /// </example>
    public string PermissionsPath { get; set; } = "";
}

/// <summary>
/// <see cref="ClaimsActorProvider"/> derivation that maps an identity provider's nested-JSON
/// claim shape (Auth0 <c>app_metadata.roles</c>, Azure B2C <c>extension_*</c>, some Okta
/// token shapes) onto <see cref="Actor.Id"/> and <see cref="Actor.Permissions"/> via a
/// dotted JSON path. Falls back to the inherited flat-claim resolution when the configured
/// paths are empty.
/// </summary>
/// <remarks>
/// <para>
/// Register via
/// <see cref="ServiceCollectionExtensions.AddNestedJsonPathClaimsActorProvider"/>.
/// Configure with <see cref="NestedJsonPathClaimsActorOptions"/> — provide the
/// <see cref="NestedJsonPathClaimsActorOptions.ContainerClaim"/> name and the dotted
/// <see cref="NestedJsonPathClaimsActorOptions.ActorIdPath"/> / 
/// <see cref="NestedJsonPathClaimsActorOptions.PermissionsPath"/> values for your provider.
/// </para>
/// <para>
/// JSON parsing uses <see cref="JsonDocument"/> for AOT compatibility (no source generators
/// or reflection). Malformed JSON in the container claim emits a warning at most once per
/// application lifetime and falls back to flat-claim resolution so a malformed token does
/// not silently 401 the entire actor pipeline.
/// </para>
/// </remarks>
public class NestedJsonPathClaimsActorProvider : ClaimsActorProvider
{
    /// <summary>
    /// Throttle flag for the malformed-JSON diagnostic; flips from 0 to 1 the first time
    /// the diagnostic fires for any instance.
    /// </summary>
    private static int s_malformedJsonWarningFired;

    private new readonly NestedJsonPathClaimsActorOptions Options;
    private readonly ILogger<NestedJsonPathClaimsActorProvider>? _logger;

    /// <summary>
    /// Test-only hook for resetting the per-provider throttles.
    /// </summary>
    internal static new void ResetDiagnosticThrottlesForTests()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        s_malformedJsonWarningFired = 0;
    }

    /// <summary>
    /// Initializes a new <see cref="NestedJsonPathClaimsActorProvider"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="NestedJsonPathClaimsActorOptions.ContainerClaim"/> is empty but
    /// <see cref="NestedJsonPathClaimsActorOptions.ActorIdPath"/> or
    /// <see cref="NestedJsonPathClaimsActorOptions.PermissionsPath"/> is set. Silently
    /// ignoring the configured paths when the container claim is empty would reintroduce the
    /// silent-403 footgun the provider exists to prevent — fail fast instead so the consumer
    /// fixes the misconfiguration before the first request.
    /// </exception>
    public NestedJsonPathClaimsActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<NestedJsonPathClaimsActorOptions> options,
        ILogger<NestedJsonPathClaimsActorProvider>? logger = null)
        : base(
            httpContextAccessor,
            // Wrap the derived options snapshot so the base class sees the consumer's
            // ContainerClaim / ActorIdClaim / PermissionsClaim / ValidateClaimShapeOnFirstUse
            // values via its IOptions<ClaimsActorOptions> dependency. Both ClaimsActorProvider
            // and NestedJsonPathClaimsActorProvider use IOptions (not IOptionsMonitor) so the
            // snapshot at construction time is the correct semantics — value mutations after
            // composition would not flow through the base either.
            Microsoft.Extensions.Options.Options.Create<ClaimsActorOptions>(options.Value),
            logger: null)
    {
        Options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(Options.ContainerClaim)
            && (!string.IsNullOrWhiteSpace(Options.ActorIdPath) || !string.IsNullOrWhiteSpace(Options.PermissionsPath)))
        {
            throw new InvalidOperationException(
                "NestedJsonPathClaimsActorOptions.ContainerClaim must be set when ActorIdPath or " +
                "PermissionsPath is configured. Leaving the container claim empty (or whitespace-only) " +
                "would silently ignore the configured nested-JSON paths and fall back to flat-claim " +
                "resolution, reintroducing the silent-403 footgun this provider exists to prevent.");
        }
    }

    /// <inheritdoc />
    public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // If no paths are configured at all, the consumer is using the provider as a drop-in
        // replacement for the base; delegate verbatim. (The container-required-when-path-set
        // misconfiguration is caught at construction time — see the constructor invariant.)
        if (string.IsNullOrEmpty(Options.ActorIdPath) && string.IsNullOrEmpty(Options.PermissionsPath))
        {
            return base.GetCurrentActorAsync(cancellationToken);
        }

        var httpContext = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");

        var identity = httpContext.User.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity;
        if (identity is null)
            return Task.FromResult(Maybe<Actor>.None);

        var containerJson = identity.FindFirst(Options.ContainerClaim)?.Value;
        if (string.IsNullOrWhiteSpace(containerJson))
        {
            // No container claim → fall back to flat resolution so a token without the nested
            // container (e.g., service principal vs interactive user) keeps working when the
            // provider is also configured with a flat ActorIdClaim / PermissionsClaim.
            return base.GetCurrentActorAsync(cancellationToken);
        }

        if (!TryParseJsonDocument(containerJson, out var doc))
        {
            // Malformed JSON in the container claim is a token-shape bug. Emit a one-off
            // warning and fall back to the flat resolution rather than 401-ing the request.
            if (Interlocked.CompareExchange(ref s_malformedJsonWarningFired, 1, 0) == 0 && _logger is not null)
                LogMalformedJsonContainer(_logger, Options.ContainerClaim);
            return base.GetCurrentActorAsync(cancellationToken);
        }

        using (doc)
        {
            string? actorId = !string.IsNullOrEmpty(Options.ActorIdPath)
                ? TraverseToString(doc.RootElement, Options.ActorIdPath)
                : null;

            // ActorId fallback when the path resolved nothing: route through the base's
            // short↔long fallback resolver so MapInboundClaims-remapped tokens (e.g.,
            // configured "sub" but token carries ClaimTypes.NameIdentifier) still resolve.
            // A literal identity.FindFirst(Options.ActorIdClaim) call here would skip the
            // base's fallback and reintroduce the silent-401 footgun.
            if (actorId is null)
            {
                var fallbackActorId = ResolveClaimWithFallback(identity, Options.ActorIdClaim);
                if (string.IsNullOrWhiteSpace(fallbackActorId))
                    return Task.FromResult(Maybe<Actor>.None);
                actorId = fallbackActorId;
            }

            var permissions = !string.IsNullOrEmpty(Options.PermissionsPath)
                ? TraverseToStringSet(doc.RootElement, Options.PermissionsPath)
                : FrozenSet<string>.Empty;

            // Permissions fallback when the path resolved nothing: also route through the
            // base's short↔long aware resolver. Same MapInboundClaims rationale as the
            // ActorId fallback above.
            if (permissions.Count == 0 && !string.IsNullOrEmpty(Options.PermissionsClaim))
            {
                permissions = ResolveAllClaimsWithFallback(identity, Options.PermissionsClaim)
                    .Select(c => c.Value)
                    .ToFrozenSet();
            }

            // Run the base's diagnostic on the FINAL permission set so the empty-permissions
            // warning fires even when the nested path itself produced empty (the path missed,
            // the terminal element was a number/boolean/null that yielded nothing, etc.).
            // Without this call, the override would silently 403 on malformed terminal shapes.
            // Pass our own logger because the base's _logger slot was deliberately left null
            // in our constructor (we own the diagnostic-logger relationship for this provider).
            MaybeEmitClaimShapeDiagnostics(identity, permissions, _logger);

            return Task.FromResult(Maybe.From(Actor.Create(actorId, permissions)));
        }
    }

    private static bool TryParseJsonDocument(string json, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            return false;
        }
    }

    /// <summary>
    /// Traverses <paramref name="root"/> through the dotted <paramref name="path"/>, returning
    /// the terminal element's string value, or <see langword="null"/> if the path does not
    /// resolve or the terminal element is not a string.
    /// </summary>
    private static string? TraverseToString(JsonElement root, string path)
    {
        var element = root;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (!element.TryGetProperty(segment, out var next))
                return null;

            element = next;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    /// <summary>
    /// Traverses <paramref name="root"/> through the dotted <paramref name="path"/>, returning
    /// the terminal element flattened to a frozen set of strings. The terminal may be:
    /// (a) a string — yields one entry; (b) an array — yields the string-typed entries; or
    /// (c) an object — yields the property names (Auth0 roles-as-object shape). Any other
    /// shape produces an empty set.
    /// </summary>
    private static FrozenSet<string> TraverseToStringSet(JsonElement root, string path)
    {
        var element = root;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object)
                return FrozenSet<string>.Empty;

            if (!element.TryGetProperty(segment, out var next))
                return FrozenSet<string>.Empty;

            element = next;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                {
                    var value = element.GetString();
                    return string.IsNullOrEmpty(value)
                        ? FrozenSet<string>.Empty
                        : new[] { value }.ToFrozenSet();
                }

            case JsonValueKind.Array:
                {
                    var values = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var v = item.GetString();
                            if (!string.IsNullOrEmpty(v))
                                values.Add(v);
                        }
                    }

                    return values.ToFrozenSet();
                }

            case JsonValueKind.Object:
                {
                    var values = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                        values.Add(property.Name);

                    return values.ToFrozenSet();
                }

            default:
                return FrozenSet<string>.Empty;
        }
    }

    private static readonly Action<ILogger, string, Exception?> _logMalformedJsonContainer =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, nameof(NestedJsonPathClaimsActorProvider)),
            "NestedJsonPathClaimsActorProvider could not parse claim '{ContainerClaim}' as JSON. " +
            "The provider fell back to the inherited flat-claim resolution for this request. " +
            "Inspect the JWT to verify the container claim carries a JSON document. This " +
            "diagnostic fires at most once per application lifetime.");

    private static void LogMalformedJsonContainer(ILogger<NestedJsonPathClaimsActorProvider> logger, string containerClaim) =>
        _logMalformedJsonContainer(logger, containerClaim, null);
}
