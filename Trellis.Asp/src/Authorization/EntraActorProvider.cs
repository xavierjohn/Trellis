namespace Trellis.Asp.Authorization;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// <see cref="IActorProvider"/> implementation that hydrates an <see cref="Actor"/>
/// from the current <see cref="HttpContext.User"/> using Azure Entra ID v2.0 JWT claims.
/// Extends <see cref="ClaimsActorProvider"/> with Entra-specific claim mapping for
/// permissions, forbidden permissions, and ABAC attributes.
/// </summary>
/// <remarks>
/// <para>
/// Register as scoped in DI via <see cref="ServiceCollectionExtensions.AddEntraActorProvider"/>.
/// Mapping behavior is controlled by <see cref="EntraActorOptions"/>.
/// </para>
/// <para>
/// This provider assumes authentication has already occurred (e.g., via
/// <c>AddMicrosoftIdentityWebApi</c>). It returns <see cref="Maybe{T}.None"/> when the
/// request has no usable authenticated actor — no authenticated
/// <see cref="System.Security.Claims.ClaimsIdentity"/>, or the configured
/// <see cref="EntraActorOptions.IdClaimType"/> is missing (and the short <c>oid</c>
/// fallback also misses) — and the mediator authorization pipeline maps that to
/// <see cref="Error.AuthenticationRequired"/> (HTTP 401). It throws
/// <see cref="InvalidOperationException"/> only for genuine configuration failures
/// (no <c>HttpContext</c>, or a <c>Map*</c> delegate threw); those surface as HTTP 500.
/// </para>
/// </remarks>
public sealed class EntraActorProvider : ClaimsActorProvider
{
    private const string DefaultOidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string ShortOidClaimType = "oid";

    private readonly EntraActorOptions _entraOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntraActorProvider"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides the current HTTP context.</param>
    /// <param name="options">Entra-specific claim mapping options.</param>
    public EntraActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<EntraActorOptions> options)
        : base(httpContextAccessor, Microsoft.Extensions.Options.Options.Create(
            new ClaimsActorOptions
            {
                ActorIdClaim = options.Value.IdClaimType,
                PermissionsClaim = "roles"
            })) =>
        _entraOptions = options.Value;

    /// <inheritdoc />
    public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // HttpContext missing is a configuration bug, not authentication state. Throw to
        // surface as 500; do not mask as 401 via Maybe.None.
        var httpContext = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");

        var user = httpContext.User;

        // No authenticated identity → client-error state → Maybe.None → 401.
        var identity = user.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity;
        if (identity is null)
            return Task.FromResult(Maybe<Actor>.None);

        var claims = identity.Claims;

        // Authenticated identity present but the configured ID claim is missing — client
        // can't be identified, treat as unauthenticated for the response. (Token-shape vs
        // server-side misconfig is indistinguishable here; same 401 outcome serves both.)
        var id = ResolveActorId(identity, _entraOptions);
        if (id is null)
            return Task.FromResult(Maybe<Actor>.None);

        var permissions = InvokeMapping(
            "MapPermissions",
            () => _entraOptions.MapPermissions(claims));

        var forbiddenPermissions = InvokeMapping(
            "MapForbiddenPermissions",
            () => _entraOptions.MapForbiddenPermissions(claims));

        var attributes = InvokeMapping(
            "MapAttributes",
            () => _entraOptions.MapAttributes(claims, httpContext));

        var actor = new Actor(id, permissions, forbiddenPermissions, attributes);
        return Task.FromResult(Maybe.From(actor));
    }

    internal new ValueTask<Maybe<ActorId>> ResolveCurrentActorIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");
        var identity = context.User.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity;
        return ValueTask.FromResult(identity is null
            ? Maybe<ActorId>.None
            : Maybe.From(ResolveActorId(identity, _entraOptions)));
    }

    private static ActorId? ResolveActorId(ClaimsIdentity identity, EntraActorOptions config)
    {
        var id = identity.FindFirst(config.IdClaimType)?.Value;
        if (id is null && string.Equals(config.IdClaimType, DefaultOidClaimType, StringComparison.Ordinal))
            id = identity.FindFirst(ShortOidClaimType)?.Value;

        return string.IsNullOrWhiteSpace(id) ? null : ActorId.Create(id);
    }

    private static T InvokeMapping<T>(string mappingName, Func<T> mapping)
    {
        try
        {
            return mapping();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"EntraActorOptions.{mappingName} threw an exception while mapping the authenticated user's claims.",
                exception);
        }
    }
}