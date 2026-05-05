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

    #region Argument-null guards (inspection findings m-2 + m-3)

    /// <summary>
    /// Inspection finding m-2: <see cref="Actor"/>'s constructor previously deferred null-checks
    /// on the three collection parameters to the snapshot helpers, which surfaced as confusing
    /// <see cref="NullReferenceException"/>s with no parameter name. Public APIs in Trellis
    /// uniformly throw <see cref="ArgumentNullException"/> with the offending parameter name.
    /// </summary>
    [Theory]
    [InlineData("permissions")]
    [InlineData("forbiddenPermissions")]
    [InlineData("attributes")]
    public void Constructor_NullCollection_ThrowsArgumentNullException(string nullParameterName)
    {
        var act = () => new Actor(
            "user-1",
            permissions: nullParameterName == "permissions" ? null! : new HashSet<string>(),
            forbiddenPermissions: nullParameterName == "forbiddenPermissions" ? null! : new HashSet<string>(),
            attributes: nullParameterName == "attributes" ? null! : new Dictionary<string, string>());

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == nullParameterName);
    }

    [Fact]
    public void Create_NullPermissions_ThrowsArgumentNullException()
    {
        var act = () => Actor.Create("user-1", permissions: null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "permissions");
    }

    [Fact]
    public void HasPermission_NullPermission_ThrowsArgumentNullException()
    {
        var actor = CreateActor();

        var act = () => actor.HasPermission(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "permission");
    }

    [Theory]
    [InlineData(null, "scope")]
    [InlineData("permission", null)]
    public void HasPermission_Scoped_NullArgument_ThrowsArgumentNullException(string? permission, string? scope)
    {
        var actor = CreateActor();

        var act = () => actor.HasPermission(permission!, scope!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HasAllPermissions_NullEnumerable_ThrowsArgumentNullException()
    {
        var actor = CreateActor();

        var act = () => actor.HasAllPermissions(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "permissions");
    }

    [Fact]
    public void HasAnyPermission_NullEnumerable_ThrowsArgumentNullException()
    {
        var actor = CreateActor();

        var act = () => actor.HasAnyPermission(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "permissions");
    }

    [Fact]
    public void IsOwner_NullResourceOwnerId_ThrowsArgumentNullException()
    {
        var actor = CreateActor();

        var act = () => actor.IsOwner(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resourceOwnerId");
    }

    [Fact]
    public void HasAttribute_NullKey_ThrowsArgumentNullException()
    {
        var actor = CreateActor();

        var act = () => actor.HasAttribute(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "key");
    }

    [Fact]
    public void GetAttribute_NullKey_ThrowsArgumentNullException()
    {
        var actor = CreateActor();

        var act = () => actor.GetAttribute(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "key");
    }

    #endregion

    #region Structural equality (inspection finding m-1 + i-1)

    /// <summary>
    /// Inspection finding m-1: <see cref="Actor"/> is declared <c>sealed record</c> so the
    /// compiler synthesises structural <see cref="object.Equals(object)"/> /
    /// <see cref="object.GetHashCode"/>. Two of the three collection-typed properties
    /// (<see cref="Actor.Permissions"/>, <see cref="Actor.ForbiddenPermissions"/>,
    /// <see cref="Actor.Attributes"/>) are interface types whose default equality
    /// comparer falls back to reference equality — so two actors built from identical
    /// inputs would compare unequal. The fix overrides <c>Equals</c> / <c>GetHashCode</c>
    /// to compare the snapshots structurally so the <c>record</c> contract holds.
    /// </summary>
    [Fact]
    public void Equals_TwoActorsWithIdenticalState_AreEqual()
    {
        var a1 = new Actor(
            "user-1",
            new HashSet<string> { "Orders.Read", "Orders.Write" },
            new HashSet<string> { "Orders.Delete" },
            new Dictionary<string, string> { [ActorAttributes.TenantId] = "tenant-a" });

        var a2 = new Actor(
            "user-1",
            new HashSet<string> { "Orders.Read", "Orders.Write" },
            new HashSet<string> { "Orders.Delete" },
            new Dictionary<string, string> { [ActorAttributes.TenantId] = "tenant-a" });

        a1.Equals(a2).Should().BeTrue("the record's structural equality should compare collections by content");
        (a1 == a2).Should().BeTrue();
        a1.GetHashCode().Should().Be(a2.GetHashCode(), "equal objects must have equal hash codes");
    }

    [Fact]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var a1 = Actor.Create("user-1", new HashSet<string> { "X" });
        var a2 = Actor.Create("user-2", new HashSet<string> { "X" });

        a1.Equals(a2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentPermissionContent_ReturnsFalse()
    {
        var a1 = Actor.Create("user-1", new HashSet<string> { "X" });
        var a2 = Actor.Create("user-1", new HashSet<string> { "Y" });

        a1.Equals(a2).Should().BeFalse();
    }

    [Fact]
    public void Equals_PermissionsSameContentDifferentOrder_ReturnsTrue()
    {
        var a1 = Actor.Create("user-1", new HashSet<string> { "A", "B", "C" });
        var a2 = Actor.Create("user-1", new HashSet<string> { "C", "B", "A" });

        a1.Equals(a2).Should().BeTrue("set equality is order-independent");
    }

    [Fact]
    public void Equals_DifferentForbiddenPermissions_ReturnsFalse()
    {
        var a1 = new Actor("user-1", new HashSet<string> { "X" }, new HashSet<string>(), new Dictionary<string, string>());
        var a2 = new Actor("user-1", new HashSet<string> { "X" }, new HashSet<string> { "X" }, new Dictionary<string, string>());

        a1.Equals(a2).Should().BeFalse("ForbiddenPermissions participates in equality");
    }

    [Fact]
    public void Equals_DifferentAttributeValues_ReturnsFalse()
    {
        var a1 = new Actor("user-1", new HashSet<string>(), new HashSet<string>(),
            new Dictionary<string, string> { ["k"] = "v1" });
        var a2 = new Actor("user-1", new HashSet<string>(), new HashSet<string>(),
            new Dictionary<string, string> { ["k"] = "v2" });

        a1.Equals(a2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentAttributeKeys_ReturnsFalse()
    {
        var a1 = new Actor("user-1", new HashSet<string>(), new HashSet<string>(),
            new Dictionary<string, string> { ["k1"] = "v" });
        var a2 = new Actor("user-1", new HashSet<string>(), new HashSet<string>(),
            new Dictionary<string, string> { ["k2"] = "v" });

        a1.Equals(a2).Should().BeFalse();
    }

    [Fact]
    public void Equals_NullOther_ReturnsFalse()
    {
        var actor = Actor.Create("user-1", new HashSet<string>());

        actor.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_SameReference_ReturnsTrue()
    {
        var actor = Actor.Create("user-1", new HashSet<string> { "X" });

        actor.Equals(actor).Should().BeTrue();
    }

    #endregion
}