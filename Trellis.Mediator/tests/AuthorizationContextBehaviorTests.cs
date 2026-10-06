namespace Trellis.Mediator.Tests;

using global::Mediator;
using Trellis.Testing;

public class AuthorizationContextBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_SuccessOrFailure_EstablishesFrameAndCleansUp(bool fail)
    {
        var command = new ContextCommand();
        var behavior = new AuthorizationContextBehavior<ContextCommand, Result<string>>();
        var response = fail ? Result.Fail<string>(new Error.Forbidden("business.denied")) : Result.Ok("done");
        using var cancellation = new CancellationTokenSource();
        object? captured = null;

        var result = await behavior.Handle(command, async (message, token) =>
        {
            message.Should().BeSameAs(command);
            token.Should().Be(cancellation.Token);
            captured = AuthorizationDispatchContext.CurrentIdentity;
            captured.Should().NotBeNull();
            await Task.Yield();
            AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(captured);
            return response;
        }, cancellation.Token);

        result.Should().Be(response);
        AuthorizationDispatchContext.CurrentIdentity.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ExceptionOrCancellation_RestoresParent(bool cancel)
    {
        using var outer = AuthorizationDispatchContext.Begin<ContextCommand, Result<string>>(new());
        var parent = AuthorizationDispatchContext.CurrentIdentity;
        var behavior = new AuthorizationContextBehavior<ContextCommand, Result<string>>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception expected = cancel
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("handler failed");

        var invoke = async () => await behavior.Handle(new(), async (_, _) =>
        {
            AuthorizationDispatchContext.CurrentIdentity.Should().NotBeSameAs(parent);
            await Task.Yield();
            throw expected;
        }, cancellation.Token);

        (await invoke.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(expected);
        AuthorizationDispatchContext.CurrentIdentity.Should().BeSameAs(parent);
    }

    private sealed record ContextCommand : ICommand<Result<string>>, IAuthorize
    {
        public IReadOnlyList<string> RequiredPermissions => [];
    }
}
