namespace Trellis.Mediator.Tests;

using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="AuthorizationBehavior{TMessage, TResponse}"/>.
/// </summary>
public class AuthorizationBehaviorTests
{
    #region Actor has all required permissions

    [Fact]
    public async Task Handle_ActorHasAllPermissions_CallsNextAndReturnsHandlerResult()
    {
        var actorProvider = FakeActorProvider.WithPermissions("user-1", "Admin.Write");
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(actorProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Done");
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Actor missing permission

    [Fact]
    public async Task Handle_ActorMissingPermission_DoesNotCallNextAndReturnsForbidden()
    {
        var actorProvider = FakeActorProvider.NoPermissions();
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(actorProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
        result.Error.Detail.Should().Contain("Admin.Write");
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked when permissions are missing");
    }

    [Fact]
    public async Task Handle_ActorMissingMultiplePermissions_ErrorListsAllMissing()
    {
        var actorProvider = FakeActorProvider.WithPermissions("user-1", "Other.Read");
        var behavior = new AuthorizationBehavior<MultiPermissionCommand, Result<string>>(actorProvider);
        var command = new MultiPermissionCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<MultiPermissionCommand, Result<string>>(
            Result.Success("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Detail.Should().Contain("Orders.Write");
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Empty permissions list

    [Fact]
    public async Task Handle_EmptyPermissions_AlwaysPasses()
    {
        var actorProvider = FakeActorProvider.NoPermissions();
        var behavior = new AuthorizationBehavior<NoPermissionsCommand, Result<string>>(actorProvider);
        var command = new NoPermissionsCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<NoPermissionsCommand, Result<string>>(
            Result.Success("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Single permission

    [Fact]
    public async Task Handle_SinglePermission_WorksCorrectly()
    {
        var actorProvider = FakeActorProvider.WithPermissions("user-1", "Admin.Write", "Other.Read");
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(actorProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ActorProviderReturnsNull_ThrowsInvalidOperationException()
    {
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(new NullActorProvider());
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("should not reach"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Async actor provider — actor with permission

    [Fact]
    public async Task Handle_AsyncActorWithPermission_InvokesHandler()
    {
        var asyncProvider = FakeAsyncActorProvider.WithPermissions("user-1", "Admin.Write");
        var syncProvider = FakeActorProvider.NoPermissions();
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, asyncProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Done");
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Async actor provider — actor missing permission

    [Fact]
    public async Task Handle_AsyncActorMissingPermission_ReturnsForbidden()
    {
        var asyncProvider = FakeAsyncActorProvider.NoPermissions();
        var syncProvider = FakeActorProvider.WithPermissions("user-1", "Admin.Write");
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, asyncProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
        result.Error.Detail.Should().Contain("Admin.Write");
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked when permissions are missing");
    }

    #endregion

    #region Async provider preferred over sync when both registered

    [Fact]
    public async Task Handle_AsyncPreferredOverSync_WhenBothRegistered()
    {
        var syncProvider = FakeActorProvider.WithPermissions("user-1", "PermissionA");
        var asyncProvider = FakeAsyncActorProvider.WithPermissions("user-1", "Admin.Write");
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, asyncProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        // Async provider has Admin.Write → should succeed
        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AsyncPreferredOverSync_SyncPermissionsIgnored()
    {
        // Sync has the permission, async does NOT — should fail (proving async is used)
        var syncProvider = FakeActorProvider.WithPermissions("user-1", "Admin.Write");
        var asyncProvider = FakeAsyncActorProvider.NoPermissions("user-1");
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, asyncProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
        tracker.WasInvoked.Should().BeFalse("sync provider should be ignored when async is registered");
    }

    #endregion

    #region Sync fallback when async not registered

    [Fact]
    public async Task Handle_SyncFallback_WhenAsyncNotRegistered()
    {
        var syncProvider = FakeActorProvider.WithPermissions("user-1", "Admin.Write");
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Async cancellation token propagated

    [Fact]
    public async Task Handle_AsyncCancellationToken_Propagated()
    {
        using var cts = new CancellationTokenSource();
        var actor = Actor.Create("user-1", new HashSet<string>(["Admin.Write"]));
        var capturingProvider = new TokenCapturingAsyncActorProvider(actor);
        var syncProvider = FakeActorProvider.NoPermissions();
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, capturingProvider);
        var command = new AdminCommand("data");
        var next = NextDelegate.ReturningAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        await behavior.Handle(command, next, cts.Token);

        capturingProvider.LastCancellationToken.Should().Be(cts.Token);
    }

    #endregion

    #region Async null actor throws

    [Fact]
    public async Task Handle_AsyncActorProviderReturnsNull_ThrowsInvalidOperationException()
    {
        var syncProvider = FakeActorProvider.NoPermissions();
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, new NullAsyncActorProvider());
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("should not reach"));

        var act = async () => await behavior.Handle(command, next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Async delayed provider

    [Fact]
    public async Task Handle_DelayedAsyncProvider_ReturnsSuccessAfterDelay()
    {
        var actor = Actor.Create("user-1", new HashSet<string>(["Admin.Write"]));
        var delayedProvider = new DelayedAsyncActorProvider(actor, TimeSpan.FromMilliseconds(50));
        var syncProvider = FakeActorProvider.NoPermissions();
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(syncProvider, delayedProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Success("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    private sealed class NullActorProvider : IActorProvider
    {
        public Actor GetCurrentActor() => null!;
    }
}