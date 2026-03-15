namespace Trellis.Asp.Authorization.Tests;

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

/// <summary>
/// Tests for <see cref="EntraActorProvider"/> default and customized claim mapping.
/// </summary>
public class EntraActorProviderTests
{
    private const string OidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    private static EntraActorProvider CreateProvider(
        ClaimsPrincipal user,
        EntraActorOptions? options = null,
        IPAddress? remoteIp = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        if (remoteIp is not null)
            httpContext.Connection.RemoteIpAddress = remoteIp;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var opts = Options.Create(options ?? new EntraActorOptions());
        return new EntraActorProvider(accessor, opts);
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    #region Default mapping

    [Fact]
    public void GetCurrentActor_DefaultMapping_SetsIdFromOidClaim()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-oid-123"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.Id.Should().Be("user-oid-123");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_MapsRolesClaimsToPermissions()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("roles", "Documents.Read"),
            new Claim("roles", "Documents.Write"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.Permissions.Should().BeEquivalentTo(["Documents.Read", "Documents.Write"]);
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ForbiddenPermissionsIsEmpty()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.ForbiddenPermissions.Should().BeEmpty();
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ExtractsTenantId()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("tid", "tenant-abc"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.TenantId).Should().Be("tenant-abc");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ExtractsPreferredUsername()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("preferred_username", "alice@contoso.com"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.PreferredUsername).Should().Be("alice@contoso.com");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ExtractsAuthorizedParty()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("azp", "app-client-id"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.AuthorizedParty).Should().Be("app-client-id");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ExtractsAuthorizedPartyAcr()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("azpacr", "2"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.AuthorizedPartyAcr).Should().Be("2");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ExtractsAuthContextClassReference()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("acrs", "c1"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.AuthContextClassReference).Should().Be("c1");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_ExtractsIpAddress()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"));

        var actor = CreateProvider(user, remoteIp: IPAddress.Parse("10.0.0.1")).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.IpAddress).Should().Be("10.0.0.1");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_NoRemoteIp_OmitsIpAttribute()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.HasAttribute(ActorAttributes.IpAddress).Should().BeFalse();
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_MfaInAmrClaim_SetsMfaTrue()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("amr", "pwd"),
            new Claim("amr", "mfa"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("true");
    }

    [Theory]
    [InlineData("MFA")]
    [InlineData("Mfa")]
    public void GetCurrentActor_DefaultMapping_MfaInAmrClaim_WithDifferentCasing_SetsMfaTrue(string mfaValue)
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("amr", "pwd"),
            new Claim("amr", mfaValue));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("true");
    }

    [Fact]
    public void GetCurrentActor_DefaultMapping_NoMfaInAmrClaim_SetsMfaFalse()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("amr", "pwd"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("false");
    }

    #endregion

    #region Error handling

    [Fact]
    public void GetCurrentActor_NoHttpContext_ThrowsInvalidOperationException()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var provider = new EntraActorProvider(accessor, Options.Create(new EntraActorOptions()));

        var act = provider.GetCurrentActor;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HttpContext*");
    }

    [Fact]
    public void GetCurrentActor_NotAuthenticated_ThrowsInvalidOperationException()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var provider = CreateProvider(user);

        var act = provider.GetCurrentActor;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*authenticated*");
    }

    [Fact]
    public void GetCurrentActor_MissingOidClaim_ThrowsInvalidOperationException()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1")); // has sub but not oid

        var provider = CreateProvider(user);

        var act = provider.GetCurrentActor;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*objectidentifier*");
    }

    #endregion

    #region Custom options

    [Fact]
    public void GetCurrentActor_CustomIdClaimType_UsesConfiguredClaim()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-sub-456"));

        var options = new EntraActorOptions { IdClaimType = "sub" };
        var actor = CreateProvider(user, options).GetCurrentActor();

        actor.Id.Should().Be("user-sub-456");
    }

    [Fact]
    public void GetCurrentActor_CustomMapPermissions_UsesDelegate()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("scp", "User.Read Mail.Send"));

        var options = new EntraActorOptions
        {
            MapPermissions = claims => claims
                .Where(c => c.Type == "scp")
                .SelectMany(c => c.Value.Split(' '))
                .ToHashSet()
        };

        var actor = CreateProvider(user, options).GetCurrentActor();

        actor.Permissions.Should().BeEquivalentTo(["User.Read", "Mail.Send"]);
    }

    [Fact]
    public void GetCurrentActor_CustomMapForbiddenPermissions_UsesDelegate()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("roles", "Documents.Read"),
            new Claim("denied", "Documents.Read"));

        var options = new EntraActorOptions
        {
            MapForbiddenPermissions = claims => claims
                .Where(c => c.Type == "denied")
                .Select(c => c.Value)
                .ToHashSet()
        };

        var actor = CreateProvider(user, options).GetCurrentActor();

        actor.ForbiddenPermissions.Should().Contain("Documents.Read");
        actor.HasPermission("Documents.Read").Should().BeFalse("deny overrides allow");
    }

    [Fact]
    public void GetCurrentActor_CustomMapAttributes_UsesDelegate()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("custom_region", "us-west-2"));

        var options = new EntraActorOptions
        {
            MapAttributes = (claims, _) => claims
                .Where(c => c.Type == "custom_region")
                .ToDictionary(c => "region", c => c.Value)
        };

        var actor = CreateProvider(user, options).GetCurrentActor();

        actor.GetAttribute("region").Should().Be("us-west-2");
    }

    [Fact]
    public void GetCurrentActor_CustomMapPermissions_WhenDelegateThrows_WrapsExceptionWithContext()
    {
        var user = AuthenticatedUser(new Claim(OidClaimType, "user-1"));
        var options = new EntraActorOptions
        {
            MapPermissions = _ => throw new InvalidOperationException("Permissions exploded")
        };

        var act = () => CreateProvider(user, options).GetCurrentActor();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EntraActorOptions.MapPermissions*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("Permissions exploded");
    }

    [Fact]
    public void GetCurrentActor_CustomMapForbiddenPermissions_WhenDelegateThrows_WrapsExceptionWithContext()
    {
        var user = AuthenticatedUser(new Claim(OidClaimType, "user-1"));
        var options = new EntraActorOptions
        {
            MapForbiddenPermissions = _ => throw new InvalidOperationException("Forbidden exploded")
        };

        var act = () => CreateProvider(user, options).GetCurrentActor();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EntraActorOptions.MapForbiddenPermissions*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("Forbidden exploded");
    }

    [Fact]
    public void GetCurrentActor_CustomMapAttributes_WhenDelegateThrows_WrapsExceptionWithContext()
    {
        var user = AuthenticatedUser(new Claim(OidClaimType, "user-1"));
        var options = new EntraActorOptions
        {
            MapAttributes = (_, _) => throw new InvalidOperationException("Attributes exploded")
        };

        var act = () => CreateProvider(user, options).GetCurrentActor();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EntraActorOptions.MapAttributes*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("Attributes exploded");
    }

    #endregion

    #region Role claim type variants

    [Fact]
    public void GetCurrentActor_DefaultMapping_AlsoMapsClaimTypesRoleClaim()
    {
        // System.Security.Claims.ClaimTypes.Role is the long-form URI
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim(ClaimTypes.Role, "Admin.FullAccess"));

        var actor = CreateProvider(user).GetCurrentActor();

        actor.Permissions.Should().Contain("Admin.FullAccess");
    }

    #endregion
}