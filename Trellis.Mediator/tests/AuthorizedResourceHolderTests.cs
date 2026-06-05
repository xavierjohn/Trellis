namespace Trellis.Mediator.Tests;

using global::Mediator;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="AuthorizedResourceHolder{TMessage, TResource}"/>, the internal
/// scoped accessor that backs <see cref="IAuthorizedResource{TMessage, TResource}"/>.
/// Focused on the AsyncLocal copy-on-push lifecycle correctness — the load-bearing
/// piece that GPT-5.5 review round 2 finding 1 identified as essential for correct
/// behavior under nested <c>mediator.Send</c> and concurrent <c>Task.WhenAll</c>
/// dispatch in the same DI scope. Pipeline-level integration is exercised by
/// <see cref="ResourceAuthorizationBehaviorTests"/> and
/// <see cref="ResourceAuthorizationViaBehaviorTests"/>.
/// </summary>
public class AuthorizedResourceHolderTests
{
    #region GetRequired / TryGet outside dispatch

    [Fact]
    public void GetRequired_OutsideAnyPush_ThrowsWithDiagnosticMessage()
    {
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();

        var act = () => holder.GetRequired();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HolderTestCommand*")
            .WithMessage("*TestResource*")
            .WithMessage("*AddResourceAuthorization*");
    }

    [Fact]
    public void TryGet_OutsideAnyPush_ReturnsFalseAndOutNull()
    {
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();

        var found = holder.TryGet(out var resource);

        found.Should().BeFalse();
        resource.Should().BeNull();
    }

    #endregion

    #region Single push / pop

    [Fact]
    public void Push_ThenGetRequired_ReturnsPushedInstance()
    {
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        var resource = new TestResource("r1", "owner-1");

        using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(resource))
        {
            holder.GetRequired().Should().BeSameAs(resource);
            holder.TryGet(out var got).Should().BeTrue();
            got.Should().BeSameAs(resource);
        }

        // After dispose, no resource in scope.
        holder.TryGet(out _).Should().BeFalse();
    }

    [Fact]
    public void Push_NullResource_ThrowsArgumentNullException()
    {
        var act = () => AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resource");
    }

    [Fact]
    public void ScopeToken_DoubleDispose_IsIdempotent()
    {
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        var outer = new TestResource("outer", "owner");
        var inner = new TestResource("inner", "owner");

        using var outerToken = AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(outer);

        var innerToken = AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(inner);
        innerToken.Dispose();
        innerToken.Dispose(); // second dispose must not corrupt the AsyncLocal

        // Outer should still be in scope; double-pop would have unwound back to "no value".
        holder.GetRequired().Should().BeSameAs(outer);
    }

    #endregion

    #region Nested push / pop (mirrors nested mediator.Send)

    [Fact]
    public void NestedPush_GetRequired_ReturnsTopOfStack()
    {
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        var outer = new TestResource("outer", "owner");
        var inner = new TestResource("inner", "owner");

        using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(outer))
        {
            holder.GetRequired().Should().BeSameAs(outer);

            using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(inner))
            {
                holder.GetRequired().Should().BeSameAs(inner,
                    "the most-recently-pushed resource is the active one — nested mediator.Send semantics");
            }

            holder.GetRequired().Should().BeSameAs(outer,
                "popping the inner push restores the outer dispatch's resource");
        }

        holder.TryGet(out _).Should().BeFalse();
    }

    [Fact]
    public async Task NestedPush_AcrossAwaitPoint_RestoresOuterAfterInnerCompletes()
    {
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        var outer = new TestResource("outer", "owner");
        var inner = new TestResource("inner", "owner");

        using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(outer))
        {
            await Task.Yield();
            holder.GetRequired().Should().BeSameAs(outer);

            using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(inner))
            {
                await Task.Yield();
                holder.GetRequired().Should().BeSameAs(inner);
            }

            await Task.Yield();
            holder.GetRequired().Should().BeSameAs(outer,
                "AsyncLocal flow must restore the outer value after the inner push's using-block disposes, even across awaits");
        }
    }

    #endregion

    #region Parallel dispatch — load-bearing AsyncLocal copy-on-push correctness

    [Fact]
    public async Task ParallelPushes_OfDifferentResources_DoNotCrossContaminate()
    {
        // This is the GPT-5.5 round-2 blocking-finding scenario:
        // parallel mediator.Send of the same closed pair (TMessage, TResource) with
        // different resources must NOT see each other's pushes. AsyncLocal flows the
        // *reference*; mutating a shared Stack<T> would cross-contaminate. The
        // copy-on-push implementation must defend against this.
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        const int parallelism = 32;
        var observed = new TestResource?[parallelism];

        var ct = TestContext.Current.CancellationToken;
        var tasks = new Task[parallelism];
        for (int i = 0; i < parallelism; i++)
        {
            int local = i; // capture
            tasks[i] = Task.Run(async () =>
            {
                var mine = new TestResource($"r{local}", "owner");
                using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(mine))
                {
                    // Yield several times to maximize the chance of interleaving with siblings.
                    for (int y = 0; y < 4; y++) await Task.Yield();
                    observed[local] = holder.GetRequired();
                }
            }, ct);
        }

        await Task.WhenAll(tasks);

        for (int i = 0; i < parallelism; i++)
            observed[i].Should().NotBeNull()
                .And.Subject.As<TestResource>().Id.Should().Be($"r{i}",
                    $"task {i} pushed r{i} and must observe only r{i} despite {parallelism - 1} concurrent siblings");

        // After all tasks complete and dispose their pushes, the holder is empty in the parent flow.
        holder.TryGet(out _).Should().BeFalse();
    }

    [Fact]
    public async Task ParallelChildren_InheritParentStack_AndDoNotMutateIt()
    {
        // Variation: the parent has already pushed before forking. Each child should
        // see the parent's push as the bottom of its own stack, push its own resource
        // on top, then dispose. The parent's view after WhenAll must be unchanged.
        var holder = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        var parentResource = new TestResource("parent", "owner");

        using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(parentResource))
        {
            holder.GetRequired().Should().BeSameAs(parentResource);

            var ct = TestContext.Current.CancellationToken;
            var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(async () =>
            {
                var child = new TestResource($"child-{i}", "owner");
                holder.GetRequired().Should().BeSameAs(parentResource,
                    "at fork time the child inherits the parent's AsyncLocal value");

                using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(child))
                {
                    for (int y = 0; y < 4; y++) await Task.Yield();
                    holder.GetRequired().Should().BeSameAs(child);
                }

                holder.GetRequired().Should().BeSameAs(parentResource,
                    "after disposing the child's push, the child sees the parent's resource again");
            }, ct)).ToArray();

            await Task.WhenAll(tasks);

            holder.GetRequired().Should().BeSameAs(parentResource,
                "after all children complete, the parent's view is unchanged by sibling forks");
        }
    }

    #endregion

    #region Cross-pair isolation

    [Fact]
    public void DifferentClosedPairs_HaveIndependentStacks()
    {
        // Each closed AuthorizedResourceHolder<TMessage, TResource> has its own static
        // AsyncLocal field. Pushing on one closed pair must not be observable on another.
        var holderA = new AuthorizedResourceHolder<HolderTestCommand, TestResource>();
        var holderB = new AuthorizedResourceHolder<OtherHolderTestCommand, TestResource>();

        var a = new TestResource("a", "owner");
        using (AuthorizedResourceHolder<HolderTestCommand, TestResource>.Push(a))
        {
            holderA.TryGet(out _).Should().BeTrue();
            holderB.TryGet(out _).Should().BeFalse(
                "a different closed pair has its own AsyncLocal-backed stack");
        }
    }

    #endregion

    #region Test fixtures

    private sealed record HolderTestCommand(string ResourceId)
        : ICommand<Result<string>>, IAuthorizeResource<TestResource>
    {
        public IResult Authorize(Actor actor, TestResource resource) => Result.Ok();
    }

    private sealed record OtherHolderTestCommand(string ResourceId)
        : ICommand<Result<string>>, IAuthorizeResource<TestResource>
    {
        public IResult Authorize(Actor actor, TestResource resource) => Result.Ok();
    }

    #endregion
}
