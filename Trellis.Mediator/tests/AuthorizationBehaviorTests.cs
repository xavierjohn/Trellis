using Trellis.Testing;
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
            Result.Ok("Done"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Done");
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
            Result.Ok("should not reach"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        result.UnwrapError().Detail.Should().Be("Insufficient permissions.");
        result.UnwrapError().Detail.Should().NotContain("Admin.Write");
        tracker.WasInvoked.Should().BeFalse("handler should not be invoked when permissions are missing");
    }

    [Fact]
    public async Task Handle_ActorMissingMultiplePermissions_ErrorListsAllMissing()
    {
        var actorProvider = FakeActorProvider.WithPermissions("user-1", "Other.Read");
        var behavior = new AuthorizationBehavior<MultiPermissionCommand, Result<string>>(actorProvider);
        var command = new MultiPermissionCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<MultiPermissionCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Be("Insufficient permissions.");
        result.UnwrapError().Detail.Should().NotContain("Orders.Write");
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
            Result.Ok("Done"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

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
            Result.Ok("Done"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ActorProviderReturnsNone_EmitsUnauthorized()
    {
        // "No authenticated actor" is modelled as Maybe<Actor>.None on the IActorProvider
        // contract — client-error state, not an exception. The authorization pipeline must
        // short-circuit with Error.AuthenticationRequired (HTTP 401) and must not invoke the handler.
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(new NoActorProvider());
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.AuthenticationRequired>();
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Cancellation token propagated

    [Fact]
    public async Task Handle_CancellationToken_Propagated()
    {
        using var cts = new CancellationTokenSource();
        var actor = Actor.Create("user-1", new HashSet<string>(["Admin.Write"]));
        var capturingProvider = new TokenCapturingActorProvider(actor);
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(capturingProvider);
        var command = new AdminCommand("data");
        var next = NextDelegate.ReturningAsync<AdminCommand, Result<string>>(
            Result.Ok("Done"));

        await behavior.HandleWithContext(command, next, cts.Token);

        capturingProvider.LastCancellationToken.Should().Be(cts.Token);
    }

    #endregion

    #region Delayed provider

    [Fact]
    public async Task Handle_DelayedProvider_ReturnsSuccessAfterDelay()
    {
        var actor = Actor.Create("user-1", new HashSet<string>(["Admin.Write"]));
        var delayedProvider = new DelayedActorProvider(actor, TimeSpan.FromMilliseconds(50));
        var behavior = new AuthorizationBehavior<AdminCommand, Result<string>>(delayedProvider);
        var command = new AdminCommand("data");
        var (next, tracker) = NextDelegate.TrackingAsync<AdminCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.HandleWithContext(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    /// <summary>
    /// Actor provider that returns <see cref="Maybe{T}.None"/> — represents an unauthenticated
    /// request. The authorization pipeline maps this to <see cref="Error.AuthenticationRequired"/> (HTTP 401).
    /// </summary>
    private sealed class NoActorProvider : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Maybe<Actor>.None);
    }

    /// <summary>
    /// Actor provider that introduces a delay, for testing genuine async behavior.
    /// </summary>
    private sealed class DelayedActorProvider(Actor actor, TimeSpan delay) : IActorProvider
    {
        public async Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return Maybe.From(actor);
        }
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
}