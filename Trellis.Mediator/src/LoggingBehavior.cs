namespace Trellis.Mediator;

using System.Diagnostics;
using global::Mediator;
using Microsoft.Extensions.Logging;

/// <summary>
/// Pipeline behavior that logs command/query execution with duration and Result outcome.
/// Logs at <see cref="LogLevel.Debug"/> for the per-message start/end pair (cross-cutting
/// observability noise belongs at Debug, not Information; consumers who want per-call timing
/// in production raise the level via <c>"Trellis.Mediator": "Debug"</c> in their logging
/// configuration). Expected caller/domain failures are logged at <see cref="LogLevel.Information"/>;
/// unexpected, dependency, or opaque transport failures are logged at <see cref="LogLevel.Warning"/>.
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
    /// Telemetry redaction options resolved from DI. Under <see cref="ServiceCollectionExtensions.AddTrellisBehaviors(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
    /// the singleton is always registered, so this argument is non-null in production. The
    /// optional-null fallback exists only for consumers that instantiate the behavior outside
    /// of DI (custom test fixtures); when null, the safe-by-default options are used and
    /// <see cref="Error.Detail"/> is redacted.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is null.</exception>
    public LoggingBehavior(
        ILogger<LoggingBehavior<TMessage, TResponse>> logger,
        TrellisMediatorTelemetryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
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
            var logLevel = ClassifyFailureLogLevel(error);

            if (logLevel == LogLevel.Warning)
            {
                LogHandledWithUnexpectedFailure(_logger, messageName, elapsed.TotalMilliseconds, summary);
            }
            else
            {
                LogHandledWithExpectedFailure(_logger, messageName, elapsed.TotalMilliseconds, summary);
            }
        }
        else
        {
            LogHandled(_logger, messageName, elapsed.TotalMilliseconds);
        }

        return response;
    }

    private static LogLevel ClassifyFailureLogLevel(Error error) =>
        error switch
        {
            // Expected caller/domain outcomes are normal control flow; internal, dependency,
            // and opaque transport failures stay at Warning so operators see actionable signals.
            Error.InvalidInput or Error.InvariantViolation or Error.NotFound or Error.Gone
                or Error.Conflict or Error.AuthenticationRequired or Error.Forbidden
                or Error.RateLimited => LogLevel.Information,
            Error.Aggregate aggregate => ClassifyAggregateFailureLogLevel(aggregate),
            Error.Unavailable or Error.Unexpected or Error.TransportFault => LogLevel.Warning,
            _ => LogLevel.Warning,
        };

    private static LogLevel ClassifyAggregateFailureLogLevel(Error.Aggregate aggregate)
    {
        foreach (var error in aggregate.Errors.Items)
        {
            if (ClassifyFailureLogLevel(error) >= LogLevel.Warning)
                return LogLevel.Warning;
        }

        return LogLevel.Information;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {MessageName}")]
    private static partial void LogHandling(ILogger logger, string messageName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms")]
    private static partial void LogHandled(ILogger logger, string messageName, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms — Failed: {ErrorSummary}")]
    private static partial void LogHandledWithExpectedFailure(ILogger logger, string messageName, double elapsedMs, string errorSummary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handled {MessageName} in {ElapsedMs:0.00}ms — Failed: {ErrorSummary}")]
    private static partial void LogHandledWithUnexpectedFailure(ILogger logger, string messageName, double elapsedMs, string errorSummary);
}
