using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>.
/// The generic behavior loads a resource via <see cref="IResourceLoader{TMessage, TResource}"/>
/// and authorizes against it before calling the handler.
/// </summary>
public class ResourceAuthorizationBehaviorTests
{
    #region Resource loaded and actor is owner — calls handler

    [Fact]
    public async Task Handle_ResourceFoundAndActorIsOwner_CallsNextAndReturnsSuccess()
    {
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<ResourceOwnerCommand>("owner-1", resource);
        var command = new ResourceOwnerCommand("res-1");
        var (next, tracker) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Done");
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Resource loaded and actor is NOT owner — returns Forbidden

    [Fact]
    public async Task Handle_ResourceFoundAndActorIsNotOwner_ReturnsForbiddenAndDoesNotCallHandler()
    {
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<ResourceOwnerCommand>("other-user", resource);
        var command = new ResourceOwnerCommand("res-1");
        var (next, tracker) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        result.UnwrapError().Detail.Should().Contain("Only the resource owner");
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked when authorization fails");
    }

    #endregion

    #region Resource not found — returns failure before auth check

    [Fact]
    public async Task Handle_ResourceNotFound_ReturnsNotFoundAndDoesNotCallHandler()
    {
        var behavior = CreateBehavior<ResourceOwnerCommand>("owner-1", resource: null);
        var command = new ResourceOwnerCommand("nonexistent");
        var (next, tracker) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.NotFound>();
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked when resource is not found");
    }

    #endregion

    #region Resource not found — authorization not invoked

    [Fact]
    public async Task Handle_ResourceNotFound_AuthorizeIsNotCalled()
    {
        var behavior = CreateBehavior<TrackingAuthCommand>("owner-1", resource: null);
        var command = new TrackingAuthCommand("nonexistent");
        var (next, _) = NextDelegate.TrackingAsync<TrackingAuthCommand, Result<string>>(
            Result.Ok("done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        command.AuthorizeWasCalled.Should().BeFalse("Authorize should not be called when resource loading fails");
    }

    #endregion

    #region FullAuthResourceCommand — non-owner with elevated permission

    [Fact]
    public async Task Handle_NonOwnerWithElevatedPermission_AllowsAccess()
    {
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<FullAuthResourceCommand>("admin-user", resource, "Resources.WriteAny");
        var command = new FullAuthResourceCommand("res-1");
        var (next, tracker) = NextDelegate.TrackingAsync<FullAuthResourceCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Error detail preserved from authorization

    [Fact]
    public async Task Handle_AuthorizationFails_PreservesCustomErrorDetail()
    {
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<FullAuthResourceCommand>("other-user", resource);
        var command = new FullAuthResourceCommand("res-1");
        var (next, tracker) = NextDelegate.TrackingAsync<FullAuthResourceCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("Cannot modify another user's resource");
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Error detail preserved from loader

    [Fact]
    public async Task Handle_LoaderReturnsCustomError_ErrorIsPreserved()
    {
        var notFoundError = new Error.NotFound(new ResourceRef("Resource", "OrderId"?.ToString())) { Detail = "Order 'xyz' was not found." };
        var behavior = CreateBehavior<ResourceOwnerCommand>("owner-1", resource: null, notFoundError: notFoundError);
        var command = new ResourceOwnerCommand("xyz");
        var (next, _) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("Order 'xyz' was not found.");
    }

    #endregion

    #region Actor checked before resource load

    [Fact]
    public async Task Handle_NullActor_ThrowsBeforeResourceLoaderIsCalled()
    {
        // ga-11: the resource loader must not run when the caller is unauthenticated.
        // Loader I/O is expensive (DB) and can leak existence by timing — gate on actor first.
        var loader = new FakeResourceLoader<ResourceOwnerCommand>(new TestResource("res-1", "owner-1"));
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ => loader);
        var sp = services.BuildServiceProvider();

        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            new NullActorProvider(), sp);

        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(Result.Ok("Done"));

        await FluentActions.Invoking(() => behavior.Handle(command, next, CancellationToken.None).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        loader.WasCalled.Should().BeFalse();
    }

    #endregion

    #region CancellationToken propagated to loader

    [Fact]
    public async Task Handle_PropagatesCancellationTokenToLoader()
    {
        using var cts = new CancellationTokenSource();
        var resource = new TestResource("res-1", "owner-1");
        var loader = new FakeResourceLoader<ResourceOwnerCommand>(resource);
        var behavior = CreateBehaviorWithLoader<ResourceOwnerCommand>("owner-1", loader);
        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("Done"));

        await behavior.Handle(command, next, cts.Token);

        loader.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task Handle_MissingResourceLoader_ThrowsInvalidOperationExceptionWithBehaviorContext()
    {
        var actorProvider = FakeActorProvider.NoPermissions("owner-1");
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            actorProvider,
            serviceProvider);
        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result`1>*")
            .WithMessage("*IResourceLoader<ResourceOwnerCommand, TestResource>*");
    }

    [Fact]
    public async Task Handle_ActorProviderReturnsNull_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ =>
            new FakeResourceLoader<ResourceOwnerCommand>(new TestResource("res-1", "owner-1")));
        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            new NullActorProvider(),
            services.BuildServiceProvider());
        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Cancellation token propagated to actor provider

    [Fact]
    public async Task Handle_PropagatesCancellationTokenToActorProvider()
    {
        using var cts = new CancellationTokenSource();
        var actor = Actor.Create("owner-1", new HashSet<string>());
        var capturingProvider = new TokenCapturingActorProvider(actor);
        var resource = new TestResource("res-1", "owner-1");

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ =>
            new FakeResourceLoader<ResourceOwnerCommand>(resource));
        var sp = services.BuildServiceProvider();

        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            capturingProvider, sp);
        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("Done"));

        await behavior.Handle(command, next, cts.Token);

        capturingProvider.LastCancellationToken.Should().Be(cts.Token);
    }

    #endregion

    #region Helpers

    private static ResourceAuthorizationBehavior<TMessage, TestResource, Result<string>>
        CreateBehavior<TMessage>(
            string actorId,
            TestResource? resource,
            params string[] permissions)
        where TMessage : IAuthorizeResource<TestResource>, global::Mediator.IMessage
        => CreateBehavior<TMessage>(actorId, resource, notFoundError: null, permissions);

    private static ResourceAuthorizationBehavior<TMessage, TestResource, Result<string>>
        CreateBehavior<TMessage>(
            string actorId,
            TestResource? resource,
            Error? notFoundError,
            params string[] permissions)
        where TMessage : IAuthorizeResource<TestResource>, global::Mediator.IMessage
    {
        var loader = new FakeResourceLoader<TMessage>(resource, notFoundError);
        return CreateBehaviorWithLoader(actorId, loader, permissions);
    }

    private static ResourceAuthorizationBehavior<TMessage, TestResource, Result<string>>
        CreateBehaviorWithLoader<TMessage>(
            string actorId,
            FakeResourceLoader<TMessage> loader,
            params string[] permissions)
        where TMessage : IAuthorizeResource<TestResource>, global::Mediator.IMessage
    {
        var actorProvider = permissions.Length > 0
            ? FakeActorProvider.WithPermissions(actorId, permissions)
            : FakeActorProvider.NoPermissions(actorId);

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<TMessage, TestResource>>(_ => loader);
        var sp = services.BuildServiceProvider();

        return new ResourceAuthorizationBehavior<TMessage, TestResource, Result<string>>(
            actorProvider, sp);
    }

    /// <summary>
    /// Command that tracks whether Authorize was called (for verifying short-circuit on NotFound).
    /// </summary>
    private sealed record TrackingAuthCommand(string ResourceId)
        : global::Mediator.ICommand<Result<string>>, IAuthorizeResource<TestResource>
    {
        public bool AuthorizeWasCalled { get; private set; }

        public IResult Authorize(Actor actor, TestResource resource)
        {
            AuthorizeWasCalled = true;
            return Result.Ok();
        }
    }

    private sealed class NullActorProvider : IActorProvider
    {
        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<Actor>(null!);
    }

    /// <summary>
    /// Actor provider that captures the <see cref="CancellationToken"/> for verification.
    /// </summary>
    private sealed class TokenCapturingActorProvider(Actor actor) : IActorProvider
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(actor);
        }
    }

    private sealed class FakeResourceLoader<TMessage> : IResourceLoader<TMessage, TestResource>
    {
        private readonly TestResource? _resource;
        private readonly Error _notFoundError;

        public CancellationToken LastCancellationToken { get; private set; }
        public bool WasCalled { get; private set; }

        public FakeResourceLoader(TestResource? resource, Error? notFoundError = null)
        {
            _resource = resource;
            _notFoundError = notFoundError ?? new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found." };
        }

        public Task<Result<TestResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastCancellationToken = cancellationToken;
            return _resource is not null
                ? Task.FromResult(Result.Ok(_resource))
                : Task.FromResult(Result.Fail<TestResource>(_notFoundError));
        }
    }

    #endregion
}