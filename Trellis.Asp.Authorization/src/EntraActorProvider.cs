namespace Trellis.Asp.Authorization;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// <see cref="IActorProvider"/> implementation that hydrates an <see cref="Actor"/>
/// from the current <see cref="HttpContext.User"/> using Azure Entra ID v2.0 JWT claims.
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
public sealed class EntraActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<EntraActorOptions> options) : IActorProvider
{
    /// <inheritdoc />
    public Actor GetCurrentActor()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No HttpContext available. Ensure this is called within an HTTP request scope.");

        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException(
                "No authenticated user. Ensure authentication middleware runs before actor resolution.");

        var config = options.Value;
        var claims = user.Claims;

        var id = user.FindFirstValue(config.IdClaimType)
            ?? throw new InvalidOperationException(
                $"Claim '{config.IdClaimType}' not found in the authenticated user's claims. " +
                "Verify the token configuration or set EntraActorOptions.IdClaimType.");

        var permissions = InvokeMapping(
            "MapPermissions",
            () => config.MapPermissions(claims));

        var forbiddenPermissions = InvokeMapping(
            "MapForbiddenPermissions",
            () => config.MapForbiddenPermissions(claims));

        var attributes = InvokeMapping(
            "MapAttributes",
            () => config.MapAttributes(claims, httpContext));

        return new Actor(id, permissions, forbiddenPermissions, attributes);
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