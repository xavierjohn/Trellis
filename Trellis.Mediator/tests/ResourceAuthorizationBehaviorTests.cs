using Trellis.Testing;
namespace Trellis.Mediator.Tests;

using global::Mediator;
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
    public async Task Handle_NoActor_EmitsUnauthorizedBeforeResourceLoaderIsCalled()
    {
        // ga-11: the resource loader must not run when the caller is unauthenticated.
        // Loader I/O is expensive (DB) and can leak existence by timing — gate on actor first.
        // "No authenticated actor" is modelled as Maybe<Actor>.None on IActorProvider; the
        // pipeline maps it to Error.AuthenticationRequired (HTTP 401).
        var loader = new FakeResourceLoader<ResourceOwnerCommand>(new TestResource("res-1", "owner-1"));
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ => loader);
        var sp = services.BuildServiceProvider();

        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            new NoActorProvider(), sp);

        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.AuthenticationRequired>();
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
    public async Task Handle_ActorProviderReturnsNone_EmitsUnauthorized()
    {
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ =>
            new FakeResourceLoader<ResourceOwnerCommand>(new TestResource("res-1", "owner-1")));
        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            new NoActorProvider(),
            services.BuildServiceProvider());
        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.AuthenticationRequired>();
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

    #region Accessor populated for handler during next() — v4 typed accessor

    [Fact]
    public async Task Handle_AuthorizeSucceeds_AccessorPopulatedDuringNextWithSameInstance()
    {
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<ResourceOwnerCommand>("owner-1", resource);
        var command = new ResourceOwnerCommand("res-1");

        TestResource? observedDuringNext = null;
        bool observedSameInstance = false;
        var holder = new AuthorizedResourceHolder<ResourceOwnerCommand, TestResource>();
        MessageHandlerDelegate<ResourceOwnerCommand, Result<string>> next = (_, _) =>
        {
            holder.TryGet(out observedDuringNext).Should().BeTrue(
                "the accessor must be populated by ResourceAuthorizationBehavior before invoking next");
            observedSameInstance = ReferenceEquals(observedDuringNext, resource);
            return new ValueTask<Result<string>>(Result.Ok("Done"));
        };

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        observedDuringNext.Should().NotBeNull();
        observedSameInstance.Should().BeTrue(
            "the accessor must return the SAME instance the loader returned — that is the whole point");
        // After Handle returns, the using-block has disposed and the accessor is empty again.
        holder.TryGet(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AuthorizeFails_AccessorNotPopulatedDuringDownstreamObservation()
    {
        // Denied authorizations must NOT expose the loaded resource. The pipeline returns
        // a Forbidden failure before any push happens; observers running in the same async
        // flow (e.g., the calling test code) must see the accessor as empty.
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<ResourceOwnerCommand>("other-user", resource);
        var command = new ResourceOwnerCommand("res-1");
        var (next, tracker) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        tracker.WasInvoked.Should().BeFalse();

        new AuthorizedResourceHolder<ResourceOwnerCommand, TestResource>()
            .TryGet(out _).Should().BeFalse(
                "denied authorizations must not leak the loaded resource through the accessor");
    }

    [Fact]
    public async Task Handle_LoaderFails_AccessorNotPopulated()
    {
        var behavior = CreateBehavior<ResourceOwnerCommand>("owner-1", resource: null);
        var command = new ResourceOwnerCommand("nonexistent");
        var (next, tracker) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        tracker.WasInvoked.Should().BeFalse();
        new AuthorizedResourceHolder<ResourceOwnerCommand, TestResource>()
            .TryGet(out _).Should().BeFalse(
                "load failures must not populate the accessor");
    }

    [Fact]
    public async Task Handle_AccessorPoppedAfterHandlerEvenOnHandlerFailure()
    {
        // The using-block guarantees pop even when next returns a failure result.
        // After Handle returns the accessor must be empty in the caller's async flow.
        var resource = new TestResource("res-1", "owner-1");
        var behavior = CreateBehavior<ResourceOwnerCommand>("owner-1", resource);
        var command = new ResourceOwnerCommand("res-1");
        MessageHandlerDelegate<ResourceOwnerCommand, Result<string>> next = (_, _) =>
            new ValueTask<Result<string>>(Result.Fail<string>(
                new Error.Conflict(null, "test.conflict") { Detail = "Handler-level failure" }));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Conflict>();
        new AuthorizedResourceHolder<ResourceOwnerCommand, TestResource>()
            .TryGet(out _).Should().BeFalse(
                "the using-block must pop the accessor even when the handler returns a failure result");
    }

    [Fact]
    public async Task Handle_NestedSends_OuterAccessorRestoredAfterInner()
    {
        // Simulates the nested-mediator.Send case: an outer dispatch's handler invokes
        // a SECOND ResourceAuthorizationBehavior dispatch (for a different message but the
        // same closed pair shape). The outer handler must see its own resource before AND
        // after the nested dispatch completes.
        var outerResource = new TestResource("outer-res", "owner-1");
        var innerResource = new TestResource("inner-res", "owner-1");

        var outerBehavior = CreateBehavior<ResourceOwnerCommand>("owner-1", outerResource);
        var innerBehavior = CreateBehavior<ResourceOwnerCommand>("owner-1", innerResource);

        var outerCommand = new ResourceOwnerCommand("outer-res");
        var innerCommand = new ResourceOwnerCommand("inner-res");

        var observations = new List<string>();
        var holder = new AuthorizedResourceHolder<ResourceOwnerCommand, TestResource>();

        MessageHandlerDelegate<ResourceOwnerCommand, Result<string>> innerNext = (_, _) =>
        {
            observations.Add($"inner-next:{holder.GetRequired().Id}");
            return new ValueTask<Result<string>>(Result.Ok("inner-done"));
        };

        MessageHandlerDelegate<ResourceOwnerCommand, Result<string>> outerNext = async (_, ct) =>
        {
            observations.Add($"outer-next-before-nested:{holder.GetRequired().Id}");
            var innerResult = await innerBehavior.Handle(innerCommand, innerNext, ct);
            innerResult.IsSuccess.Should().BeTrue();
            observations.Add($"outer-next-after-nested:{holder.GetRequired().Id}");
            return Result.Ok("outer-done");
        };

        var result = await outerBehavior.Handle(outerCommand, outerNext, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        observations.Should().Equal(
            "outer-next-before-nested:outer-res",
            "inner-next:inner-res",
            "outer-next-after-nested:outer-res");
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

    private sealed class NoActorProvider : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe<Actor>.None);
    }

    /// <summary>
    /// Actor provider that captures the <see cref="CancellationToken"/> for verification.
    /// </summary>
    private sealed class TokenCapturingActorProvider(Actor actor) : IActorProvider
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Maybe.From(actor));
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

    #region Loader returns Result.Ok with null payload — fail closed

    [Fact]
    public async Task Handle_LoaderReturnsSuccessWithNullPayload_CollapsesToForbidden()
    {
        // Defense-in-depth: an IResourceLoader that violates its Result<T> contract by
        // returning Result.Ok carrying a null value must NOT pass null through to
        // message.Authorize where a downstream member access would NRE and bubble as 500.
        // The pipeline must fail closed with a Forbidden response.
        var loader = new NullPayloadResourceLoader<ResourceOwnerCommand>();
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ => loader);
        var sp = services.BuildServiceProvider();

        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            FakeActorProvider.NoPermissions("owner-1"), sp);
        var command = new ResourceOwnerCommand("res-1");
        var (next, tracker) = NextDelegate.TrackingAsync<ResourceOwnerCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var error = result.UnwrapError();
        error.Should().BeOfType<Error.Forbidden>();
        error.Code.Should().Be("resource.authorization.null-payload");
        tracker.WasInvoked.Should().BeFalse();
    }

    private sealed class NullPayloadResourceLoader<TMessage> : IResourceLoader<TMessage, TestResource>
    {
        public Task<Result<TestResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
            => Task.FromResult(Result.Ok<TestResource>(null!));
    }

    #endregion
}