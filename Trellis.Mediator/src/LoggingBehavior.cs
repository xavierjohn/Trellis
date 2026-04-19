namespace Trellis.Mediator;

using System.Diagnostics;
using global::Mediator;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pipeline behavior that logs command/query execution with duration and Result outcome.
/// Logs at Information level for success, Warning level for failure.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/>.</typeparam>
public sealed partial class LoggingBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
    where TResponse : IResult
{
    private readonly ILogger<LoggingBehavior<TMessage, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResponse>> logger)
        => _logger = logger;

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var messageName = typeof(TMessage).Name;
        var stopwatch = Stopwatch.GetTimestamp();

        LogHandling(_logger, messageName);

        var response = await next(message, cancellationToken).ConfigureAwait(false);

        var elapsed = Stopwatch.GetElapsedTime(stopwatch);

        if (response.TryGetError(out var error))
            LogHandledWithFailure(_logger, messageName, elapsed.TotalMilliseconds, error.Detail);
        else
            LogHandled(_logger, messageName, elapsed.TotalMilliseconds);

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {MessageName}")]
    private static partial void LogHandling(ILogger logger, string messageName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms")]
    private static partial void LogHandled(ILogger logger, string messageName, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms — Failed: {ErrorDetail}")]
    private static partial void LogHandledWithFailure(ILogger logger, string messageName, double elapsedMs, string errorDetail);
}