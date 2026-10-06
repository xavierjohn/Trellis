namespace Trellis.Mediator.Tests.Helpers;

using global::Mediator;

internal static class AuthorizationTestPipeline
{
    internal static ValueTask<TResponse> HandleWithContext<TMessage, TResponse>(
        this IPipelineBehavior<TMessage, TResponse> behavior,
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
        where TMessage : IAuthorizationMessage, IMessage
        where TResponse : IResult, IFailureFactory<TResponse>
        => new AuthorizationContextBehavior<TMessage, TResponse>().Handle(
            message, (current, token) => behavior.Handle(current, next, token), cancellationToken);
}
