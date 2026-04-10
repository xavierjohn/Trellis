namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// Maps <see cref="WriteOutcome{T}"/> to ActionResult/IResult with correct status codes and headers.
/// </summary>
public static class WriteOutcomeExtensions
{
    /// <summary>
    /// Converts a <see cref="WriteOutcome{T}"/> to an <see cref="ActionResult"/> with correct HTTP status codes and headers,
    /// honoring the RFC 7240 <c>Prefer</c> request header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the <c>Prefer: return=minimal</c> header is present and the outcome is <see cref="WriteOutcome{T}.Updated"/>,
    /// returns 204 No Content instead of 200 OK with body. When <c>Prefer: return=representation</c> is present,
    /// returns 200 OK with body (the default behavior for Updated).
    /// </para>
    /// <para>
    /// The <c>Preference-Applied</c> response header is emitted when a <c>return</c> preference is honored.
    /// Other outcomes (<see cref="WriteOutcome{T}.Created"/>, <see cref="WriteOutcome{T}.UpdatedNoContent"/>,
    /// <see cref="WriteOutcome{T}.Accepted"/>, <see cref="WriteOutcome{T}.AcceptedNoContent"/>) are not
    /// affected by the <c>return</c> preference.
    /// </para>
    /// </remarks>
    public static ActionResult ToActionResult<T, TOut>(
        this WriteOutcome<T> outcome,
        ControllerBase controller,
        Func<T, TOut>? map = null)
    {
        var prefer = PreferHeader.Parse(controller.Request);

        if (outcome is WriteOutcome<T>.Updated replaced && prefer.ReturnMinimal)
        {
            if (replaced.Metadata is not null)
                ActionResultExtensions.ApplyMetadataHeaders(controller.Response, replaced.Metadata);
            AppendVaryPrefer(controller.Response);
            controller.Response.Headers["Preference-Applied"] = "return=minimal";
            return controller.NoContent();
        }

        var result = MapOutcomeToActionResult(outcome, controller, map);

        if (outcome is WriteOutcome<T>.Updated)
        {
            AppendVaryPrefer(controller.Response);
            if (prefer.ReturnRepresentation)
                controller.Response.Headers["Preference-Applied"] = "return=representation";
        }

        return result;
    }

    private static ActionResult MapOutcomeToActionResult<T, TOut>(WriteOutcome<T> outcome, ControllerBase controller, Func<T, TOut>? map)
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

    private static void AppendVaryPrefer(HttpResponse response)
    {
        // Vary may be a single comma-separated string (e.g., "Accept, Accept-Encoding")
        // or multiple StringValues entries. Check both forms for "Prefer".
        var existing = response.Headers.Vary;
        foreach (var entry in existing)
        {
            if (entry is null) continue;
            foreach (var part in entry.Split(',', StringSplitOptions.TrimEntries))
                if (string.Equals(part, "Prefer", StringComparison.OrdinalIgnoreCase))
                    return;
        }

        response.Headers.Append("Vary", "Prefer");
    }

    #region ToUpdatedActionResult — Convenience extensions for Result<T> update responses with Prefer support

    /// <summary>
    /// Converts a successful <see cref="Result{TIn}"/> to an updated response, honoring RFC 7240 <c>Prefer</c>.
    /// Returns 200 OK with body by default, or 204 No Content when <c>Prefer: return=minimal</c> is present.
    /// On failure, returns the appropriate error response.
    /// </summary>
    /// <typeparam name="TIn">The domain type in the result.</typeparam>
    /// <typeparam name="TOut">The mapped output type for the response body.</typeparam>
    /// <param name="result">The result from the update operation.</param>
    /// <param name="controller">The controller context.</param>
    /// <param name="metadata">Optional representation metadata (ETag, Last-Modified, etc.).</param>
    /// <param name="map">Function to transform the domain value to a response DTO.</param>
    /// <returns>An ActionResult with appropriate status code and headers.</returns>
    public static ActionResult<TOut> ToUpdatedActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controller,
        RepresentationMetadata? metadata,
        Func<TIn, TOut> map)
    {
        if (result.IsFailure)
            return result.Error.ToActionResult<TOut>(controller);

        var outcome = new WriteOutcome<TIn>.Updated(result.Value, metadata);
        return outcome.ToActionResult(controller, map);
    }

    /// <summary>
    /// Async Task variant of <see cref="ToUpdatedActionResult{TIn,TOut}(Result{TIn}, ControllerBase, RepresentationMetadata, Func{TIn, TOut})"/>.
    /// </summary>
    public static async Task<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        ControllerBase controller,
        RepresentationMetadata? metadata,
        Func<TIn, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedActionResult(controller, metadata, map);
    }

    /// <summary>
    /// Async ValueTask variant of <see cref="ToUpdatedActionResult{TIn,TOut}(Result{TIn}, ControllerBase, RepresentationMetadata, Func{TIn, TOut})"/>.
    /// </summary>
    public static async ValueTask<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        ControllerBase controller,
        RepresentationMetadata? metadata,
        Func<TIn, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedActionResult(controller, metadata, map);
    }

    /// <summary>
    /// Converts a successful <see cref="Result{TIn}"/> to an updated response with dynamic metadata,
    /// honoring RFC 7240 <c>Prefer</c>.
    /// </summary>
    /// <typeparam name="TIn">The domain type in the result.</typeparam>
    /// <typeparam name="TOut">The mapped output type for the response body.</typeparam>
    /// <param name="result">The result from the update operation.</param>
    /// <param name="controller">The controller context.</param>
    /// <param name="metadataSelector">Function to build metadata from the domain value (e.g., extract ETag).</param>
    /// <param name="map">Function to transform the domain value to a response DTO.</param>
    /// <returns>An ActionResult with appropriate status code and headers.</returns>
    public static ActionResult<TOut> ToUpdatedActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controller,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map)
    {
        if (result.IsFailure)
            return result.Error.ToActionResult<TOut>(controller);

        var metadata = metadataSelector(result.Value);
        var outcome = new WriteOutcome<TIn>.Updated(result.Value, metadata);
        return outcome.ToActionResult(controller, map);
    }

    /// <summary>
    /// Async Task variant of <see cref="ToUpdatedActionResult{TIn,TOut}(Result{TIn}, ControllerBase, Func{TIn, RepresentationMetadata}, Func{TIn, TOut})"/>.
    /// </summary>
    public static async Task<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        ControllerBase controller,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedActionResult(controller, metadataSelector, map);
    }

    /// <summary>
    /// Async ValueTask variant of <see cref="ToUpdatedActionResult{TIn,TOut}(Result{TIn}, ControllerBase, Func{TIn, RepresentationMetadata}, Func{TIn, TOut})"/>.
    /// </summary>
    public static async ValueTask<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        ControllerBase controller,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedActionResult(controller, metadataSelector, map);
    }

    #endregion

    #region WriteOutcome → Minimal API IResult (Prefer-aware)

    /// <summary>
    /// Converts a <see cref="WriteOutcome{T}"/> to a Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/>
    /// with correct HTTP status codes and headers, honoring the RFC 7240 <c>Prefer</c> request header.
    /// The domain value is serialized directly without transformation.
    /// </summary>
    /// <typeparam name="T">The domain type contained in the outcome.</typeparam>
    /// <param name="outcome">The write outcome to convert.</param>
    /// <param name="httpContext">The HTTP context (used to read Prefer header and set response headers).</param>
    /// <returns>An <see cref="Microsoft.AspNetCore.Http.IResult"/> with appropriate status code, headers, and optional body.</returns>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(
        this WriteOutcome<T> outcome,
        HttpContext httpContext)
        => outcome.ToHttpResult<T, T>(httpContext, null);

    /// <summary>
    /// Converts a <see cref="WriteOutcome{T}"/> to a Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/>
    /// with correct HTTP status codes and headers, honoring the RFC 7240 <c>Prefer</c> request header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the <c>Prefer: return=minimal</c> header is present and the outcome is <see cref="WriteOutcome{T}.Updated"/>,
    /// returns 204 No Content instead of 200 OK with body. When <c>Prefer: return=representation</c> is present,
    /// returns 200 OK with body (the default behavior for Updated).
    /// </para>
    /// <para>
    /// The <c>Preference-Applied</c> response header is emitted when a <c>return</c> preference is honored.
    /// Other outcomes (<see cref="WriteOutcome{T}.Created"/>, <see cref="WriteOutcome{T}.UpdatedNoContent"/>,
    /// <see cref="WriteOutcome{T}.Accepted"/>, <see cref="WriteOutcome{T}.AcceptedNoContent"/>) are not
    /// affected by the <c>return</c> preference.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The domain type contained in the outcome.</typeparam>
    /// <typeparam name="TOut">The mapped output type for the response body.</typeparam>
    /// <param name="outcome">The write outcome to convert.</param>
    /// <param name="httpContext">The HTTP context (used to read Prefer header and set response headers).</param>
    /// <param name="map">Function to transform the domain value to a response DTO.</param>
    /// <returns>An <see cref="Microsoft.AspNetCore.Http.IResult"/> with appropriate status code, headers, and optional body.</returns>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T, TOut>(
        this WriteOutcome<T> outcome,
        HttpContext httpContext,
        Func<T, TOut>? map)
    {
        var response = httpContext.Response;
        var prefer = PreferHeader.Parse(httpContext.Request);

        switch (outcome)
        {
            case WriteOutcome<T>.Created created:
                if (created.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(response, created.Metadata);
                if (map is not null)
                    return Results.Created(created.Location, map(created.Value));
                return Results.Created(created.Location, created.Value);

            case WriteOutcome<T>.Updated replaced:
                if (replaced.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(response, replaced.Metadata);

                if (prefer.ReturnMinimal)
                {
                    AppendVaryPrefer(response);
                    response.Headers["Preference-Applied"] = "return=minimal";
                    return Results.NoContent();
                }

                AppendVaryPrefer(response);
                if (prefer.ReturnRepresentation)
                    response.Headers["Preference-Applied"] = "return=representation";

                if (map is not null)
                    return Results.Ok(map(replaced.Value));
                return Results.Ok(replaced.Value);

            case WriteOutcome<T>.UpdatedNoContent noContent:
                if (noContent.Metadata is not null)
                    ActionResultExtensions.ApplyMetadataHeaders(response, noContent.Metadata);
                return Results.NoContent();

            case WriteOutcome<T>.Accepted accepted:
                if (accepted.RetryAfter is not null)
                    response.Headers["Retry-After"] = accepted.RetryAfter.ToHeaderValue();
                if (map is not null)
                    return Results.Accepted(accepted.MonitorUri, map(accepted.StatusBody));
                return Results.Accepted(accepted.MonitorUri, accepted.StatusBody);

            case WriteOutcome<T>.AcceptedNoContent acceptedNoContent:
                if (acceptedNoContent.MonitorUri is not null)
                    response.Headers.Location = acceptedNoContent.MonitorUri;
                if (acceptedNoContent.RetryAfter is not null)
                    response.Headers["Retry-After"] = acceptedNoContent.RetryAfter.ToHeaderValue();
                return Results.StatusCode(StatusCodes.Status202Accepted);

            default:
                throw new InvalidOperationException($"Unknown WriteOutcome type: {outcome.GetType().Name}");
        }
    }

    #endregion
}