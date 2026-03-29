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
/// <c>AddMicrosoftIdentityWebApi</c>). It throws
/// <see cref="InvalidOperationException"/> if no authenticated user exists.
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
    public override Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = HttpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");

        var user = httpContext.User;

        var identity = user.Identities.FirstOrDefault(i => i.IsAuthenticated) as ClaimsIdentity
            ?? throw new InvalidOperationException(
                "No authenticated user. Ensure authentication middleware runs before actor resolution.");

        var claims = identity.Claims;

        var id = ResolveActorId(identity, _entraOptions)
            ?? throw new InvalidOperationException(
                $"Claim '{_entraOptions.IdClaimType}' not found in the authenticated user's claims. " +
                "Verify the token configuration or set EntraActorOptions.IdClaimType.");

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
        return Task.FromResult(actor);
    }

    private static string? ResolveActorId(ClaimsIdentity identity, EntraActorOptions config)
    {
        var id = identity.FindFirst(config.IdClaimType)?.Value;
        if (id is not null)
            return id;

        return string.Equals(config.IdClaimType, DefaultOidClaimType, StringComparison.Ordinal)
            ? identity.FindFirst(ShortOidClaimType)?.Value
            : null;
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