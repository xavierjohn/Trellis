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
    private readonly TrellisMediatorTelemetryOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">
    /// Telemetry redaction options resolved from DI. When <c>null</c> (i.e. not registered)
    /// the safe-by-default options are used and <see cref="Error.Detail"/> is redacted.
    /// </param>
    public LoggingBehavior(
        ILogger<LoggingBehavior<TMessage, TResponse>> logger,
        TrellisMediatorTelemetryOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new TrellisMediatorTelemetryOptions();
    }

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
        {
            // Always log the stable Code; only include Detail when explicitly opted in,
            // because Detail can contain user input / PII (ga-12).
            var summary = _options.IncludeErrorDetail
                ? error.GetDisplayMessage()
                : error.Code;
            LogHandledWithFailure(_logger, messageName, elapsed.TotalMilliseconds, summary);
        }
        else
        {
            LogHandled(_logger, messageName, elapsed.TotalMilliseconds);
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {MessageName}")]
    private static partial void LogHandling(ILogger logger, string messageName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms")]
    private static partial void LogHandled(ILogger logger, string messageName, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms — Failed: {ErrorSummary}")]
    private static partial void LogHandledWithFailure(ILogger logger, string messageName, double elapsedMs, string errorSummary);
}