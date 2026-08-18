namespace Trellis.Asp.Authorization.Tests;

using System.Security.Claims;

/// <summary>
/// Tests for <see cref="RolePermissionProjection"/> — the role→permission flattening helper.
/// </summary>
public class RolePermissionProjectionTests
{
    private static Dictionary<string, IReadOnlyCollection<string>> Map(
        params (string Role, string[] Permissions)[] entries)
    {
        var map = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        foreach (var (role, permissions) in entries)
            map[role] = permissions;
        return map;
    }

    // === ExpandRoles ===

    [Fact]
    public void ExpandRoles_SingleRole_ReturnsItsPermissions()
    {
        var map = Map(("Admin", ["orders:read", "orders:write"]));

        var permissions = RolePermissionProjection.ExpandRoles(["Admin"], map);

        permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    [Fact]
    public void ExpandRoles_MultipleRoles_FlattensAndDeduplicates()
    {
        var map = Map(
            ("Reader", ["orders:read"]),
            ("Editor", ["orders:read", "orders:write"]));

        var permissions = RolePermissionProjection.ExpandRoles(["Reader", "Editor"], map);

        permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    [Fact]
    public void ExpandRoles_UnknownRole_IsSkippedReturningTheKnownPermissions()
    {
        var map = Map(("Admin", ["orders:read"]));

        var permissions = RolePermissionProjection.ExpandRoles(["Admin", "GhostRole"], map);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ExpandRoles_AllUnknownRoles_ReturnsEmpty()
    {
        var map = Map(("Admin", ["orders:read"]));

        var permissions = RolePermissionProjection.ExpandRoles(["Nope", "AlsoNope"], map);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public void ExpandRoles_EmptyRoles_ReturnsEmpty()
    {
        var map = Map(("Admin", ["orders:read"]));

        var permissions = RolePermissionProjection.ExpandRoles([], map);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public void ExpandRoles_NullOrWhitespaceRoleNames_AreSkipped()
    {
        var map = Map(("Admin", ["orders:read"]));

        var permissions = RolePermissionProjection.ExpandRoles(["Admin", "", "   ", null!], map);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ExpandRoles_NullOrWhitespacePermissionValues_AreSkipped()
    {
        var map = Map(("Admin", ["orders:read", "", "   ", null!]));

        var permissions = RolePermissionProjection.ExpandRoles(["Admin"], map);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ExpandRoles_CaseInsensitiveMap_MatchesCaseVariantRole()
    {
        var map = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = ["orders:read"],
        };

        var permissions = RolePermissionProjection.ExpandRoles(["ADMIN"], map);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ExpandRoles_OrdinalOutput_TreatsCaseVariantPermissionsAsDistinct()
    {
        var map = Map(("Admin", ["orders:read", "Orders:Read"]));

        var permissions = RolePermissionProjection.ExpandRoles(["Admin"], map);

        permissions.Should().BeEquivalentTo(["orders:read", "Orders:Read"]);
    }

    [Fact]
    public void ExpandRoles_NullRoles_Throws()
    {
        var act = () => RolePermissionProjection.ExpandRoles(null!, Map());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExpandRoles_NullMap_Throws()
    {
        var act = () => RolePermissionProjection.ExpandRoles(["Admin"], null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // === ForRoleClaims ===

    [Fact]
    public void ForRoleClaims_DefaultClaimType_MatchesShortRolesAndClaimTypesRole()
    {
        var map = Map(("Admin", ["orders:read"]), ("Reader", ["catalog:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map);

        var permissions = project([
            new Claim("roles", "Admin"),
            new Claim(ClaimTypes.Role, "Reader"),
        ]);

        permissions.Should().BeEquivalentTo(["orders:read", "catalog:read"]);
    }

    [Fact]
    public void ForRoleClaims_ExplicitClaimType_MatchesOnlyThatType()
    {
        var map = Map(("Admin", ["orders:read"]), ("Reader", ["catalog:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map, roleClaimType: "app_role");

        var permissions = project([
            new Claim("app_role", "Admin"),
            new Claim("roles", "Reader"),
        ]);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ForRoleClaims_UnknownRoleClaim_IsSkippedWithoutThrowing()
    {
        var map = Map(("Admin", ["orders:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map);

        var act = () => project([new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Role, "GhostRole")]);

        act.Should().NotThrow();
        act().Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ForRoleClaims_KeepRolesAsPermissions_AddsRoleNamesPlusExpandedPermissions()
    {
        var map = Map(("Admin", ["orders:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map, keepRolesAsPermissions: true);

        var permissions = project([new Claim(ClaimTypes.Role, "Admin")]);

        permissions.Should().BeEquivalentTo(["Admin", "orders:read"]);
    }

    [Fact]
    public void ForRoleClaims_KeepRolesAsPermissions_UnmappedRole_IsNotAddedAsPermission()
    {
        var map = Map(("Admin", ["orders:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map, keepRolesAsPermissions: true);

        var permissions = project([
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "GhostRole"),
        ]);

        permissions.Should().BeEquivalentTo(["Admin", "orders:read"]);
        permissions.Should().NotContain("GhostRole",
            "an unmapped role must never leak in as a permission, even with keepRolesAsPermissions");
    }

    [Fact]
    public void ExpandRoles_RoleMappedToEmptyCollection_ContributesNothing()
    {
        var map = Map(("Guest", []));

        var permissions = RolePermissionProjection.ExpandRoles(["Guest"], map);

        permissions.Should().BeEmpty();
    }

    [Fact]
    public void ForRoleClaims_DefaultKeepRolesFalse_OmitsRoleNames()
    {
        var map = Map(("Admin", ["orders:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map);

        var permissions = project([new Claim(ClaimTypes.Role, "Admin")]);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ForRoleClaims_NonRoleClaims_AreIgnored()
    {
        var map = Map(("Admin", ["orders:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map);

        var permissions = project([
            new Claim("sub", "user-1"),
            new Claim("email", "a@b.com"),
            new Claim(ClaimTypes.Role, "Admin"),
        ]);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ForRoleClaims_EmptyRoleClaimValues_AreSkipped()
    {
        var map = Map(("Admin", ["orders:read"]));
        var project = RolePermissionProjection.ForRoleClaims(map);

        var permissions = project([
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "   "),
        ]);

        permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void ForRoleClaims_NullMap_Throws()
    {
        var act = () => RolePermissionProjection.ForRoleClaims(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ForRoleClaims_WhitespaceClaimType_Throws(string blank)
    {
        var act = () => RolePermissionProjection.ForRoleClaims(Map(), roleClaimType: blank);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForRoleClaims_AssignableToEntraMapPermissions_ProducesExpectedPermissions()
    {
        var map = Map(("Admin", ["orders:read", "orders:write"]));
        var options = new EntraActorOptions
        {
            MapPermissions = RolePermissionProjection.ForRoleClaims(map),
        };

        var permissions = options.MapPermissions([new Claim(ClaimTypes.Role, "Admin")]);

        permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }
}