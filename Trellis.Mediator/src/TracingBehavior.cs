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
    internal static readonly ActivitySource ActivitySource = new("Trellis.Mediator");

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
            if (response.IsFailure)
            {
                activity.SetStatus(ActivityStatusCode.Error, response.Error.Detail);
                activity.SetTag("error.type", response.Error.GetType().Name);
                activity.SetTag("error.code", response.Error.Code);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
        }

        return response;
    }
}