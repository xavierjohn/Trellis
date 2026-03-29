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
    public async Task GetCurrentActorAsync_DefaultMapping_SetsIdFromOidClaim()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-oid-123"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-oid-123");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_SetsIdFromShortOidClaim()
    {
        var user = AuthenticatedUser(
            new Claim("oid", "user-oid-short-123"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-oid-short-123");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_MapsRolesClaimsToPermissions()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("roles", "Documents.Read"),
            new Claim("roles", "Documents.Write"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().BeEquivalentTo(["Documents.Read", "Documents.Write"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ForbiddenPermissionsIsEmpty()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.ForbiddenPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ExtractsTenantId()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("tid", "tenant-abc"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.TenantId).Should().Be("tenant-abc");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ExtractsPreferredUsername()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("preferred_username", "alice@contoso.com"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.PreferredUsername).Should().Be("alice@contoso.com");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ExtractsAuthorizedParty()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("azp", "app-client-id"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.AuthorizedParty).Should().Be("app-client-id");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ExtractsAuthorizedPartyAcr()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("azpacr", "2"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.AuthorizedPartyAcr).Should().Be("2");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ExtractsAuthContextClassReference()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("acrs", "c1"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.AuthContextClassReference).Should().Be("c1");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_ExtractsIpAddress()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"));

        var actor = await CreateProvider(user, remoteIp: IPAddress.Parse("10.0.0.1")).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.IpAddress).Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_NoRemoteIp_OmitsIpAttribute()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasAttribute(ActorAttributes.IpAddress).Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_MfaInAmrClaim_SetsMfaTrue()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("amr", "pwd"),
            new Claim("amr", "mfa"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("true");
    }

    [Theory]
    [InlineData("MFA")]
    [InlineData("Mfa")]
    public async Task GetCurrentActorAsync_DefaultMapping_MfaInAmrClaim_WithDifferentCasing_SetsMfaTrue(string mfaValue)
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("amr", "pwd"),
            new Claim("amr", mfaValue));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("true");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_NoMfaInAmrClaim_SetsMfaFalse()
    {
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim("amr", "pwd"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("false");
    }

    #endregion

    #region Multi-identity claim spoofing (S1)

    [Fact]
    public async Task GetCurrentActorAsync_MultipleIdentities_OnlyReadsFromAuthenticatedIdentity()
    {
        var authenticatedIdentity = new ClaimsIdentity(
        [
            new Claim(OidClaimType, "real-user-oid"),
            new Claim("roles", "Documents.Read"),
            new Claim("tid", "real-tenant")
        ], "Bearer");

        var spoofedIdentity = new ClaimsIdentity(
        [
            new Claim(OidClaimType, "admin-oid-999"),
            new Claim("roles", "GlobalAdmin"),
            new Claim("roles", "Documents.Delete"),
            new Claim("tid", "spoofed-tenant")
        ]); // no authenticationType → IsAuthenticated = false

        var principal = new ClaimsPrincipal(new[] { authenticatedIdentity, spoofedIdentity });
        var provider = CreateProvider(principal);

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("real-user-oid", "should read from authenticated identity, not spoofed");
        actor.HasPermission("Documents.Read").Should().BeTrue();
        actor.HasPermission("GlobalAdmin").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.HasPermission("Documents.Delete").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.GetAttribute(ActorAttributes.TenantId).Should().Be("real-tenant", "attributes should come from authenticated identity only");
    }

    [Fact]
    public async Task GetCurrentActorAsync_SpoofedIdentityFirst_OnlyReadsFromAuthenticatedIdentity()
    {
        var spoofedIdentity = new ClaimsIdentity(
        [
            new Claim(OidClaimType, "admin-oid-999"),
            new Claim("roles", "GlobalAdmin"),
            new Claim("tid", "spoofed-tenant")
        ]); // no authenticationType → IsAuthenticated = false

        var authenticatedIdentity = new ClaimsIdentity(
        [
            new Claim(OidClaimType, "real-user-oid"),
            new Claim("roles", "Documents.Read"),
            new Claim("tid", "real-tenant")
        ], "Bearer");

        // Spoofed identity is first — FindFirstValue would return spoofed values before the fix
        var principal = new ClaimsPrincipal(new[] { spoofedIdentity, authenticatedIdentity });
        var provider = CreateProvider(principal);

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("real-user-oid", "should ignore unauthenticated identity even when listed first");
        actor.HasPermission("Documents.Read").Should().BeTrue();
        actor.HasPermission("GlobalAdmin").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.GetAttribute(ActorAttributes.TenantId).Should().Be("real-tenant");
    }

    #endregion

    #region Error handling

    [Fact]
    public async Task GetCurrentActorAsync_NoHttpContext_ThrowsInvalidOperationException()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var provider = new EntraActorProvider(accessor, Options.Create(new EntraActorOptions()));

        var act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HttpContext*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_NotAuthenticated_ThrowsInvalidOperationException()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var provider = CreateProvider(user);

        var act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authenticated*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_MissingOidClaim_ThrowsInvalidOperationException()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1")); // has sub but not oid

        var provider = CreateProvider(user);

        var act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*objectidentifier*");
    }

    #endregion

    #region Custom options

    [Fact]
    public async Task GetCurrentActorAsync_CustomIdClaimType_UsesConfiguredClaim()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-sub-456"));

        var options = new EntraActorOptions { IdClaimType = "sub" };
        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-sub-456");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomMapPermissions_UsesDelegate()
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

        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().BeEquivalentTo(["User.Read", "Mail.Send"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomMapForbiddenPermissions_UsesDelegate()
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

        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.ForbiddenPermissions.Should().Contain("Documents.Read");
        actor.HasPermission("Documents.Read").Should().BeFalse("deny overrides allow");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomMapAttributes_UsesDelegate()
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

        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.GetAttribute("region").Should().Be("us-west-2");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomMapPermissions_WhenDelegateThrows_WrapsExceptionWithContext()
    {
        var user = AuthenticatedUser(new Claim(OidClaimType, "user-1"));
        var options = new EntraActorOptions
        {
            MapPermissions = _ => throw new InvalidOperationException("Permissions exploded")
        };

        var act = async () => await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EntraActorOptions.MapPermissions*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomMapForbiddenPermissions_WhenDelegateThrows_WrapsExceptionWithContext()
    {
        var user = AuthenticatedUser(new Claim(OidClaimType, "user-1"));
        var options = new EntraActorOptions
        {
            MapForbiddenPermissions = _ => throw new InvalidOperationException("Forbidden exploded")
        };

        var act = async () => await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EntraActorOptions.MapForbiddenPermissions*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomMapAttributes_WhenDelegateThrows_WrapsExceptionWithContext()
    {
        var user = AuthenticatedUser(new Claim(OidClaimType, "user-1"));
        var options = new EntraActorOptions
        {
            MapAttributes = (_, _) => throw new InvalidOperationException("Attributes exploded")
        };

        var act = async () => await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EntraActorOptions.MapAttributes*");
    }

    #endregion

    #region Role claim type variants

    [Fact]
    public async Task GetCurrentActorAsync_DefaultMapping_AlsoMapsClaimTypesRoleClaim()
    {
        // System.Security.Claims.ClaimTypes.Role is the long-form URI
        var user = AuthenticatedUser(
            new Claim(OidClaimType, "user-1"),
            new Claim(ClaimTypes.Role, "Admin.FullAccess"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().Contain("Admin.FullAccess");
    }

    #endregion
}