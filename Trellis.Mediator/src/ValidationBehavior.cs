namespace Trellis.Mediator;

using global::Mediator;

/// <summary>
/// Pipeline behavior that validates messages implementing <see cref="IValidate"/>
/// before they reach the handler. If validation fails, returns the failure Result
/// without calling the handler.
/// </summary>
/// <typeparam name="TMessage">The message type, constrained to <see cref="IValidate"/>.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
public sealed class ValidationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IValidate, global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var validationResult = message.Validate();
        if (validationResult.TryGetError(out var error))
            return new ValueTask<TResponse>(TResponse.CreateFailure(error));

        return next(message, cancellationToken);
    }
}