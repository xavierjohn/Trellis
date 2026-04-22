namespace Trellis.Http;

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Trellis;

/// <summary>
/// Canonical Railway-Oriented HTTP extensions for <see cref="HttpResponseMessage"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the v2 (ADR-002 §7) surface for <c>Trellis.Http</c>. It collapses the v1
/// "60+ overload" surface into a small set of composable operators that bridge
/// <see cref="Task{TResult}"/> of <see cref="HttpResponseMessage"/> into
/// <see cref="Result{TValue}"/> pipelines and deserialize JSON payloads.
/// </para>
/// <para>
/// <b>Disposal contract.</b> The library owns the lifecycle of the underlying
/// <see cref="HttpResponseMessage"/> on terminal or transformative paths:
/// <list type="bullet">
///   <item><description>
///     <see cref="ToResultAsync(Task{HttpResponseMessage}, Func{HttpStatusCode, Error?}?)"/> and the
///     body-aware overload dispose the response when the supplied mapper returns a non-null
///     <see cref="Error"/> (the <c>Fail</c> path). When the mapper returns <see langword="null"/>
///     (or no mapper is supplied), the response flows through and the caller still owns disposal
///     until a subsequent <c>ReadJson*</c> call consumes it.
///   </description></item>
///   <item><description>
///     <see cref="HandleNotFoundAsync"/>, <see cref="HandleConflictAsync"/>, and
///     <see cref="HandleUnauthorizedAsync"/> dispose the response on the matched-status
///     <c>Fail</c> path; non-match passes the response through unchanged.
///   </description></item>
///   <item><description>
///     <see cref="ReadJsonAsync"/> and <see cref="ReadJsonMaybeAsync"/> always dispose the
///     response after reading (success or failure), and short-circuit when the input
///     <see cref="Result{TValue}"/> is already a failure (no response to dispose in that case).
///   </description></item>
/// </list>
/// In practice: once you call <c>ReadJson*</c>, you no longer need to dispose the
/// <see cref="HttpResponseMessage"/> yourself.
/// </para>
/// </remarks>
public static class HttpResponseExtensions
{
    /// <summary>
    /// Bridges a <see cref="Task{HttpResponseMessage}"/> into a
    /// <see cref="Task{Result}"/> of <see cref="HttpResponseMessage"/>.
    /// </summary>
    /// <param name="response">The pending HTTP response.</param>
    /// <param name="statusMap">
    /// Optional mapper from <see cref="HttpStatusCode"/> to <see cref="Error"/>.
    /// When <see langword="null"/> (the default), every status code yields
    /// <see cref="Result.Ok{T}(T)"/> and the caller is responsible for downstream
    /// status checks. When supplied, a <see langword="null"/> return passes the
    /// response through as <see cref="Result.Ok{T}(T)"/>, and a non-<see langword="null"/>
    /// return becomes a <see cref="Result.Fail{T}(Error)"/>; in the failure case
    /// the underlying <see cref="HttpResponseMessage"/> is disposed.
    /// </param>
    /// <returns>
    /// A <see cref="Task{T}"/> that completes with <see cref="Result.Ok{T}(T)"/>
    /// or <see cref="Result.Fail{T}(Error)"/> per the contract above.
    /// </returns>
    public static async Task<Result<HttpResponseMessage>> ToResultAsync(
        this Task<HttpResponseMessage> response,
        Func<HttpStatusCode, Error?>? statusMap = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        var message = await response.ConfigureAwait(false);

        if (statusMap is null)
            return Result.Ok(message);

        Error? mapped;
        try
        {
            mapped = statusMap(message.StatusCode);
        }
        catch
        {
            message.Dispose();
            throw;
        }

        if (mapped is null)
            return Result.Ok(message);

        message.Dispose();
        return Result.Fail<HttpResponseMessage>(mapped);
    }

    /// <summary>
    /// Bridges a <see cref="Task{HttpResponseMessage}"/> into a
    /// <see cref="Task{Result}"/> of <see cref="HttpResponseMessage"/>, allowing the
    /// failure mapper to inspect the response body or headers asynchronously.
    /// </summary>
    /// <param name="response">The pending HTTP response.</param>
    /// <param name="mapper">
    /// Asynchronous mapper invoked only when
    /// <see cref="HttpResponseMessage.IsSuccessStatusCode"/> is <see langword="false"/>.
    /// Returning <see langword="null"/> passes the response through as
    /// <see cref="Result.Ok{T}(T)"/>. Returning a non-<see langword="null"/>
    /// <see cref="Error"/> causes the response to be disposed and
    /// <see cref="Result.Fail{T}(Error)"/> to be returned.
    /// </param>
    /// <param name="ct">Cancellation token forwarded to <paramref name="mapper"/>.</param>
    /// <returns>The mapped <see cref="Result{T}"/>.</returns>
    /// <remarks>
    /// Replaces the v1 <c>HandleFailureAsync&lt;TContext&gt;</c> overloads. The
    /// <c>TContext</c> channel is unnecessary because closures already capture
    /// caller state.
    /// </remarks>
    public static async Task<Result<HttpResponseMessage>> ToResultAsync(
        this Task<HttpResponseMessage> response,
        Func<HttpResponseMessage, CancellationToken, Task<Error?>> mapper,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(mapper);

        var message = await response.ConfigureAwait(false);

        if (message.IsSuccessStatusCode)
            return Result.Ok(message);

        Error? error;
        try
        {
            error = await mapper(message, ct).ConfigureAwait(false);
        }
        catch
        {
            message.Dispose();
            throw;
        }

        if (error is null)
            return Result.Ok(message);

        message.Dispose();
        return Result.Fail<HttpResponseMessage>(error);
    }

    /// <summary>
    /// Maps <see cref="HttpStatusCode.NotFound"/> to a
    /// <see cref="Result.Fail{T}(Error)"/> carrying <paramref name="error"/>; any other
    /// status code passes through as <see cref="Result.Ok{T}(T)"/>.
    /// </summary>
    /// <param name="response">The pending HTTP response.</param>
    /// <param name="error">The <see cref="Error.NotFound"/> to surface on a 404 match.</param>
    /// <returns>A <see cref="Task{T}"/> producing the mapped <see cref="Result{T}"/>.</returns>
    /// <remarks>
    /// On a matched 404 the underlying <see cref="HttpResponseMessage"/> is disposed
    /// before returning. On any non-match the caller continues to own disposal until a
    /// downstream operator (typically <see cref="ReadJsonAsync"/> or
    /// <see cref="ReadJsonMaybeAsync"/>) consumes the response.
    /// </remarks>
    public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(
        this Task<HttpResponseMessage> response,
        Error.NotFound error)
    {
        ArgumentNullException.ThrowIfNull(response);

        var message = await response.ConfigureAwait(false);

        if (message.StatusCode == HttpStatusCode.NotFound)
        {
            message.Dispose();
            return Result.Fail<HttpResponseMessage>(error);
        }

        return Result.Ok(message);
    }

    /// <summary>
    /// Maps <see cref="HttpStatusCode.Conflict"/> to a
    /// <see cref="Result.Fail{T}(Error)"/> carrying <paramref name="error"/>; any other
    /// status code passes through as <see cref="Result.Ok{T}(T)"/>.
    /// </summary>
    /// <param name="response">The pending HTTP response.</param>
    /// <param name="error">The <see cref="Error.Conflict"/> to surface on a 409 match.</param>
    /// <returns>A <see cref="Task{T}"/> producing the mapped <see cref="Result{T}"/>.</returns>
    /// <remarks>
    /// On a matched 409 the underlying <see cref="HttpResponseMessage"/> is disposed
    /// before returning; on any other status the caller continues to own disposal.
    /// </remarks>
    public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(
        this Task<HttpResponseMessage> response,
        Error.Conflict error)
    {
        ArgumentNullException.ThrowIfNull(response);

        var message = await response.ConfigureAwait(false);

        if (message.StatusCode == HttpStatusCode.Conflict)
        {
            message.Dispose();
            return Result.Fail<HttpResponseMessage>(error);
        }

        return Result.Ok(message);
    }

    /// <summary>
    /// Maps <see cref="HttpStatusCode.Unauthorized"/> to a
    /// <see cref="Result.Fail{T}(Error)"/> carrying <paramref name="error"/>; any other
    /// status code passes through as <see cref="Result.Ok{T}(T)"/>.
    /// </summary>
    /// <param name="response">The pending HTTP response.</param>
    /// <param name="error">The <see cref="Error.Unauthorized"/> to surface on a 401 match.</param>
    /// <returns>A <see cref="Task{T}"/> producing the mapped <see cref="Result{T}"/>.</returns>
    /// <remarks>
    /// On a matched 401 the underlying <see cref="HttpResponseMessage"/> is disposed
    /// before returning; on any other status the caller continues to own disposal.
    /// </remarks>
    public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(
        this Task<HttpResponseMessage> response,
        Error.Unauthorized error)
    {
        ArgumentNullException.ThrowIfNull(response);

        var message = await response.ConfigureAwait(false);

        if (message.StatusCode == HttpStatusCode.Unauthorized)
        {
            message.Dispose();
            return Result.Fail<HttpResponseMessage>(error);
        }

        return Result.Ok(message);
    }

    /// <summary>
    /// Reads and deserializes the body of a successful HTTP response into
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The payload type. Must be a non-nullable reference or value type.</typeparam>
    /// <param name="response">A pending <see cref="Task{T}"/> of <see cref="Result{T}"/> of <see cref="HttpResponseMessage"/>.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON metadata for <typeparamref name="T"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// On already-failed input: short-circuits with the upstream error (no response to dispose).
    /// On success status with a deserializable body: <see cref="Result.Ok{T}(T)"/>.
    /// On non-success status, empty/null body, <see cref="HttpStatusCode.NoContent"/>,
    /// <see cref="HttpStatusCode.ResetContent"/>, or invalid JSON (<see cref="JsonException"/>):
    /// <see cref="Result.Fail{T}(Error)"/> wrapping <see cref="Error.InternalServerError"/>.
    /// </returns>
    /// <remarks>
    /// Whenever a response is read (success or failure), it is disposed before returning.
    /// </remarks>
    public static async Task<Result<T>> ReadJsonAsync<T>(
        this Task<Result<HttpResponseMessage>> response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(response);

        var result = await response.ConfigureAwait(false);
        if (!result.TryGetValue(out var message, out var error))
            return Result.Fail<T>(error);

        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        try
        {
            ct.ThrowIfCancellationRequested();

            if (!message.IsSuccessStatusCode)
                return Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"HTTP response is in a failed state for value {typeof(T).Name}. Status code: {message.StatusCode}.",
                });

            if (message.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.ResetContent)
                return Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"HTTP response had no body for value {typeof(T).Name}.",
                });

            if (message.Content is null)
                return Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"HTTP response body was null for value {typeof(T).Name}.",
                });

            var bytes = await message.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                return Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"HTTP response body was empty for value {typeof(T).Name}.",
                });

            T? value;
            try
            {
                value = JsonSerializer.Deserialize(bytes, jsonTypeInfo);
            }
            catch (JsonException ex)
            {
                return Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"Failed to deserialize HTTP response to {typeof(T).Name}: {ex.Message}",
                });
            }

            return value is null
                ? Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"HTTP response deserialized to null for value {typeof(T).Name}.",
                })
                : Result.Ok(value);
        }
        finally
        {
            message.Dispose();
        }
    }

    /// <summary>
    /// Reads and deserializes the body of a successful HTTP response into
    /// <see cref="Maybe{T}"/>, treating <see cref="HttpStatusCode.NoContent"/>,
    /// <see cref="HttpStatusCode.ResetContent"/>, an empty body, or a JSON
    /// <c>null</c> literal as <see cref="Maybe{T}.None"/>.
    /// </summary>
    /// <typeparam name="T">The payload type. Must be a non-nullable reference or value type.</typeparam>
    /// <param name="response">A pending <see cref="Task{T}"/> of <see cref="Result{T}"/> of <see cref="HttpResponseMessage"/>.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON metadata for <typeparamref name="T"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// On already-failed input: short-circuits with the upstream error (no response to dispose).
    /// On non-success status: <see cref="Result.Fail{T}(Error)"/> with
    /// <see cref="Error.InternalServerError"/>. On success status with a parseable
    /// payload: <see cref="Result.Ok{T}(T)"/> wrapping
    /// <see cref="Maybe.From{T}(T)"/> or <see cref="Maybe{T}.None"/>.
    /// </returns>
    /// <remarks>
    /// Unlike <see cref="ReadJsonAsync"/>, an invalid JSON body is **not** caught:
    /// <see cref="JsonException"/> propagates to the caller. The response is still
    /// disposed before that exception escapes. Whenever a response is read
    /// (success or exception), it is disposed before returning.
    /// </remarks>
    public static async Task<Result<Maybe<T>>> ReadJsonMaybeAsync<T>(
        this Task<Result<HttpResponseMessage>> response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(response);

        var result = await response.ConfigureAwait(false);
        if (!result.TryGetValue(out var message, out var error))
            return Result.Fail<Maybe<T>>(error);

        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        try
        {
            ct.ThrowIfCancellationRequested();

            if (!message.IsSuccessStatusCode)
                return Result.Fail<Maybe<T>>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"HTTP response is in a failed state for value {typeof(T).Name}. Status code: {message.StatusCode}.",
                });

            if (message.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.ResetContent)
                return Result.Ok(Maybe<T>.None);

            if (message.Content is null)
                return Result.Ok(Maybe<T>.None);

            var bytes = await message.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                return Result.Ok(Maybe<T>.None);

            var value = JsonSerializer.Deserialize(bytes, jsonTypeInfo);
            return Result.Ok(value is null ? Maybe<T>.None : Maybe.From(value));
        }
        finally
        {
            message.Dispose();
        }
    }
}
