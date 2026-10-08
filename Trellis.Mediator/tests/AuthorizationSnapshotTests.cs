namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Testing;
using Folder = ActorHandlers.Consumer.Folder;
using MultiResourceCommand = ActorHandlers.Consumer.MultiResourceCommand;
using Observation = ActorHandlers.Consumer.Observation;
using ConsumerOrder = ActorHandlers.Consumer.Order;

public class AuthorizationSnapshotTests
{
    [Fact]
    public async Task Handle_StaticAndDirect_UsesOneIdenticalActorSnapshot()
    {
        var first = CreateActor("checked", "score:write");
        var changed = CreateActor("changed");
        var provider = new ChangingProvider(first, changed);
        var command = new SnapshotDirectCommand();
        var resource = new SnapshotResource("resource", "owner");
        var loader = new CountingLoader<SnapshotDirectCommand>(resource);
        using var services = new ServiceCollection()
            .AddSingleton<IResourceLoader<SnapshotDirectCommand, SnapshotResource>>(loader)
            .BuildServiceProvider();
        var context = new AuthorizationContextBehavior<SnapshotDirectCommand, Result<string>>();
        var permissions = new AuthorizationBehavior<SnapshotDirectCommand, Result<string>>(provider);
        var authorization = new ResourceAuthorizationBehavior<SnapshotDirectCommand, SnapshotResource, Result<string>>(
            provider, services);

        var result = await context.Handle(command, (message, token) =>
            permissions.Handle(message, (authorizedMessage, authorizedToken) =>
                authorization.Handle(authorizedMessage, (_, ct) =>
                {
                    AuthorizationDispatchContext.GetRequiredActor<SnapshotDirectCommand, Result<string>>()
                        .Should().BeSameAs(first);
                    new AuthorizedResourceHolder<SnapshotDirectCommand, SnapshotResource>()
                        .GetRequiredResource().Should().BeSameAs(resource);
                    ct.Should().Be(TestContext.Current.CancellationToken);
                    return new(Result.Ok("done"));
                }, authorizedToken), token), TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        command.CheckedActor.Should().BeSameAs(first);
        provider.Calls.Should().Be(1);
        loader.Calls.Should().Be(1);
        first.Should().Be(changed, "actor equality is ID-only, so snapshot assertions must use reference identity");
        AuthorizationDispatchContext.CurrentIdentity.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StaticAndVia_UsesOneActorAndPublishesLeafNotOwner()
    {
        var first = CreateActor("checked", "score:write");
        var provider = new ChangingProvider(first, CreateActor("changed"));
        var command = new SnapshotViaCommand();
        var leaf = new SnapshotResource("leaf", "owner");
        var owner = new SnapshotOwner("owner");
        var loader = new CountingLoader<SnapshotViaCommand>(leaf);
        using var services = new ServiceCollection()
            .AddSingleton<IResourceLoader<SnapshotViaCommand, SnapshotResource>>(loader)
            .BuildServiceProvider();
        var path = new ResolvedAuthorizationPath(typeof(SnapshotViaCommand), typeof(SnapshotResource),
            typeof(SnapshotOwner),
            [new ResolvedAuthorizationHop(typeof(SnapshotResource), typeof(SnapshotOwner), typeof(string),
                resource => [((SnapshotResource)resource).OwnerId],
                (_, _, _) => Task.FromResult(HopLoadResult.Success(owner)), false)]);
        var context = new AuthorizationContextBehavior<SnapshotViaCommand, Result<string>>();
        var permissions = new AuthorizationBehavior<SnapshotViaCommand, Result<string>>(provider);
        var authorization = new ResourceAuthorizationViaBehavior<SnapshotViaCommand, SnapshotResource, SnapshotOwner, Result<string>>(
            provider, services, path);

        var result = await context.Handle(command, (message, token) =>
            permissions.Handle(message, (authorizedMessage, authorizedToken) =>
                authorization.Handle(authorizedMessage, (_, _) =>
                {
                    AuthorizationDispatchContext.GetRequiredActor<SnapshotViaCommand, Result<string>>()
                        .Should().BeSameAs(first);
                    new AuthorizedResourceHolder<SnapshotViaCommand, SnapshotResource>()
                        .GetRequiredResource().Should().BeSameAs(leaf);
                    return new(Result.Ok("done"));
                }, authorizedToken), token), TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        command.CheckedActor.Should().BeSameAs(first);
        command.CheckedOwners.Should().ContainSingle().Which.Should().BeSameAs(owner);
        provider.Calls.Should().Be(1);
        loader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ManualAuthorizationWithoutContext_FailsBeforeProviderResolution()
    {
        var provider = new ChangingProvider(CreateActor("checked", "score:write"));
        var behavior = new AuthorizationBehavior<SnapshotDirectCommand, Result<string>>(provider);

        var invoke = async () => await behavior.Handle(new(), (_, _) => new(Result.Ok("bypassed")),
            TestContext.Current.CancellationToken);

        await invoke.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SnapshotDirectCommand*AuthorizationContextBehavior*");
        provider.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_MultipleTypedResources_EitherRegistrationOrder_SuppliesSelectedSnapshot(
        bool registerFolderFirst, bool handleFolder)
    {
        var actor = CreateActor("checked", "score:write");
        var provider = new ChangingProvider(actor, CreateActor("changed"));
        var order = new ConsumerOrder("order", actor.Id.Value);
        var folder = new Folder("folder", actor.Id.Value);
        var orderLoader = new MultiResourceLoader<ConsumerOrder>(order);
        var folderLoader = new MultiResourceLoader<Folder>(folder);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IActorProvider>(provider)
            .AddSingleton<IResourceLoader<MultiResourceCommand, ConsumerOrder>>(orderLoader)
            .AddSingleton<IResourceLoader<MultiResourceCommand, Folder>>(folderLoader);
        services.AddTrellisBehaviors();

        if (registerFolderFirst)
            services.AddResourceAuthorization<MultiResourceCommand, Folder, Result<Observation>>();
        services.AddResourceAuthorization<MultiResourceCommand, ConsumerOrder, Result<Observation>>();
        if (!registerFolderFirst)
            services.AddResourceAuthorization<MultiResourceCommand, Folder, Result<Observation>>();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();
        var command = new MultiResourceCommand("order", new(actor), new(actor));
        var orderHandler = new SnapshotHandler<MultiResourceCommand, ConsumerOrder>();
        var folderHandler = new SnapshotHandler<MultiResourceCommand, Folder>();
        MessageHandlerDelegate<MultiResourceCommand, Result<Observation>> pipeline = handleFolder
            ? folderHandler.Handle : orderHandler.Handle;
        foreach (var behavior in scope.ServiceProvider
            .GetServices<IPipelineBehavior<MultiResourceCommand, Result<Observation>>>().Reverse())
        {
            var next = pipeline;
            pipeline = (message, cancellationToken) => behavior.Handle(message, next, cancellationToken);
        }

        var result = await pipeline(command, TestContext.Current.CancellationToken);

        var observation = result.Should().BeSuccess().Which;
        object expectedResource = handleFolder ? folder : order;
        observation.Actor.Should().BeSameAs(actor);
        observation.Resource.Should().BeSameAs(expectedResource);
        observation.Token.Should().Be(TestContext.Current.CancellationToken);
        command.Probe.CheckedActor.Should().BeSameAs(actor);
        command.FolderProbe.CheckedActor.Should().BeSameAs(actor);
        provider.Calls.Should().Be(1);
        orderLoader.Calls.Should().Be(1);
        folderLoader.Calls.Should().Be(1);
        (orderHandler.Calls + folderHandler.Calls).Should().Be(1);
        AuthorizationDispatchContext.CurrentIdentity.Should().BeNull();
        scope.ServiceProvider.GetRequiredService<IAuthorizedResource<MultiResourceCommand, ConsumerOrder>>()
            .TryGetResource(out _).Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<IAuthorizedResource<MultiResourceCommand, Folder>>()
            .TryGetResource(out _).Should().BeFalse();
    }

    private static Actor CreateActor(string state, params string[] permissions) =>
        new("same-id", new HashSet<string>(permissions), new HashSet<string>(),
            new Dictionary<string, string> { ["state"] = state });

    private sealed class ChangingProvider(params Actor[] actors) : IActorProvider
    {
        public int Calls { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe.From(actors[Math.Min(Calls++, actors.Length - 1)]));
    }

    private sealed class CountingLoader<TMessage>(SnapshotResource resource) : IResourceLoader<TMessage, SnapshotResource>
    {
        public int Calls { get; private set; }

        public Task<Result<SnapshotResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result.Ok(resource));
        }
    }

    private sealed class MultiResourceLoader<TResource>(TResource resource) : IResourceLoader<MultiResourceCommand, TResource>
    {
        public int Calls { get; private set; }

        public Task<Result<TResource>> LoadAsync(MultiResourceCommand message, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result.Ok(resource));
        }
    }

    private sealed class SnapshotHandler<TMessage, TResource>
        : ActorResourceCommandHandler<TMessage, TResource, Result<Observation>>
        where TMessage : ICommand<Result<Observation>>, IAuthorizeResource<TResource>
        where TResource : class
    {
        public int Calls { get; private set; }

        protected override ValueTask<Result<Observation>> Handle(
            TMessage command, Actor actor, TResource resource, CancellationToken cancellationToken)
        {
            Calls++;
            return new(Result.Ok(new Observation(actor, resource, cancellationToken)));
        }
    }

    private sealed record SnapshotDirectCommand : ICommand<Result<string>>, IAuthorize, IAuthorizeResource<SnapshotResource>
    {
        public IReadOnlyList<string> RequiredPermissions => ["score:write"];
        public Actor? CheckedActor { get; private set; }

        public IResult Authorize(Actor actor, SnapshotResource resource)
        {
            CheckedActor = actor;
            return Result.Ensure(actor.HasPermission("score:write"), new Error.Forbidden("snapshot.changed"));
        }
    }

    private sealed record SnapshotViaCommand : ICommand<Result<string>>, IAuthorize,
        IAuthorizeResourceVia<SnapshotOwner>, IIdentifyResource<SnapshotResource, string>
    {
        public IReadOnlyList<string> RequiredPermissions => ["score:write"];
        public Actor? CheckedActor { get; private set; }
        public IReadOnlyList<SnapshotOwner> CheckedOwners { get; private set; } = [];
        public string GetResourceId() => "leaf";

        public IResult Authorize(Actor actor, IReadOnlyList<SnapshotOwner> owners)
        {
            CheckedActor = actor;
            CheckedOwners = owners;
            return Result.Ensure(actor.HasPermission("score:write"), new Error.Forbidden("snapshot.changed"));
        }
    }

    private sealed record SnapshotResource(string Id, string OwnerId) : IIdentifyRelatedResource<SnapshotOwner, string>
    {
        public string GetRelatedResourceId() => OwnerId;
    }

    private sealed record SnapshotOwner(string Id);
}
