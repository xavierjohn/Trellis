namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for the construction-time invariants of indirect authorization primitives:
/// <see cref="HopLoadResult"/>, <see cref="ResolvedAuthorizationPath"/>, and
/// the type-agreement guards in <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/>'s
/// constructor. The runtime walk itself is covered by
/// <see cref="ResourceAuthorizationViaBehaviorTests"/>.
/// </summary>
public class ResourceAuthorizationViaInvariantsTests
{
    #region HopLoadResult invariants

    [Fact]
    public void HopLoadResult_Default_IsFailureNotSuccess()
    {
        // default(HopLoadResult) must never be mistaken for a successful load —
        // a misconfigured hop loader returning default would otherwise pass a null
        // value through the pipeline and bypass the empty-list short-circuit.
        var def = default(HopLoadResult);

        def.IsSuccess.Should().BeFalse();
        def.Value.Should().BeNull();
        def.Error.Should().NotBeNull();
        def.Error!.Code.Should().Be("resource.authorization-via.hop-uninitialized");
    }

    [Fact]
    public void HopLoadResult_Success_NullValue_Throws()
    {
        // ReSharper disable once AssignNullToNotNullAttribute — deliberately exercising null path.
        var act = () => HopLoadResult.Success(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HopLoadResult_Failure_NullError_Throws()
    {
        var act = () => HopLoadResult.Failure(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HopLoadResult_Success_RoundTripsValue()
    {
        var owner = new TestOwner("o-1", "actor");

        var r = HopLoadResult.Success(owner);

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().BeSameAs(owner);
        r.Error.Should().BeNull();
    }

    #endregion

    #region ResolvedAuthorizationPath invariants

    [Fact]
    public void ResolvedAuthorizationPath_EmptyHops_Throws()
    {
        var act = () => new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: []);

        act.Should().Throw<ArgumentException>().WithMessage("*at least one hop*");
    }

    [Fact]
    public void ResolvedAuthorizationPath_FirstHopFromTypeMismatch_Throws()
    {
        // Path declares leafType = TestLeaf but the first hop starts from TestOwner.
        var hop = MakeIdentityHop(typeof(TestOwner), typeof(TestOwner), isPlural: false);

        var act = () => new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: [hop]);

        act.Should().Throw<ArgumentException>().WithMessage("*First hop*FromType*");
    }

    [Fact]
    public void ResolvedAuthorizationPath_TerminalHopToTypeMismatch_Throws()
    {
        // Single hop TestLeaf -> Mid but path declares ownerType = TestOwner.
        var hop = MakeIdentityHop(typeof(TestLeaf), typeof(Mid), isPlural: false);

        var act = () => new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: [hop]);

        act.Should().Throw<ArgumentException>().WithMessage("*Terminal hop*ToType*");
    }

    [Fact]
    public void ResolvedAuthorizationPath_AdjacentHopsDoNotChain_Throws()
    {
        // Chain breaks: TestLeaf -> Mid, then Other -> TestOwner. hops[0].ToType != hops[1].FromType.
        var hop1 = MakeIdentityHop(typeof(TestLeaf), typeof(Mid), isPlural: false);
        var hop2 = MakeIdentityHop(typeof(Other), typeof(TestOwner), isPlural: false);

        var act = () => new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: [hop1, hop2]);

        act.Should().Throw<ArgumentException>().WithMessage("*Hops do not chain*");
    }

    [Fact]
    public void ResolvedAuthorizationPath_PluralHopInMiddle_Throws()
    {
        // Plural hops are only valid at the terminal position; v1 rejects plural-in-middle.
        var hop1 = MakeIdentityHop(typeof(TestLeaf), typeof(Mid), isPlural: true);
        var hop2 = MakeIdentityHop(typeof(Mid), typeof(TestOwner), isPlural: false);

        var act = () => new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: [hop1, hop2]);

        act.Should().Throw<ArgumentException>().WithMessage("*Plural hop*terminal*");
    }

    [Fact]
    public void ResolvedAuthorizationPath_DefensiveCopy_CallerMutationDoesNotAffectPath()
    {
        var hop1 = MakeIdentityHop(typeof(TestLeaf), typeof(TestOwner), isPlural: false);
        var hopList = new List<ResolvedAuthorizationHop> { hop1 };

        var path = new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: hopList);

        // Mutate the caller's list; path.Hops must be unaffected.
        hopList.Add(MakeIdentityHop(typeof(Other), typeof(Other), isPlural: false));

        path.Hops.Should().HaveCount(1);
    }

    #endregion

    #region Behavior constructor — type-agreement guards (defense against unkeyed DI registration)

    [Fact]
    public void Behavior_Constructor_PathMessageTypeMismatch_Throws()
    {
        // Path is for AnotherCommand but the behavior is closed over SingleHopCommand.
        var path = new ResolvedAuthorizationPath(
            messageType: typeof(AnotherCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: [MakeIdentityHop(typeof(TestLeaf), typeof(TestOwner), isPlural: false)]);

        var act = () => new ResourceAuthorizationViaBehavior<SingleHopCommand, TestLeaf, TestOwner, Result<string>>(
            FakeActorProvider.NoPermissions("a"),
            new ServiceCollection().BuildServiceProvider(),
            path);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*path is for AnotherCommand*TMessage = SingleHopCommand*");
    }

    [Fact]
    public void Behavior_Constructor_PathLeafTypeMismatch_Throws()
    {
        // Build a path whose internal hops use TestLeaf but where the path's leafType is Mid.
        // We can't actually construct such a path because the path's own ctor cross-checks
        // hop[0].FromType == leafType. So construct a valid path with leafType=Mid and
        // hand it to a behavior closed over TLeaf=TestLeaf.
        var path = new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(Mid),
            ownerType: typeof(TestOwner),
            hops: [MakeIdentityHop(typeof(Mid), typeof(TestOwner), isPlural: false)]);

        var act = () => new ResourceAuthorizationViaBehavior<SingleHopCommand, TestLeaf, TestOwner, Result<string>>(
            FakeActorProvider.NoPermissions("a"),
            new ServiceCollection().BuildServiceProvider(),
            path);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*leaf type is Mid*TLeaf = TestLeaf*");
    }

    [Fact]
    public void Behavior_Constructor_PathOwnerTypeMismatch_Throws()
    {
        var path = new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(Mid),
            hops: [MakeIdentityHop(typeof(TestLeaf), typeof(Mid), isPlural: false)]);

        var act = () => new ResourceAuthorizationViaBehavior<SingleHopCommand, TestLeaf, TestOwner, Result<string>>(
            FakeActorProvider.NoPermissions("a"),
            new ServiceCollection().BuildServiceProvider(),
            path);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*owner type is Mid*TOwner = TestOwner*");
    }

    #endregion

    #region Helpers

    private static ResolvedAuthorizationHop MakeIdentityHop(Type from, Type to, bool isPlural) =>
        new(
            fromType: from,
            toType: to,
            toIdType: typeof(string),
            extractIds: _ => [],
            loadAsync: (_, _, _) => Task.FromResult(HopLoadResult.Failure(new Error.NotFound(new ResourceRef("test", null)))),
            isPlural: isPlural);

    /// <summary>Leaf type used by the behavior.</summary>
    public sealed record TestLeaf(string Id) : IIdentifyRelatedResource<TestOwner, string>
    {
        // Resolver path: TestLeaf -> TestOwner via this single-FK navigation.
        public string GetRelatedResourceId() => Id;
    }

    /// <summary>Owner type used by the behavior.</summary>
    public sealed record TestOwner(string Id, string Owner);

    /// <summary>Intermediate / wrong type used to construct invalid paths.</summary>
    public sealed record Mid(string Id);

    /// <summary>A second wrong type used to construct broken chains.</summary>
    public sealed record Other(string Id);

    /// <summary>Synthetic via-authorized command used to close behavior generics.</summary>
    public sealed record SingleHopCommand(string Id)
        : global::Mediator.ICommand<Result<string>>,
          IAuthorizeResourceVia<TestOwner>,
          IIdentifyResource<TestLeaf, string>
    {
        public string GetResourceId() => Id;
        public IResult Authorize(Actor actor, IReadOnlyList<TestOwner> owners) => Result.Ok();
    }

    /// <summary>Second synthetic command for cross-message validation.</summary>
    public sealed record AnotherCommand(string Id)
        : global::Mediator.ICommand<Result<string>>,
          IAuthorizeResourceVia<TestOwner>,
          IIdentifyResource<TestLeaf, string>
    {
        public string GetResourceId() => Id;
        public IResult Authorize(Actor actor, IReadOnlyList<TestOwner> owners) => Result.Ok();
    }

    #endregion
}