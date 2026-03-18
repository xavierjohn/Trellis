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

    private sealed class NullActorProvider : IActorProvider
    {
        public Actor GetCurrentActor() => null!;
    }
}