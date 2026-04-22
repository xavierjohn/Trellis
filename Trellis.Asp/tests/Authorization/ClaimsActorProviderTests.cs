namespace Trellis.Asp.Authorization.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

/// <summary>
/// Tests for <see cref="ClaimsActorProvider"/> — the generic OIDC/JWT claims-based actor provider.
/// </summary>
public class ClaimsActorProviderTests
{
    private static ClaimsActorProvider CreateProvider(
        ClaimsPrincipal user,
        ClaimsActorOptions? options = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var opts = Options.Create(options ?? new ClaimsActorOptions());
        return new ClaimsActorProvider(accessor, opts);
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    #region Default claim mapping (sub + permissions)

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_SetsIdFromSubClaim()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-sub-123"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-sub-123");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_MapsPermissionsClaims()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", "orders:read"),
            new Claim("permissions", "orders:write"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_NoPermissionsClaims_ReturnsEmptyPermissions()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-1"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().BeEmpty();
    }

    #endregion

    #region Custom claim mapping

    [Fact]
    public async Task GetCurrentActorAsync_CustomActorIdClaim_UsesConfiguredClaim()
    {
        var user = AuthenticatedUser(new Claim("oid", "user-oid-456"));
        var options = new ClaimsActorOptions { ActorIdClaim = "oid" };

        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-oid-456");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomPermissionsClaim_UsesConfiguredClaim()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("roles", "Admin"),
            new Claim("roles", "Editor"));
        var options = new ClaimsActorOptions { PermissionsClaim = "roles" };

        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().BeEquivalentTo(["Admin", "Editor"]);
    }

    #endregion

    #region Error handling

    [Fact]
    public async Task GetCurrentActorAsync_NoHttpContext_ThrowsInvalidOperationException()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var provider = new ClaimsActorProvider(accessor, Options.Create(new ClaimsActorOptions()));

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HttpContext*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_NotAuthenticated_ThrowsInvalidOperationException()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type
        var provider = CreateProvider(user);

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authenticated*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_MissingActorIdClaim_ThrowsInvalidOperationException()
    {
        var user = AuthenticatedUser(new Claim("oid", "user-1")); // has oid but not sub

        Func<Task> act = async () => await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sub*not found*");
    }

    #endregion

    #region Nested-claim mapping (ga-15)

    [Fact]
    public async Task GetCurrentActorAsync_NestedJsonClaim_NotAutoFlattened()
    {
        // ga-15: ClaimsActorProvider does flat ClaimType matching only — a JSON-object
        // claim is stored as the raw serialized string, not unwrapped. Consumers must
        // subclass to project nested values into Actor.Permissions.
        var nested = """{"role":"admin","tier":"gold"}""";
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", nested));
        var options = new ClaimsActorOptions { PermissionsClaim = "app_metadata" };

        var actor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Permissions.Should().BeEquivalentTo([nested],
            "the default provider returns the raw claim value verbatim and does not parse JSON");
    }

    [Fact]
    public async Task GetCurrentActorAsync_Subclass_CanProjectNestedJsonClaim()
    {
        // ga-15: documented subclassing pattern for nested-claim mapping.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", """{"roles":["orders:read","orders:write"]}"""));

        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var provider = new NestedJsonRolesActorProvider(accessor, Options.Create(new ClaimsActorOptions()));

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-1");
        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    private sealed class NestedJsonRolesActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<ClaimsActorOptions> options) : ClaimsActorProvider(httpContextAccessor, options)
    {
        public override Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var httpContext = HttpContextAccessor.HttpContext!;
            var identity = (ClaimsIdentity)httpContext.User.Identities.First(i => i.IsAuthenticated);

            var id = identity.FindFirst(Options.ActorIdClaim)!.Value;
            var nested = identity.FindFirst("app_metadata")?.Value;
            var permissions = nested is null
                ? new HashSet<string>()
                : System.Text.Json.JsonDocument.Parse(nested)
                    .RootElement.GetProperty("roles")
                    .EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToHashSet();

            return Task.FromResult(Actor.Create(id, permissions));
        }
    }

    #endregion

    #region Multi-identity claim spoofing (S1)

    [Fact]
    public async Task GetCurrentActorAsync_MultipleIdentities_OnlyReadsFromAuthenticatedIdentity()
    {
        // Arrange: principal with authenticated identity + unauthenticated identity with spoofed claims
        var authenticatedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "real-user-123"),
            new Claim("permissions", "orders:read")
        ], "Bearer"); // authenticationType = "Bearer" → IsAuthenticated = true

        var spoofedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "admin-999"),
            new Claim("permissions", "admin"),
            new Claim("permissions", "orders:delete")
        ]); // no authenticationType → IsAuthenticated = false

        var principal = new ClaimsPrincipal(new[] { authenticatedIdentity, spoofedIdentity });
        var provider = CreateProvider(principal);

        // Act
        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        // Assert — actor should ONLY have claims from the authenticated identity
        actor.Id.Should().Be("real-user-123", "should read from authenticated identity, not spoofed");
        actor.HasPermission("orders:read").Should().BeTrue();
        actor.HasPermission("admin").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.HasPermission("orders:delete").Should().BeFalse("should NOT have permissions from unauthenticated identity");
    }

    [Fact]
    public async Task GetCurrentActorAsync_SpoofedIdentityFirst_OnlyReadsFromAuthenticatedIdentity()
    {
        // Arrange: spoofed (unauthenticated) identity listed FIRST — worst case for FindFirstValue
        var spoofedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "admin-999"),
            new Claim("permissions", "admin"),
            new Claim("permissions", "orders:delete")
        ]); // no authenticationType → IsAuthenticated = false

        var authenticatedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "real-user-123"),
            new Claim("permissions", "orders:read")
        ], "Bearer");

        // Spoofed identity is first — FindFirstValue("sub") would return "admin-999" before the fix
        var principal = new ClaimsPrincipal(new[] { spoofedIdentity, authenticatedIdentity });
        var provider = CreateProvider(principal);

        // Act
        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        // Assert
        actor.Id.Should().Be("real-user-123", "should ignore unauthenticated identity even when listed first");
        actor.HasPermission("orders:read").Should().BeTrue();
        actor.HasPermission("admin").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.HasPermission("orders:delete").Should().BeFalse("should NOT have permissions from unauthenticated identity");
    }

    #endregion

    #region ForbiddenPermissions and Attributes defaults

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_ForbiddenPermissionsIsEmpty()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-1"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.ForbiddenPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_AttributesIsEmpty()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-1"));

        var actor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Attributes.Should().BeEmpty();
    }

    #endregion
}