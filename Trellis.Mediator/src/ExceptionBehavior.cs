namespace Trellis.Mediator;

using global::Mediator;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pipeline behavior that catches unhandled exceptions from handlers and converts
/// them to <see cref="Error.Unexpected(string, string?)"/> failures. This is a safety net — handlers
/// should not throw, but if they do, this prevents unhandled exceptions from escaping
/// the mediator pipeline.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
public sealed partial class ExceptionBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly ILogger<ExceptionBehavior<TMessage, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ExceptionBehavior(ILogger<ExceptionBehavior<TMessage, TResponse>> logger)
        => _logger = logger;

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var messageName = typeof(TMessage).Name;
            LogUnhandledException(_logger, ex, messageName);

            var error = Error.Unexpected($"An unexpected error occurred while processing {messageName}.");
            return TResponse.CreateFailure(error);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in {MessageName}")]
    private static partial void LogUnhandledException(ILogger logger, Exception ex, string messageName);
}