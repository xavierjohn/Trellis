namespace Trellis.Testing.Tests;

using Trellis.Authorization;
using Xunit.Sdk;

public class ActorAssertionsTests
{
    private static Actor Make(
        string id = "alice",
        string[]? permissions = null,
        string[]? forbidden = null,
        Dictionary<string, string>? attributes = null) =>
        new(id,
            new HashSet<string>(permissions ?? []),
            new HashSet<string>(forbidden ?? []),
            attributes ?? []);

    private sealed record ReadOrders : IAuthorize
    {
        public IReadOnlyList<string> RequiredPermissions { get; } = ["Orders.Read", "Orders.List"];
    }

    [Fact]
    public void HavePermission_Granted_Passes() =>
        Make(permissions: ["Orders.Read"]).Should().HavePermission("Orders.Read");

    [Fact]
    public void HavePermission_NeverGranted_FailsSaying_NotGranted()
    {
        var act = () => Make(permissions: ["Orders.Read"]).Should().HavePermission("Orders.Write");

        act.Should().Throw<XunitException>().WithMessage("*Orders.Write*not granted*");
    }

    [Fact]
    public void HavePermission_GrantedButForbidden_FailsSaying_Forbidden()
    {
        var actor = Make(permissions: ["Orders.Write"], forbidden: ["Orders.Write"]);

        var act = () => actor.Should().HavePermission("Orders.Write");

        act.Should().Throw<XunitException>().WithMessage("*Orders.Write*forbidden*");
    }

    [Fact]
    public void HaveScopedPermission_UsesScopeSeparator()
    {
        var actor = Make(permissions: ["Orders.Read:tenant-1"]);

        actor.Should().HaveScopedPermission("Orders.Read", "tenant-1");
        var act = () => actor.Should().HaveScopedPermission("Orders.Read", "tenant-2");
        act.Should().Throw<XunitException>().WithMessage("*Orders.Read:tenant-2*");
    }

    [Fact]
    public void NotHavePermission_Absent_Passes_And_Present_Fails()
    {
        var actor = Make(permissions: ["Orders.Read"]);

        actor.Should().NotHavePermission("Orders.Write");
        var act = () => actor.Should().NotHavePermission("Orders.Read");
        act.Should().Throw<XunitException>().WithMessage("*Orders.Read*");
    }

    [Fact]
    public void NotHavePermission_ForbiddenPermission_Passes() =>
        Make(permissions: ["Orders.Write"], forbidden: ["Orders.Write"]).Should().NotHavePermission("Orders.Write");

    [Fact]
    public void HaveAllPermissions_ListsEveryMissingPermission()
    {
        var actor = Make(permissions: ["A"]);

        actor.Should().HaveAllPermissions(["A"]);
        var act = () => actor.Should().HaveAllPermissions(["A", "B", "C"]);
        act.Should().Throw<XunitException>().WithMessage("*B*C*");
    }

    [Fact]
    public void HaveAnyPermission_FailsWhenNoneMatch()
    {
        var actor = Make(permissions: ["A"]);

        actor.Should().HaveAnyPermission(["X", "A"]);
        var act = () => actor.Should().HaveAnyPermission(["X", "Y"]);
        act.Should().Throw<XunitException>().WithMessage("*X*Y*");
    }

    [Fact]
    public void HaveId_And_BeOwnerOf_CompareActorIdentity()
    {
        var actor = Make("alice");

        actor.Should().HaveId("alice").And.BeOwnerOf(ActorId.Create("alice"));
        var wrongId = () => actor.Should().HaveId("bob");
        var wrongOwner = () => actor.Should().BeOwnerOf(ActorId.Create("bob"));

        wrongId.Should().Throw<XunitException>().WithMessage("*bob*alice*");
        wrongOwner.Should().Throw<XunitException>().WithMessage("*bob*");
    }

    [Fact]
    public void HaveAttribute_ChecksKeyAndOptionalValue()
    {
        var actor = Make(attributes: new() { ["tenant"] = "t-1" });

        actor.Should().HaveAttribute("tenant").And.HaveAttributeValue("tenant", "t-1");
        var missing = () => actor.Should().HaveAttribute("region");
        var wrongValue = () => actor.Should().HaveAttributeValue("tenant", "t-2");

        missing.Should().Throw<XunitException>().WithMessage("*region*");
        wrongValue.Should().Throw<XunitException>().WithMessage("*t-2*t-1*");
    }

    [Fact]
    public void NullActor_ReportsAssertionFailure()
    {
        var act = () => ((Actor)null!).Should().HavePermission("x");

        act.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRequirePermissions_ExactSetIgnoringOrder_Passes() =>
        new ReadOrders().ShouldRequirePermissions("Orders.List", "Orders.Read");

    [Fact]
    public void ShouldRequirePermissions_MissingAndExtra_AreBothReported()
    {
        var act = () => new ReadOrders().ShouldRequirePermissions("Orders.Read", "Orders.Write");

        act.Should().Throw<XunitException>().WithMessage("*Orders.Write*Orders.List*");
    }

    [Fact]
    public void ShouldBeAuthorizedFor_MatchesThePipelineAllPermissionsRule()
    {
        var message = new ReadOrders();
        var allowed = Make(permissions: ["Orders.Read", "Orders.List"]);
        var denied = Make(permissions: ["Orders.Read"]);

        message.ShouldBeAuthorizedFor(allowed);
        message.ShouldNotBeAuthorizedFor(denied);
        var wrongAllow = () => message.ShouldBeAuthorizedFor(denied);
        var wrongDeny = () => message.ShouldNotBeAuthorizedFor(allowed);

        wrongAllow.Should().Throw<XunitException>().WithMessage("*Orders.List*");
        wrongDeny.Should().Throw<XunitException>().WithMessage("*all*required*");
    }

    [Fact]
    public void ShouldBeAuthorizedFor_ForbiddenPermission_DeniesLikeThePipeline()
    {
        var actor = Make(permissions: ["Orders.Read", "Orders.List"], forbidden: ["Orders.List"]);

        new ReadOrders().ShouldNotBeAuthorizedFor(actor);
    }
}