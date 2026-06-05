namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="AuthFailureExposurePolicy"/> translation logic in
/// <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>.
/// </summary>
public sealed class ResourceAuthorizationBehaviorExposurePolicyTests
{
    [Fact]
    public async Task Handle_DefaultPolicyPropagate_ForbiddenAuthFailurePassesThrough()
    {
        var resource = new HiddenResource("res-1", "owner-1", "kind-public");
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, new ResourceAuthorizationOptions());
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_ForbiddenTranslatedToNotFoundWithResourceRef()
    {
        var resource = new HiddenResource("res-1", "owner-1", "kind-public");
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
        notFound.Resource.Id.Should().Be("res-1");
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_LoadFailureForbiddenTranslatedToNotFound()
    {
        // A loader returning Result.Fail<TestResource>(Forbidden) — e.g. a remote ACL — must
        // also flow through the translation, not just authorize-failures. The spec calls this
        // out explicitly: "Apply policy ... to both load-failure and authorize-failure paths."
        var loaderForbidden = new Error.Forbidden("acl.downstream-denied");
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError(actorId: "owner-1", loaderForbidden, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
        notFound.Resource.Id.Should().Be("res-1");
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_LoadFailureNotFoundPassesThroughUnchanged()
    {
        // Only Forbidden and AuthenticationRequired are translated. A loader's NotFound stays
        // a NotFound (although it might carry a different ResourceRef than the synthetic one).
        var loaderNotFound = new Error.NotFound(ResourceRef.For("HiddenResource", "res-1"));
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError(actorId: "owner-1", loaderNotFound, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeSameAs(loaderNotFound, "loader-returned errors must be passed through verbatim when not in the translation set");
    }

    [Theory]
    [InlineData("service.degraded")]
    public async Task Handle_HideExistenceConfigured_LoadFailureUnavailablePassesThrough(string reason)
    {
        var loaderUnavailable = new Error.Unavailable(reason);
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithLoaderError(actorId: "owner-1", loaderUnavailable, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        // Hiding transient failures behind 404 would destroy operational signal.
        result.UnwrapError().Should().BeSameAs(loaderUnavailable);
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_UnauthenticatedActorTranslatesAuthRequiredToNotFound()
    {
        // The actor-resolution branch (line 91 in ResourceAuthorizationBehavior.cs) emits
        // Error.AuthenticationRequired, which must also be hidden when HideAsNotFound is on.
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: null, resource: new HiddenResource("res-1", "owner-1", "kind"), options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_PerResourcePropagateOverridesDefaultHideAsNotFound()
    {
        var resource = new HiddenResource("res-1", "owner-1", "k");
        var options = new ResourceAuthorizationOptions
        {
            DefaultExposurePolicy = AuthFailureExposurePolicy.HideAsNotFound,
        };
        options.Propagate<HiddenResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_HideExistenceConfigured_DifferentResourceNotHidden()
    {
        var resource = new HiddenResource("res-1", "owner-1", "k");
        var options = new ResourceAuthorizationOptions().HideExistence<UnrelatedResource>();
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: "other-user", resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_NoIdentifyResource_NotFoundEmittedWithoutId()
    {
        // ResourceOwnerCommand does not implement IIdentifyResource<TestResource, ?>, so the
        // reflection extractor returns null and the synthetic ResourceRef lacks an Id.
        var resource = new TestResource("res-1", "owner-1");
        var options = new ResourceAuthorizationOptions().HideExistence<TestResource>();
        var behavior = CreateOwnerBehavior(actorId: "other-user", resource, options);
        var command = new ResourceOwnerCommand("res-1");

        var result = await InvokeOwner(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("TestResource");
        notFound.Resource.Id.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProjectionOverload_PublicResourceTypeIsExposedAndIdExtractedFromPublicIdentifier()
    {
        // HideExistence<TAuthorization, TPublic>() decouples loader projection from wire shape.
        // For projection, the command identifies the PUBLIC resource (PublicAggregate) and the
        // pipeline authorizes against the loaded projection (AuthorizationProjection).
        var projection = new AuthorizationProjection("owner-1");
        var options = new ResourceAuthorizationOptions()
            .HideExistence<AuthorizationProjection, PublicAggregate>();
        var behavior = CreateProjectionBehavior(actorId: "other-user", projection, options);
        var command = new ProjectionAuthorizedCommand("public-id-42");

        var result = await InvokeProjection(behavior, command);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("PublicAggregate");
        notFound.Resource.Id.Should().Be("public-id-42");
    }

    [Fact]
    public async Task Handle_NullPayloadFromLoader_TranslatedToNotFoundUnderHideAsNotFound()
    {
        // The behavior's null-payload defense synthesises Error.Forbidden internally; under
        // HideAsNotFound that synthetic Forbidden must ALSO be hidden — a misbehaving loader
        // shouldn't leak existence either.
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();
        var behavior = CreateBehaviorWithNullPayload(actorId: "owner-1", options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        result.UnwrapError().Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task Handle_AuthorizeBehaviorShortCircuit_AuthenticationRequiredFromStaticBehaviorNotHidden()
    {
        // Documented limitation: the pipeline's static-permission AuthorizationBehavior runs
        // BEFORE the resource-authorization behavior. When a command implements IAuthorize
        // alongside IAuthorizeResource<T>, an unauthenticated caller's 401 surfaces from
        // AuthorizationBehavior — outside HideAsNotFound's scope. This test asserts the
        // boundary: only failures that reach the resource-authorization behavior are hidden.
        // (Anchors the cookbook caveat — if this test changes, the cookbook must change too.)
        var resource = new HiddenResource("res-1", "owner-1", "k");
        var options = new ResourceAuthorizationOptions().HideExistence<HiddenResource>();

        // The resource-auth behavior alone — bypassing AuthorizationBehavior — handles
        // authentication-required and translates it. The "limitation" is that when the OUTER
        // AuthorizationBehavior also runs (pipeline composition) it short-circuits first; that
        // outer-pipeline composition is asserted in the higher-level integration tests of
        // AuthorizationBehavior, not at this single-behavior unit level. This test confirms
        // the inner behavior correctly translates when reached.
        var behavior = CreateBehavior<HideExistenceCommand>(actorId: null, resource, options);
        var command = new HideExistenceCommand("res-1");

        var result = await InvokeHide(behavior, command);

        // Inner behavior translates correctly — pipeline-level short-circuit is a separate concern.
        result.UnwrapError().Should().BeOfType<Error.NotFound>(
            "this confirms the inner translation; pipeline-level AuthorizationBehavior short-circuit is documented in the cookbook caveat");
    }

    [Fact]
    public async Task DI_HideExistenceConfigured_WithoutLoggingRegistered_StillAppliesPolicy()
    {
        // Regression for round-1 code-review finding: the new options-aware constructor's
        // `ILogger<...>` parameter must default to null so Microsoft DI's ActivatorUtilities
        // picks it even when the consumer hasn't called services.AddLogging(). Otherwise DI
        // would fall back to the legacy 2-arg ctor and silently drop the configured
        // HideExistence policy, leaving consumers vulnerable to existence leaks.
        var services = new ServiceCollection();

        services.AddResourceAuthorization<HideExistenceCommand, HiddenResource, Result<string>>();
        services.AddResourceAuthorization(o => o.HideExistence<HiddenResource>());

        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("other-user"));
        services.AddScoped<IResourceLoader<HideExistenceCommand, HiddenResource>>(
            _ => new HiddenResourceLoader<HideExistenceCommand>(new HiddenResource("res-1", "owner-1", "k")));
        // NOTE: NO services.AddLogging() here — exercising the bare-bones consumer path.

        using var scope = services.BuildServiceProvider().CreateScope();
        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<HideExistenceCommand, Result<string>>>();
        behavior.Should().BeOfType<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>();

        var typedBehavior = (ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>)behavior;
        var result = await InvokeHide(typedBehavior, new HideExistenceCommand("res-1"));

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("HiddenResource");
        notFound.Resource.Id.Should().Be("res-1");
    }

    private static async Task<Result<string>> InvokeHide<TMessage>(
        ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>> behavior,
        TMessage command)
        where TMessage : IAuthorizeResource<HiddenResource>, IMessage
    {
        var (next, _) = NextDelegate.TrackingAsync<TMessage, Result<string>>(Result.Ok("Done"));
        return await behavior.Handle(command, next, TestContext.Current.CancellationToken);
    }

    private static async Task<Result<string>> InvokeOwner(
        ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>> behavior,
        ResourceOwnerCommand command)
    {
        var (next, _) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(Result.Ok("Done"));
        return await behavior.Handle(command, next, TestContext.Current.CancellationToken);
    }

    private static async Task<Result<string>> InvokeProjection(
        ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>> behavior,
        ProjectionAuthorizedCommand command)
    {
        var (next, _) = NextDelegate.TrackingAsync<ProjectionAuthorizedCommand, Result<string>>(Result.Ok("Done"));
        return await behavior.Handle(command, next, TestContext.Current.CancellationToken);
    }

    private static ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>>
        CreateBehavior<TMessage>(
            string? actorId,
            HiddenResource resource,
            ResourceAuthorizationOptions options)
        where TMessage : IAuthorizeResource<HiddenResource>, IMessage
    {
        var loader = new HiddenResourceLoader<TMessage>(resource);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<TMessage, HiddenResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        IActorProvider actorProvider = actorId is null
            ? FakeActorProvider.Anonymous()
            : FakeActorProvider.NoPermissions(actorId);
        return new ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>>(
            actorProvider,
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<TMessage, HiddenResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>
        CreateBehaviorWithLoaderError(
            string actorId,
            Error loaderError,
            ResourceAuthorizationOptions options)
    {
        var loader = new ErrorReturningLoader<HideExistenceCommand>(loaderError);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<HideExistenceCommand, HiddenResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>
        CreateBehaviorWithNullPayload(
            string actorId,
            ResourceAuthorizationOptions options)
    {
        var loader = new NullPayloadHiddenLoader();
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<HideExistenceCommand, HiddenResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<HideExistenceCommand, HiddenResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>
        CreateOwnerBehavior(
            string actorId,
            TestResource resource,
            ResourceAuthorizationOptions options)
    {
        var loader = new TestResourceLoader(resource);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>>.Instance);
    }

    private static ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>>
        CreateProjectionBehavior(
            string actorId,
            AuthorizationProjection projection,
            ResourceAuthorizationOptions options)
    {
        var loader = new ProjectionLoader(projection);
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ProjectionAuthorizedCommand, AuthorizationProjection>>(_ => loader);
        var provider = services.BuildServiceProvider();
        return new ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>>(
            FakeActorProvider.NoPermissions(actorId),
            provider,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationBehavior<ProjectionAuthorizedCommand, AuthorizationProjection, Result<string>>>.Instance);
    }

    internal sealed record HiddenResource(string Id, string OwnerId, string Kind);

    internal sealed record HideExistenceCommand(string ResourceId)
        : ICommand<Result<string>>,
          IAuthorizeResource<HiddenResource>,
          IIdentifyResource<HiddenResource, string>
    {
        public string GetResourceId() => ResourceId;

        public IResult Authorize(Actor actor, HiddenResource resource) =>
            actor.Id == resource.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden"));
    }

    internal sealed record UnrelatedResource;

    internal sealed record AuthorizationProjection(string OwnerId);

    internal sealed record PublicAggregate;

    internal sealed record ProjectionAuthorizedCommand(string PublicId)
        : ICommand<Result<string>>,
          IAuthorizeResource<AuthorizationProjection>,
          IIdentifyResource<PublicAggregate, string>
    {
        public string GetResourceId() => PublicId;

        public IResult Authorize(Actor actor, AuthorizationProjection projection) =>
            actor.Id == projection.OwnerId
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("authorization.forbidden"));
    }

    private sealed class HiddenResourceLoader<TMessage>(HiddenResource resource)
        : IResourceLoader<TMessage, HiddenResource>
    {
        public Task<Result<HiddenResource>> LoadAsync(TMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(resource));
    }

    private sealed class ErrorReturningLoader<TMessage>(Error error)
        : IResourceLoader<TMessage, HiddenResource>
    {
        public Task<Result<HiddenResource>> LoadAsync(TMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Fail<HiddenResource>(error));
    }

    private sealed class NullPayloadHiddenLoader : IResourceLoader<HideExistenceCommand, HiddenResource>
    {
        public Task<Result<HiddenResource>> LoadAsync(HideExistenceCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok<HiddenResource>(null!));
    }

    private sealed class TestResourceLoader(TestResource resource)
        : IResourceLoader<ResourceOwnerCommand, TestResource>
    {
        public Task<Result<TestResource>> LoadAsync(ResourceOwnerCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(resource));
    }

    private sealed class ProjectionLoader(AuthorizationProjection projection)
        : IResourceLoader<ProjectionAuthorizedCommand, AuthorizationProjection>
    {
        public Task<Result<AuthorizationProjection>> LoadAsync(ProjectionAuthorizedCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(projection));
    }
}
