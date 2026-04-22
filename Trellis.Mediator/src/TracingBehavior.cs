namespace Trellis.Mediator;

using System.Diagnostics;
using global::Mediator;

/// <summary>
/// Pipeline behavior that creates an OpenTelemetry Activity for each command/query.
/// Tags the activity with Result status and error details on failure.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type, constrained to <see cref="IResult"/>.</typeparam>
public sealed class TracingBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::Mediator.IMessage
    where TResponse : IResult
{
    /// <summary>
    /// The name used for the <see cref="ActivitySource"/> that traces mediator pipeline operations.
    /// </summary>
    public const string ActivitySourceName = "Trellis.Mediator";
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly TrellisMediatorTelemetryOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingBehavior{TMessage, TResponse}"/> class.
    /// </summary>
    /// <param name="options">
    /// Telemetry redaction options resolved from DI. When <c>null</c> (i.e. not registered)
    /// the safe-by-default options are used and <see cref="Error.Detail"/> is redacted from
    /// the activity status description.
    /// </param>
    public TracingBehavior(TrellisMediatorTelemetryOptions? options = null)
        => _options = options ?? new TrellisMediatorTelemetryOptions();

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var messageName = typeof(TMessage).Name;

        using var activity = ActivitySource.StartActivity(messageName);

        TResponse response;
        try
        {
            response = await next(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }

        if (activity is not null)
        {
            if (response.TryGetError(out var error))
            {
                // The Code and stable type tags are operator-defined and PII-free. The Detail
                // string is opt-in (ga-12) — it can carry user input or domain payloads.
                var description = _options.IncludeErrorDetail ? error.GetDisplayMessage() : null;
                activity.SetStatus(ActivityStatusCode.Error, description);
                activity.SetTag("error.type", FormatErrorTypeName(error.GetType()));
                activity.SetTag("error.code", error.Code);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
        }

        return response;
    }

    private static string FormatErrorTypeName(Type errorType)
    {
        var declaring = errorType.DeclaringType;
        return declaring is null ? errorType.Name : $"{declaring.Name}.{errorType.Name}";
    }
}