namespace Trellis.Authorization.Tests;

/// <summary>
/// Tests for the <see cref="Actor"/> record.
/// </summary>
public class ActorTests
{
    private static readonly HashSet<string> NoPermissions = [];
    private static readonly HashSet<string> NoForbidden = [];
    private static readonly Dictionary<string, string> NoAttributes = [];

    private static Actor CreateActor(
        string id = "user-1",
        HashSet<string>? permissions = null,
        HashSet<string>? forbidden = null,
        Dictionary<string, string>? attributes = null) =>
        new(id, permissions ?? NoPermissions, forbidden ?? NoForbidden, attributes ?? NoAttributes);

    #region Actor.Create factory

    [Fact]
    public void Create_WithIdAndPermissions_SetsEmptyForbiddenAndAttributes()
    {
        var actor = Actor.Create("user-1", new HashSet<string> { "Orders.Read" });

        actor.Id.Should().Be("user-1");
        actor.Permissions.Should().Contain("Orders.Read");
        actor.ForbiddenPermissions.Should().BeEmpty();
        actor.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void Create_ActorBehavesIdenticallyToFullConstructor()
    {
        var permissions = new HashSet<string> { "A", "B" };
        var fromFactory = Actor.Create("user-1", permissions);

        fromFactory.Id.Should().Be("user-1");
        fromFactory.Permissions.Should().BeEquivalentTo(permissions);
        fromFactory.ForbiddenPermissions.Should().BeEmpty();
        fromFactory.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void Create_MutatingSourcePermissionsAfterConstruction_DoesNotChangeActor()
    {
        var permissions = new HashSet<string> { "Orders.Read" };
        var actor = Actor.Create("user-1", permissions);

        permissions.Add("Admin.Write");

        actor.HasPermission("Orders.Read").Should().BeTrue();
        actor.HasPermission("Admin.Write").Should().BeFalse("Actor should snapshot permissions at construction time");
    }

    #endregion

    #region PermissionScopeSeparator constant

    [Fact]
    public void PermissionScopeSeparator_IsColon() =>
        Actor.PermissionScopeSeparator.Should().Be(':');

    #endregion

    #region HasPermission (unscoped)

    [Fact]
    public void HasPermission_WithMatchingPermission_ReturnsTrue()
    {
        var actor = CreateActor(permissions: ["Orders.Read", "Orders.Write"]);

        actor.HasPermission("Orders.Read").Should().BeTrue();
    }

    [Fact]
    public void HasPermission_WithoutMatchingPermission_ReturnsFalse()
    {
        var actor = CreateActor(permissions: ["Orders.Read"]);

        actor.HasPermission("Admin.Write").Should().BeFalse();
    }

    [Fact]
    public void HasPermission_WithForbiddenPermission_ReturnsFalse()
    {
        var actor = CreateActor(
            permissions: ["Orders.Read", "Orders.Write"],
            forbidden: ["Orders.Write"]);

        actor.HasPermission("Orders.Write").Should().BeFalse("deny always overrides allow");
    }

    [Fact]
    public void HasPermission_WithForbiddenPermission_DoesNotAffectOtherPermissions()
    {
        var actor = CreateActor(
            permissions: ["Orders.Read", "Orders.Write"],
            forbidden: ["Orders.Write"]);

        actor.HasPermission("Orders.Read").Should().BeTrue();
    }

    #endregion

    #region HasPermission (scoped — ReBAC)

    [Fact]
    public void HasPermission_Scoped_WithMatchingScopePermission_ReturnsTrue()
    {
        var actor = CreateActor(permissions: ["Document.Edit:Tenant_A"]);

        actor.HasPermission("Document.Edit", "Tenant_A").Should().BeTrue();
    }

    [Fact]
    public void HasPermission_Scoped_WithDifferentScope_ReturnsFalse()
    {
        var actor = CreateActor(permissions: ["Document.Edit:Tenant_A"]);

        actor.HasPermission("Document.Edit", "Tenant_B").Should().BeFalse();
    }

    [Fact]
    public void HasPermission_Scoped_WithForbiddenScopedPermission_ReturnsFalse()
    {
        var actor = CreateActor(
            permissions: ["Document.Edit:Tenant_A"],
            forbidden: ["Document.Edit:Tenant_A"]);

        actor.HasPermission("Document.Edit", "Tenant_A").Should().BeFalse();
    }

    [Fact]
    public void HasPermission_Scoped_ForbiddenOnOneScopeDoesNotAffectOther()
    {
        var actor = CreateActor(
            permissions: ["Document.Edit:Tenant_A", "Document.Edit:Tenant_B"],
            forbidden: ["Document.Edit:Tenant_A"]);

        actor.HasPermission("Document.Edit", "Tenant_A").Should().BeFalse();
        actor.HasPermission("Document.Edit", "Tenant_B").Should().BeTrue();
    }

    #endregion

    #region HasAllPermissions

    [Fact]
    public void HasAllPermissions_WithAllMatching_ReturnsTrue()
    {
        var actor = CreateActor(permissions: ["Orders.Read", "Orders.Write", "Admin.Read"]);

        actor.HasAllPermissions(["Orders.Read", "Orders.Write"]).Should().BeTrue();
    }

    [Fact]
    public void HasAllPermissions_WithSomeMissing_ReturnsFalse()
    {
        var actor = CreateActor(permissions: ["Orders.Read"]);

        actor.HasAllPermissions(["Orders.Read", "Orders.Write"]).Should().BeFalse();
    }

    [Fact]
    public void HasAllPermissions_WithEmptyList_ReturnsTrue()
    {
        var actor = CreateActor();

        actor.HasAllPermissions([]).Should().BeTrue();
    }

    [Fact]
    public void HasAllPermissions_WithOneForbidden_ReturnsFalse()
    {
        var actor = CreateActor(
            permissions: ["Orders.Read", "Orders.Write"],
            forbidden: ["Orders.Write"]);

        actor.HasAllPermissions(["Orders.Read", "Orders.Write"]).Should().BeFalse();
    }

    #endregion

    #region HasAnyPermission

    [Fact]
    public void HasAnyPermission_WithOneMatching_ReturnsTrue()
    {
        var actor = CreateActor(permissions: ["Orders.Read"]);

        actor.HasAnyPermission(["Orders.Read", "Admin.Write"]).Should().BeTrue();
    }

    [Fact]
    public void HasAnyPermission_WithNoneMatching_ReturnsFalse()
    {
        var actor = CreateActor(permissions: ["Other.Permission"]);

        actor.HasAnyPermission(["Orders.Read", "Admin.Write"]).Should().BeFalse();
    }

    [Fact]
    public void HasAnyPermission_WithEmptyList_ReturnsFalse()
    {
        var actor = CreateActor(permissions: ["Orders.Read"]);

        actor.HasAnyPermission([]).Should().BeFalse();
    }

    [Fact]
    public void HasAnyPermission_AllMatchingAreForbidden_ReturnsFalse()
    {
        var actor = CreateActor(
            permissions: ["Orders.Read", "Orders.Write"],
            forbidden: ["Orders.Read", "Orders.Write"]);

        actor.HasAnyPermission(["Orders.Read", "Orders.Write"]).Should().BeFalse();
    }

    #endregion

    #region IsOwner

    [Fact]
    public void IsOwner_WithMatchingId_ReturnsTrue()
    {
        var actor = CreateActor(id: "user-42");

        actor.IsOwner("user-42").Should().BeTrue();
    }

    [Fact]
    public void IsOwner_WithDifferentId_ReturnsFalse()
    {
        var actor = CreateActor(id: "user-42");

        actor.IsOwner("user-99").Should().BeFalse();
    }

    [Fact]
    public void IsOwner_IsCaseSensitive()
    {
        var actor = CreateActor(id: "User-42");

        actor.IsOwner("user-42").Should().BeFalse("ordinal comparison is case-sensitive");
    }

    #endregion

    #region Attributes (ABAC)

    [Fact]
    public void HasAttribute_WithExistingKey_ReturnsTrue()
    {
        var actor = CreateActor(attributes: new Dictionary<string, string> { [ActorAttributes.MfaAuthenticated] = "true" });

        actor.HasAttribute(ActorAttributes.MfaAuthenticated).Should().BeTrue();
    }

    [Fact]
    public void HasAttribute_WithMissingKey_ReturnsFalse()
    {
        var actor = CreateActor();

        actor.HasAttribute(ActorAttributes.MfaAuthenticated).Should().BeFalse();
    }

    [Fact]
    public void GetAttribute_WithExistingKey_ReturnsValue()
    {
        var actor = CreateActor(attributes: new Dictionary<string, string> { [ActorAttributes.TenantId] = "contoso" });

        actor.GetAttribute(ActorAttributes.TenantId).Should().Be("contoso");
    }

    [Fact]
    public void GetAttribute_WithMissingKey_ReturnsNull()
    {
        var actor = CreateActor();

        actor.GetAttribute(ActorAttributes.TenantId).Should().BeNull();
    }

    #endregion

    #region Immutability

    [Fact]
    public void Record_IsImmutable_WithExpression_CreatesNewInstance()
    {
        var original = CreateActor(id: "user-1", permissions: ["A"]);
        var modified = original with { Id = "user-2" };

        original.Id.Should().Be("user-1");
        modified.Id.Should().Be("user-2");
        modified.Permissions.Should().BeEquivalentTo(original.Permissions);
    }

    #endregion

    #region Hierarchical Inheritance (flattened before construction)

    [Fact]
    public void FlattenedHierarchy_ManagerInheritsEmployeePermissions()
    {
        // Simulates flattening: Manager inherits Employee permissions at hydration time
        var employeePerms = new HashSet<string> { "TimeSheet.Submit", "Expense.Submit" };
        var managerPerms = new HashSet<string> { "TimeSheet.Approve", "Expense.Approve" };
        managerPerms.UnionWith(employeePerms);

        var manager = CreateActor(id: "mgr-1", permissions: managerPerms);

        manager.HasPermission("TimeSheet.Submit").Should().BeTrue("inherited from employee");
        manager.HasPermission("TimeSheet.Approve").Should().BeTrue("own permission");
    }

    #endregion

    #region Role-to-Permission Normalization

    [Fact]
    public void NormalizedPermissions_FromRole_AreCheckedIndividually()
    {
        // Simulates JWT Role "Admin" mapped to granular permissions at hydration time
        var permissions = new HashSet<string> { "User.Create", "User.Delete", "User.Read" };
        var actor = CreateActor(permissions: permissions);

        actor.HasPermission("User.Create").Should().BeTrue();
        actor.HasPermission("User.Delete").Should().BeTrue();
        actor.HasPermission("User.Read").Should().BeTrue();
        actor.HasPermission("Admin").Should().BeFalse("raw role name is not a permission");
    }

    #endregion

    #region Combined Scenarios (Deny + Scope + ABAC)

    [Fact]
    public void CombinedScenario_ScopedPermissionWithDenyAndAttributes()
    {
        var actor = new Actor(
            "user-1",
            new HashSet<string> { "Document.Read:Tenant_A", "Document.Edit:Tenant_A", "Document.Edit:Tenant_B" },
            new HashSet<string> { "Document.Edit:Tenant_A" },
            new Dictionary<string, string> { [ActorAttributes.IpAddress] = "10.0.0.1", [ActorAttributes.MfaAuthenticated] = "true" });

        actor.HasPermission("Document.Read", "Tenant_A").Should().BeTrue();
        actor.HasPermission("Document.Edit", "Tenant_A").Should().BeFalse("explicitly denied");
        actor.HasPermission("Document.Edit", "Tenant_B").Should().BeTrue("not denied");
        actor.HasAttribute(ActorAttributes.IpAddress).Should().BeTrue();
        actor.GetAttribute(ActorAttributes.MfaAuthenticated).Should().Be("true");
    }

    [Fact]
    public void Constructor_MutatingSourceAttributesAfterConstruction_DoesNotChangeActor()
    {
        var attributes = new Dictionary<string, string> { [ActorAttributes.TenantId] = "tenant-a" };
        var actor = new Actor("user-1", new HashSet<string>(), new HashSet<string>(), attributes);

        attributes[ActorAttributes.TenantId] = "tenant-b";
        attributes[ActorAttributes.MfaAuthenticated] = "true";

        actor.GetAttribute(ActorAttributes.TenantId).Should().Be("tenant-a");
        actor.HasAttribute(ActorAttributes.MfaAuthenticated).Should().BeFalse("Actor should snapshot attributes at construction time");
    }

    #endregion
}