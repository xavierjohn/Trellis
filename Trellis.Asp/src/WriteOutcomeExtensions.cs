namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// Maps <see cref="WriteOutcome{T}"/> to ActionResult with correct status codes and headers.
/// </summary>
public static class WriteOutcomeExtensions
{
    /// <summary>
    /// Converts a <see cref="WriteOutcome{T}"/> to an <see cref="ActionResult"/> with correct HTTP status codes and headers.
    /// </summary>
    public static ActionResult ToActionResult<T, TOut>(
        this WriteOutcome<T> outcome,
        ControllerBase controller,
        Func<T, TOut>? map = null)
    {
        switch (outcome)
        {
            case WriteOutcome<T>.Created created:
                if (created.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(controller.Response, created.Metadata);
                var createdValue = map is not null ? (object?)map(created.Value) : created.Value;
                return new CreatedResult(created.Location, createdValue);

            case WriteOutcome<T>.Updated replaced:
                if (replaced.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(controller.Response, replaced.Metadata);
                var replacedValue = map is not null ? (object?)map(replaced.Value) : replaced.Value;
                return controller.Ok(replacedValue);

            case WriteOutcome<T>.UpdatedNoContent noContent:
                if (noContent.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(controller.Response, noContent.Metadata);
                return controller.NoContent();

            case WriteOutcome<T>.Accepted accepted:
                if (accepted.MonitorUri is not null)
                    controller.Response.Headers.Location = accepted.MonitorUri;
                if (accepted.RetryAfter is not null)
                    controller.Response.Headers["Retry-After"] = accepted.RetryAfter.ToHeaderValue();
                var body = map is not null ? (object?)map(accepted.StatusBody) : accepted.StatusBody;
                return new ObjectResult(body) { StatusCode = StatusCodes.Status202Accepted };

            case WriteOutcome<T>.AcceptedNoContent acceptedNoContent:
                if (acceptedNoContent.MonitorUri is not null)
                    controller.Response.Headers.Location = acceptedNoContent.MonitorUri;
                if (acceptedNoContent.RetryAfter is not null)
                    controller.Response.Headers["Retry-After"] = acceptedNoContent.RetryAfter.ToHeaderValue();
                return new StatusCodeResult(StatusCodes.Status202Accepted);

            default:
                throw new InvalidOperationException($"Unknown WriteOutcome type: {outcome.GetType().Name}");
        }
    }
}
