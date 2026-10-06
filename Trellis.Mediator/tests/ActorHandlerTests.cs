namespace Trellis.Mediator.Tests;

using global::Mediator;
using System.Reflection;
using Trellis.Testing;

public class ActorHandlerTests
{
    [Theory]
    [InlineData(typeof(ActorCommandHandler<,>), 3)]
    [InlineData(typeof(ActorQueryHandler<,>), 3)]
    [InlineData(typeof(ActorResourceCommandHandler<,,>), 4)]
    [InlineData(typeof(ActorResourceQueryHandler<,,>), 4)]
    [InlineData(typeof(ActorResourceViaCommandHandler<,,,>), 4)]
    [InlineData(typeof(ActorResourceViaQueryHandler<,,,>), 4)]
    public void HandlerShape_AllBases_HaveParameterlessConstructorsNoSnapshotFieldsAndNonOverridableEntries(Type handler, int explicitParameters)
    {
        handler.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Should().ContainSingle(constructor => constructor.GetParameters().Length == 0);
        handler.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Should().BeEmpty();
        var entries = handler.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "Handle").ToArray();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(method => !method.IsVirtual || method.IsFinal);
        entries.Select(method => method.GetParameters().Length).Should().BeEquivalentTo([2, explicitParameters]);
        handler.GetMethod("HandleCore", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .IsAbstract.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Handle_NormalEntry_ForwardsAuthorizedArgumentsAndAsyncOutcome(int variant)
    {
        var business = new Business();
        var harness = CreateHarness(variant, business);
        var actor = CreateActor("checked");
        var resource = new Resource("loaded");
        using var cancellation = new CancellationTokenSource();

        await harness.InDispatch(actor, resource, true, async () =>
        {
            var result = await harness.Normal(cancellation.Token);
            var observation = result.Unwrap();
            observation.Message.Should().BeSameAs(harness.Message);
            observation.Actor.Should().BeSameAs(actor);
            observation.Resource.Should().BeSameAs(harness.HasResource ? resource : null);
            observation.Token.Should().Be(cancellation.Token);
        });

        business.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Handle_NormalEntry_MissingOrPendingContext_DoesNotRunBusinessLogic(int variant)
    {
        var business = new Business();
        var harness = CreateHarness(variant, business);
        var invoke = async () => await harness.Normal(TestContext.Current.CancellationToken);

        await invoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Mediator*explicit*");
        await harness.InDispatch(CreateActor("candidate"), new Resource("loaded"), false, async () =>
            await invoke.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage(harness.HasResource ? "*resource authorization*" : "*static authorization*"));

        business.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Handle_ExplicitEntry_UsesSuppliedArgumentsWithoutAmbientState(int variant)
    {
        var business = new Business();
        var harness = CreateHarness(variant, business);
        var actor = CreateActor("supplied");
        var resource = new Resource("supplied");
        using var cancellation = new CancellationTokenSource();

        var result = await harness.Explicit(actor, resource, cancellation.Token);

        result.Unwrap().Actor.Should().BeSameAs(actor);
        result.Unwrap().Resource.Should().BeSameAs(harness.HasResource ? resource : null);
        result.Unwrap().Token.Should().Be(cancellation.Token);
        AuthorizationDispatchContext.CurrentIdentity.Should().BeNull();

        await harness.InDispatch(CreateActor("ambient"), new Resource("ambient"), true, async () =>
        {
            var identity = AuthorizationDispatchContext.CurrentIdentity;
            var explicitResult = await harness.Explicit(actor, resource, cancellation.Token);
            explicitResult.Unwrap().Actor.Should().BeSameAs(actor);
            explicitResult.Unwrap().Resource.Should().BeSameAs(harness.HasResource ? resource : null);
            AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(identity);
            (await harness.Normal(cancellation.Token)).Unwrap().Actor.Id.Value.Should().Be("ambient");
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Handle_ExplicitEntry_NullArguments_FailsBeforeBusinessLogic(int variant)
    {
        var business = new Business();
        var harness = CreateHarness(variant, business);

        var nullActor = async () => await harness.Explicit(null!, new Resource("supplied"), TestContext.Current.CancellationToken);
        await nullActor.Should().ThrowAsync<ArgumentNullException>().WithParameterName("actor");
        if (harness.HasResource)
        {
            var nullResource = async () => await harness.Explicit(CreateActor("supplied"), null!, TestContext.Current.CancellationToken);
            await nullResource.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName(variant >= 4 ? "leaf" : "resource");
        }

        business.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Handle_BothEntries_PropagatesBusinessFailureExceptionAndCancellation(int variant)
    {
        var business = new Business();
        var harness = CreateHarness(variant, business);
        var actor = CreateActor("actor");
        var resource = new Resource("loaded");
        var error = new Error.Conflict(Resource: null, Code: "business.failed");

        await harness.InDispatch(actor, resource, true, async () =>
        {
            foreach (var invoke in new Func<CancellationToken, ValueTask<Result<Observation>>>[]
            {
                harness.Normal, token => harness.Explicit(actor, resource, token),
            })
            {
                business.Error = error;
                (await invoke(TestContext.Current.CancellationToken)).UnwrapError().Should().BeSameAs(error);
                business.Error = null;

                var expected = new InvalidOperationException("business failed");
                business.Exception = expected;
                var throwing = async () => await invoke(TestContext.Current.CancellationToken);
                (await throwing.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(expected);
                business.Exception = null;

                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                var canceled = async () => await invoke(cancellation.Token);
                (await canceled.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
            }
        });
    }

    [Fact]
    public async Task Handle_ViaEntry_WrongLeaf_DoesNotBorrowPopulatedAccessor()
    {
        var message = new ViaCommand();
        var handler = new WrongLeafHandler();
        using var scope = AuthorizationDispatchContext.Begin<ViaCommand, Result<Observation>>(message);
        await AuthorizationDispatchContext.ResolveActorAsync<ViaCommand, Result<Observation>>(
            new Provider(CreateActor("actor")), TestContext.Current.CancellationToken);
        AuthorizationDispatchContext.MarkResourceAuthorized<ViaCommand, Resource, Result<Observation>>();
        using var wrong = AuthorizedResourceHolder<ViaCommand, OtherResource>.Push(new());

        var invoke = async () => await handler.Handle(message, TestContext.Current.CancellationToken);

        await invoke.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ViaCommand*OtherResource*Resource*AddResourceAuthorization*");
        handler.Called.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DirectEntry_InnerDispatchCannotBorrowParentsResourceFrame()
    {
        var message = new DirectCommand();
        var handler = new DirectCommandHandler(new Business());
        using var outer = AuthorizationDispatchContext.Begin<DirectCommand, Result<Observation>>(message);
        await AuthorizationDispatchContext.ResolveActorAsync<DirectCommand, Result<Observation>>(
            new Provider(CreateActor("outer")), TestContext.Current.CancellationToken);
        AuthorizationDispatchContext.MarkResourceAuthorized<DirectCommand, Resource, Result<Observation>>();
        using var resource = AuthorizedResourceHolder<DirectCommand, Resource>.Push(new("outer"));
        using var inner = AuthorizationDispatchContext.Begin<DirectCommand, Result<Observation>>(message);
        await AuthorizationDispatchContext.ResolveActorAsync<DirectCommand, Result<Observation>>(
            new Provider(CreateActor("inner")), TestContext.Current.CancellationToken);
        AuthorizationDispatchContext.MarkResourceAuthorized<DirectCommand, Resource, Result<Observation>>();

        var invoke = async () => await handler.Handle(message, TestContext.Current.CancellationToken);

        await invoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Resource*DirectCommand*current dispatch*");
    }

    private static Harness CreateHarness(int variant, Business business)
    {
        switch (variant)
        {
            case 0:
                var command = new ActorCommand();
                var commandHandler = new CommandHandler(business);
                return new(command, false, token => commandHandler.Handle(command, token),
                    (actor, _, token) => commandHandler.Handle(command, actor, token),
                    (actor, resource, complete, work) => InDispatch(command, actor, resource, complete, work));
            case 1:
                var query = new ActorQuery();
                var queryHandler = new QueryHandler(business);
                return new(query, false, token => queryHandler.Handle(query, token),
                    (actor, _, token) => queryHandler.Handle(query, actor, token),
                    (actor, resource, complete, work) => InDispatch(query, actor, resource, complete, work));
            case 2:
                var directCommand = new DirectCommand();
                var directCommandHandler = new DirectCommandHandler(business);
                return new(directCommand, true, token => directCommandHandler.Handle(directCommand, token),
                    (actor, resource, token) => directCommandHandler.Handle(directCommand, actor, resource, token),
                    (actor, resource, complete, work) => InDispatch(directCommand, actor, resource, complete, work));
            case 3:
                var directQuery = new DirectQuery();
                var directQueryHandler = new DirectQueryHandler(business);
                return new(directQuery, true, token => directQueryHandler.Handle(directQuery, token),
                    (actor, resource, token) => directQueryHandler.Handle(directQuery, actor, resource, token),
                    (actor, resource, complete, work) => InDispatch(directQuery, actor, resource, complete, work));
            case 4:
                var viaCommand = new ViaCommand();
                var viaCommandHandler = new ViaCommandHandler(business);
                return new(viaCommand, true, token => viaCommandHandler.Handle(viaCommand, token),
                    (actor, leaf, token) => viaCommandHandler.Handle(viaCommand, actor, leaf, token),
                    (actor, resource, complete, work) => InDispatch(viaCommand, actor, resource, complete, work));
            case 5:
                var viaQuery = new ViaQuery();
                var viaQueryHandler = new ViaQueryHandler(business);
                return new(viaQuery, true, token => viaQueryHandler.Handle(viaQuery, token),
                    (actor, leaf, token) => viaQueryHandler.Handle(viaQuery, actor, leaf, token),
                    (actor, resource, complete, work) => InDispatch(viaQuery, actor, resource, complete, work));
            default:
                throw new ArgumentOutOfRangeException(nameof(variant));
        }
    }

    private static async Task InDispatch<TMessage>(TMessage message, Actor actor, Resource resource, bool complete, Func<Task> work)
        where TMessage : IMessage, IAuthorizationMessage
    {
        using var scope = AuthorizationDispatchContext.Begin<TMessage, Result<Observation>>(message);
        await AuthorizationDispatchContext.ResolveActorAsync<TMessage, Result<Observation>>(
            new Provider(actor), TestContext.Current.CancellationToken);
        if (complete && message is IAuthorize)
            AuthorizationDispatchContext.MarkStaticAuthorized<TMessage, Result<Observation>>();
        if (complete && message is IResourceAuthorizationMessage)
            AuthorizationDispatchContext.MarkResourceAuthorized<TMessage, Resource, Result<Observation>>();
        using var loaded = complete && message is IResourceAuthorizationMessage
            ? AuthorizedResourceHolder<TMessage, Resource>.Push(resource)
            : null;
        await work();
    }

    private static Actor CreateActor(string id) => Actor.Create(id, new HashSet<string>());

    private sealed record Harness(
        object Message, bool HasResource,
        Func<CancellationToken, ValueTask<Result<Observation>>> Normal,
        Func<Actor, Resource, CancellationToken, ValueTask<Result<Observation>>> Explicit,
        Func<Actor, Resource, bool, Func<Task>, Task> InDispatch);

    private sealed record Observation(object Message, Actor Actor, Resource? Resource, CancellationToken Token);
    private sealed record Resource(string Id) : IIdentifyRelatedResource<Owner, string>
    {
        public string GetRelatedResourceId() => "owner";
    }
    private sealed record OtherResource;
    private sealed record Owner;

    private sealed class Business
    {
        internal int Calls { get; private set; }
        internal Error? Error { get; set; }
        internal Exception? Exception { get; set; }

        internal async ValueTask<Result<Observation>> Invoke(object message, Actor actor, Resource? resource, CancellationToken token)
        {
            Calls++;
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            if (Exception is not null)
                throw Exception;
            return Error is null ? Result.Ok(new Observation(message, actor, resource, token)) : Result.Fail<Observation>(Error);
        }
    }

    private sealed class Provider(Actor actor) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(actor));
    }

    private sealed record ActorCommand : ICommand<Result<Observation>>, IAuthorize
    {
        public IReadOnlyList<string> RequiredPermissions => [];
    }
    private sealed record ActorQuery : IQuery<Result<Observation>>, IAuthorize
    {
        public IReadOnlyList<string> RequiredPermissions => [];
    }
    private sealed record DirectCommand : ICommand<Result<Observation>>, IAuthorizeResource<Resource>
    {
        public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
    }
    private sealed record DirectQuery : IQuery<Result<Observation>>, IAuthorizeResource<Resource>
    {
        public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
    }
    private sealed record ViaCommand : ICommand<Result<Observation>>, IAuthorizeResourceVia<Owner>, IIdentifyResource<Resource, string>
    {
        public string GetResourceId() => "leaf";
        public IResult Authorize(Actor actor, IReadOnlyList<Owner> owners) => Result.Ok();
    }
    private sealed record ViaQuery : IQuery<Result<Observation>>, IAuthorizeResourceVia<Owner>, IIdentifyResource<Resource, string>
    {
        public string GetResourceId() => "leaf";
        public IResult Authorize(Actor actor, IReadOnlyList<Owner> owners) => Result.Ok();
    }

    private sealed class CommandHandler(Business business) : ActorCommandHandler<ActorCommand, Result<Observation>>
    {
        protected override ValueTask<Result<Observation>> HandleCore(ActorCommand command, Actor actor, CancellationToken cancellationToken) =>
            business.Invoke(command, actor, null, cancellationToken);
    }
    private sealed class QueryHandler(Business business) : ActorQueryHandler<ActorQuery, Result<Observation>>
    {
        protected override ValueTask<Result<Observation>> HandleCore(ActorQuery query, Actor actor, CancellationToken cancellationToken) =>
            business.Invoke(query, actor, null, cancellationToken);
    }
    private sealed class DirectCommandHandler(Business business) : ActorResourceCommandHandler<DirectCommand, Resource, Result<Observation>>
    {
        protected override ValueTask<Result<Observation>> HandleCore(DirectCommand command, Actor actor, Resource resource, CancellationToken cancellationToken) =>
            business.Invoke(command, actor, resource, cancellationToken);
    }
    private sealed class DirectQueryHandler(Business business) : ActorResourceQueryHandler<DirectQuery, Resource, Result<Observation>>
    {
        protected override ValueTask<Result<Observation>> HandleCore(DirectQuery query, Actor actor, Resource resource, CancellationToken cancellationToken) =>
            business.Invoke(query, actor, resource, cancellationToken);
    }
    private sealed class ViaCommandHandler(Business business) : ActorResourceViaCommandHandler<ViaCommand, Resource, Owner, Result<Observation>>
    {
        protected override ValueTask<Result<Observation>> HandleCore(ViaCommand command, Actor actor, Resource leaf, CancellationToken cancellationToken) =>
            business.Invoke(command, actor, leaf, cancellationToken);
    }
    private sealed class ViaQueryHandler(Business business) : ActorResourceViaQueryHandler<ViaQuery, Resource, Owner, Result<Observation>>
    {
        protected override ValueTask<Result<Observation>> HandleCore(ViaQuery query, Actor actor, Resource leaf, CancellationToken cancellationToken) =>
            business.Invoke(query, actor, leaf, cancellationToken);
    }
    private sealed class WrongLeafHandler : ActorResourceViaCommandHandler<ViaCommand, OtherResource, Owner, Result<Observation>>
    {
        internal bool Called { get; private set; }
        protected override ValueTask<Result<Observation>> HandleCore(ViaCommand command, Actor actor, OtherResource leaf, CancellationToken cancellationToken)
        {
            Called = true;
            return new(Result.Ok(new Observation(command, actor, null, cancellationToken)));
        }
    }
}
