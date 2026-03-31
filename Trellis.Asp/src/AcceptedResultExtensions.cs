namespace Trellis.Asp;

using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// RFC 9110 §15.3.3 Accepted (202) result helpers for async processing patterns.
/// </summary>
public static class AcceptedResultExtensions
{
    /// <summary>
    /// Returns 202 Accepted with an optional status monitor Location header.
    /// </summary>
    public static ActionResult<TOut> ToAcceptedActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controller,
        Func<TIn, string>? statusMonitorUri = null,
        Func<TIn, TOut>? map = null)
    {
        if (result.IsFailure)
            return result.Error.ToActionResult<TOut>(controller);

        if (statusMonitorUri is not null)
            controller.Response.Headers.Location = statusMonitorUri(result.Value);

        if (map is not null)
            return new ObjectResult(map(result.Value)) { StatusCode = 202 };

        return new StatusCodeResult(202);
    }

    /// <summary>Async Task overload.</summary>
    public static async Task<ActionResult<TOut>> ToAcceptedActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        ControllerBase controller,
        Func<TIn, string>? statusMonitorUri = null,
        Func<TIn, TOut>? map = null) =>
        (await resultTask.ConfigureAwait(false)).ToAcceptedActionResult(controller, statusMonitorUri, map);

    /// <summary>Async ValueTask overload.</summary>
    public static async ValueTask<ActionResult<TOut>> ToAcceptedActionResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        ControllerBase controller,
        Func<TIn, string>? statusMonitorUri = null,
        Func<TIn, TOut>? map = null) =>
        (await resultTask.ConfigureAwait(false)).ToAcceptedActionResult(controller, statusMonitorUri, map);
}
