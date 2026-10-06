namespace Trellis.Mediator.Tests;

using global::Mediator;
using Folder = ActorHandlers.Consumer.Folder;
using MultiResourceCommand = ActorHandlers.Consumer.MultiResourceCommand;
using Observation = ActorHandlers.Consumer.Observation;
using ConsumerOrder = ActorHandlers.Consumer.Order;

public class AuthorizationDispatchContextTests
{
    [Fact]
    public void GetRequiredActor_OutsideDispatch_ExplainsContextRegistration()
    {
        var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;

        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*StaticCommand*AuthorizationContextBehavior*AddTrellisBehaviors*");
    }

    [Fact]
    public async Task GetRequiredActor_ResolvedCandidate_RequiresStaticSuccess()
    {
        var actor = CreateActor("candidate");
        using var scope = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());

        await AuthorizationDispatchContext.ResolveActorAsync<StaticCommand, Result<string>>(
            new CountingProvider(actor), TestContext.Current.CancellationToken);
        var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;
        read.Should().Throw<InvalidOperationException>().WithMessage("*static authorization*");

        AuthorizationDispatchContext.MarkStaticAuthorized<StaticCommand, Result<string>>();
        read().Should().BeSameAs(actor);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetRequiredActor_CombinedAuthorization_RequiresBothGates(bool staticFirst)
    {
        var actor = CreateActor("candidate");
        using var scope = AuthorizationDispatchContext.Begin<DirectCommand, Result<string>>(new());
        await AuthorizationDispatchContext.ResolveActorAsync<DirectCommand, Result<string>>(
            new CountingProvider(actor), TestContext.Current.CancellationToken);

        if (staticFirst)
            AuthorizationDispatchContext.MarkStaticAuthorized<DirectCommand, Result<string>>();
        else
            AuthorizationDispatchContext.MarkResourceAuthorized<DirectCommand, Resource, Result<string>>();

        var read = AuthorizationDispatchContext.GetRequiredActor<DirectCommand, Result<string>>;
        read.Should().Throw<InvalidOperationException>()
            .WithMessage(staticFirst ? "*resource authorization*" : "*static authorization*");

        if (staticFirst)
            AuthorizationDispatchContext.MarkResourceAuthorized<DirectCommand, Resource, Result<string>>();
        else
            AuthorizationDispatchContext.MarkStaticAuthorized<DirectCommand, Result<string>>();

        read().Should().BeSameAs(actor);
    }

    [Fact]
    public async Task ResolveActorAsync_AbsentActor_CachesAbsenceWithoutAuthorizing()
    {
        var provider = new CountingProvider(null);
        using var scope = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());

        (await AuthorizationDispatchContext.ResolveActorAsync<StaticCommand, Result<string>>(
            provider, TestContext.Current.CancellationToken)).Should().BeNull();
        (await AuthorizationDispatchContext.ResolveActorAsync<StaticCommand, Result<string>>(
            provider, TestContext.Current.CancellationToken)).Should().BeNull();

        provider.Calls.Should().Be(1);
        var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;
        read.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveActorAsync_ConcurrentReads_ResolvesExactlyOnce()
    {
        var actor = CreateActor("candidate");
        var completion = new TaskCompletionSource<Maybe<Actor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new DeferredProvider(completion.Task);
        using var scope = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());

        var first = AuthorizationDispatchContext.ResolveActorAsync<StaticCommand, Result<string>>(
            provider, TestContext.Current.CancellationToken);
        var second = AuthorizationDispatchContext.ResolveActorAsync<StaticCommand, Result<string>>(
            provider, TestContext.Current.CancellationToken);
        provider.Calls.Should().Be(1);
        completion.SetResult(Maybe.From(actor));

        (await first).Should().BeSameAs(actor);
        (await second).Should().BeSameAs(actor);
    }

    [Fact]
    public async Task Begin_NestedSameMessageInstance_CreatesFreshSnapshotAndRestoresParent()
    {
        var message = new StaticCommand();
        var outerActor = CreateActor("outer");
        var innerActor = CreateActor("inner");
        using var outer = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(message);
        await AuthorizeStatic(outerActor);
        var outerIdentity = AuthorizationDispatchContext.CurrentIdentity;

        using (AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(message))
        {
            AuthorizationDispatchContext.CurrentIdentity.Should().NotBeSameAs(outerIdentity);
            var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;
            read.Should().Throw<InvalidOperationException>();
            await AuthorizeStatic(innerActor);
            read().Should().BeSameAs(innerActor);
        }

        AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(outerIdentity);
        AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>().Should().BeSameAs(outerActor);
    }

    [Fact]
    public async Task GetRequiredActor_DifferentInnerMessage_DoesNotFallBackToParent()
    {
        using var outer = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
        await AuthorizeStatic(CreateActor("outer"));
        using var inner = AuthorizationDispatchContext.Begin<OtherCommand, Result<string>>(new());

        var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;
        read.Should().Throw<InvalidOperationException>().WithMessage("*StaticCommand*mismatched*OtherCommand*");
    }

    [Fact]
    public void GetRequiredActor_DifferentResponse_DiagnosesClosedIdentityMismatch()
    {
        using var scope = AuthorizationDispatchContext.Begin<StaticCommand, Result<int>>(new());

        var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;
        read.Should().Throw<InvalidOperationException>().WithMessage("*StaticCommand*mismatched*");
    }

    [Fact]
    public async Task GetRequiredResourceDispatch_WrongLeaf_DiagnosesRegisteredResource()
    {
        using var scope = AuthorizationDispatchContext.Begin<ResourceCommand, Result<string>>(new());
        await AuthorizationDispatchContext.ResolveActorAsync<ResourceCommand, Result<string>>(
            new CountingProvider(CreateActor("actor")), TestContext.Current.CancellationToken);
        AuthorizationDispatchContext.MarkResourceAuthorized<ResourceCommand, Resource, Result<string>>();

        var read = AuthorizationDispatchContext.GetRequiredResourceDispatch<ResourceCommand, OtherResource, Result<string>>;
        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*ResourceCommand*OtherResource*Resource*AddResourceAuthorization*");
    }

    [Fact]
    public async Task GetRequiredResourceDispatch_MultipleSuccessfulTypes_RejectsUnapprovedResource()
    {
        var actor = CreateActor("actor");
        using var scope = AuthorizationDispatchContext.Begin<MultiResourceCommand, Result<Observation>>(
            new("order", new(actor), new(actor)));
        await AuthorizationDispatchContext.ResolveActorAsync<MultiResourceCommand, Result<Observation>>(
            new CountingProvider(actor), TestContext.Current.CancellationToken);
        AuthorizationDispatchContext.MarkResourceAuthorized<MultiResourceCommand, ConsumerOrder, Result<Observation>>();
        AuthorizationDispatchContext.MarkResourceAuthorized<MultiResourceCommand, Folder, Result<Observation>>();
        AuthorizationDispatchContext.MarkResourceAuthorized<MultiResourceCommand, ConsumerOrder, Result<Observation>>();

        AuthorizationDispatchContext.GetRequiredResourceDispatch<MultiResourceCommand, ConsumerOrder, Result<Observation>>()
            .Should().BeSameAs(AuthorizationDispatchContext.CurrentIdentity);
        AuthorizationDispatchContext.GetRequiredResourceDispatch<MultiResourceCommand, Folder, Result<Observation>>()
            .Should().BeSameAs(AuthorizationDispatchContext.CurrentIdentity);
        var read = AuthorizationDispatchContext.GetRequiredResourceDispatch<MultiResourceCommand, OtherResource, Result<Observation>>;
        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*MultiResourceCommand*OtherResource*Order*Folder*AddResourceAuthorization*");
    }

    [Fact]
    public async Task MarkResourceAuthorized_ConcurrentTypes_PreservesBothSuccessfulChecks()
    {
        var actor = CreateActor("actor");
        using var scope = AuthorizationDispatchContext.Begin<MultiResourceCommand, Result<Observation>>(
            new("order", new(actor), new(actor)));
        await AuthorizationDispatchContext.ResolveActorAsync<MultiResourceCommand, Result<Observation>>(
            new CountingProvider(actor), TestContext.Current.CancellationToken);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;

        async Task Authorize<TResource>()
        {
            if (Interlocked.Increment(ref entered) == 2)
                bothEntered.SetResult();
            await bothEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            AuthorizationDispatchContext.MarkResourceAuthorized<MultiResourceCommand, TResource, Result<Observation>>();
        }

        await Task.WhenAll(Authorize<ConsumerOrder>(), Authorize<Folder>());

        AuthorizationDispatchContext.GetRequiredResourceDispatch<MultiResourceCommand, ConsumerOrder, Result<Observation>>()
            .Should().BeSameAs(AuthorizationDispatchContext.CurrentIdentity);
        AuthorizationDispatchContext.GetRequiredResourceDispatch<MultiResourceCommand, Folder, Result<Observation>>()
            .Should().BeSameAs(AuthorizationDispatchContext.CurrentIdentity);
    }

    [Fact]
    public async Task Begin_ConcurrentSiblings_IsolatesFramesAndPreservesParent()
    {
        var outerActor = CreateActor("outer");
        using var outer = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
        await AuthorizeStatic(outerActor);
        var outerIdentity = AuthorizationDispatchContext.CurrentIdentity;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var identities = new object?[2];

        await Task.WhenAll(Enumerable.Range(0, 2).Select(async index =>
        {
            using var scope = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
            var actor = CreateActor($"sibling-{index}");
            await AuthorizeStatic(actor);
            identities[index] = AuthorizationDispatchContext.CurrentIdentity;
            if (Interlocked.Increment(ref entered) == 2)
                bothEntered.SetResult();
            await bothEntered.Task;
            AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>().Should().BeSameAs(actor);
        }));

        identities[0].Should().NotBeSameAs(identities[1]).And.NotBeSameAs(outerIdentity);
        AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(outerIdentity);
        AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>().Should().BeSameAs(outerActor);
    }

    [Fact]
    public async Task Dispose_ExpiredInnerCapturedByOrphan_DoesNotFallBackToActiveParent()
    {
        using var outer = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
        await AuthorizeStatic(CreateActor("outer"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task orphan;
        using (AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new()))
        {
            await AuthorizeStatic(CreateActor("inner"));
            orphan = Task.Run(async () =>
            {
                await release.Task;
                var read = AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>;
                read.Should().Throw<InvalidOperationException>().WithMessage("*StaticCommand*expired*");
            }, TestContext.Current.CancellationToken);
        }

        release.SetResult();
        await orphan;
        AuthorizationDispatchContext.GetRequiredActor<StaticCommand, Result<string>>().Id.Value.Should().Be("outer");
    }

    [Fact]
    public void Dispose_DoubleDisposal_DoesNotPopParent()
    {
        using var outer = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
        var outerIdentity = AuthorizationDispatchContext.CurrentIdentity;
        using var inner = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());

        inner.Dispose();
        inner.Dispose();

        AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(outerIdentity);
    }

    [Fact]
    public void Dispose_OutOfOrder_ThrowsWithoutCorruptingFrames()
    {
        using var outer = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
        using var inner = AuthorizationDispatchContext.Begin<StaticCommand, Result<string>>(new());
        var innerIdentity = AuthorizationDispatchContext.CurrentIdentity;

        var dispose = outer.Dispose;
        dispose.Should().Throw<InvalidOperationException>().WithMessage("*reverse order*");
        AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(innerIdentity);

        inner.Dispose();
        outer.Dispose();
        AuthorizationDispatchContext.CurrentIdentity.Should().BeNull();
    }

    private static Actor CreateActor(string id) => Actor.Create(id, new HashSet<string>());

    private static async Task AuthorizeStatic(Actor actor)
    {
        await AuthorizationDispatchContext.ResolveActorAsync<StaticCommand, Result<string>>(
            new CountingProvider(actor), TestContext.Current.CancellationToken);
        AuthorizationDispatchContext.MarkStaticAuthorized<StaticCommand, Result<string>>();
    }

    private sealed class CountingProvider(Actor? actor) : IActorProvider
    {
        public int Calls { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Maybe.From(actor));
        }
    }

    private sealed class DeferredProvider(Task<Maybe<Actor>> completion) : IActorProvider
    {
        public int Calls { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return completion;
        }
    }

    private sealed record StaticCommand : ICommand<Result<string>>, IAuthorize
    {
        public IReadOnlyList<string> RequiredPermissions => [];
    }

    private sealed record OtherCommand : ICommand<Result<string>>, IAuthorize
    {
        public IReadOnlyList<string> RequiredPermissions => [];
    }

    private sealed record DirectCommand : ICommand<Result<string>>, IAuthorize, IAuthorizeResource<Resource>
    {
        public IReadOnlyList<string> RequiredPermissions => [];
        public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
    }

    private sealed record ResourceCommand : ICommand<Result<string>>, IAuthorizeResource<Resource>
    {
        public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
    }

    private sealed record Resource;
    private sealed record OtherResource;
}
