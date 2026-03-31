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

            case WriteOutcome<T>.Replaced replaced:
                if (replaced.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(controller.Response, replaced.Metadata);
                var replacedValue = map is not null ? (object?)map(replaced.Value) : replaced.Value;
                return controller.Ok(replacedValue);

            case WriteOutcome<T>.ReplacedNoContent noContent:
                if (noContent.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(controller.Response, noContent.Metadata);
                return controller.NoContent();

            case WriteOutcome<T>.Accepted accepted:
                if (accepted.StatusMonitorUri is not null)
                    controller.Response.Headers.Location = accepted.StatusMonitorUri;
                if (accepted.RetryAfter is not null)
                    controller.Response.Headers["Retry-After"] = accepted.RetryAfter.ToHeaderValue();
                if (accepted.StatusBody is not null)
                {
                    var body = map is not null ? (object?)map(accepted.StatusBody) : accepted.StatusBody;
                    return new ObjectResult(body) { StatusCode = StatusCodes.Status202Accepted };
                }

                return new StatusCodeResult(StatusCodes.Status202Accepted);

            default:
                throw new InvalidOperationException($"Unknown WriteOutcome type: {outcome.GetType().Name}");
        }
    }
}
