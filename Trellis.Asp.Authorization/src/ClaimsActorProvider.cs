namespace Trellis.Asp.Authorization;

using System.Collections.Frozen;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Configuration options for <see cref="ClaimsActorProvider"/>.
/// Controls which claims are used for actor identity and permissions.
/// </summary>
/// <remarks>
/// <para>
/// Default behavior maps the OIDC standard <c>sub</c> claim to
/// <see cref="Actor.Id"/> and the <c>permissions</c> claim to
/// <see cref="Actor.Permissions"/>. Override the claim names
/// to match your identity provider's token format.
/// </para>
/// </remarks>
public class ClaimsActorOptions
{
    /// <summary>
    /// The claim type used to resolve the actor's unique identifier.
    /// Defaults to <c>"sub"</c> (OIDC standard subject claim).
    /// </summary>
    public string ActorIdClaim { get; set; } = "sub";

    /// <summary>
    /// The claim type used to resolve the actor's permissions.
    /// Defaults to <c>"permissions"</c> (common convention in OIDC/JWT tokens).
    /// </summary>
    public string PermissionsClaim { get; set; } = "permissions";
}

/// <summary>
/// <see cref="IActorProvider"/> implementation that hydrates an <see cref="Actor"/>
/// from the current <see cref="HttpContext.User"/> using standard JWT/OIDC claims.
/// Works with any identity provider (Auth0, Keycloak, Okta, Entra, etc.).
/// </summary>
/// <remarks>
/// <para>
/// Register as scoped in DI via
/// <see cref="ServiceCollectionExtensions.AddClaimsActorProvider"/>.
/// Claim mapping is controlled by <see cref="ClaimsActorOptions"/>.
/// </para>
/// <para>
/// This provider assumes authentication has already occurred. It throws
/// <see cref="InvalidOperationException"/> if no authenticated user exists.
/// </para>
/// </remarks>
public class ClaimsActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<ClaimsActorOptions> options) : IActorProvider
{
    /// <summary>
    /// The HTTP context accessor used to retrieve the current user.
    /// </summary>
    protected IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    /// <summary>
    /// The configured claim mapping options.
    /// </summary>
    protected ClaimsActorOptions Options { get; } = options.Value;

    /// <inheritdoc />
    public virtual Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var user = HttpContextAccessor.HttpContext?.User;
        var identity = user?.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity
            ?? throw new InvalidOperationException(
                "No authenticated user. Ensure authentication middleware runs before actor resolution.");

        var actorId = identity.FindFirst(Options.ActorIdClaim)?.Value
            ?? throw new InvalidOperationException(
                $"Claim '{Options.ActorIdClaim}' not found in the authenticated user's claims.");

        var permissions = identity.FindAll(Options.PermissionsClaim)
            .Select(c => c.Value)
            .ToFrozenSet();

        var actor = Actor.Create(actorId, permissions);
        return Task.FromResult(actor);
    }
}
