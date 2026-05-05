namespace Trellis.Http;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// Operators bridge <see cref="Task{TResult}"/> of <see cref="HttpResponseMessage"/>
/// into <see cref="Result{TValue}"/> pipelines and deserialize JSON payloads.
/// </para>
/// <para>
/// <b>Disposal contract.</b> The library owns the lifecycle of the underlying
/// <see cref="HttpResponseMessage"/> on terminal or transformative paths:
/// <list type="bullet">
///   <item><description>
///     <see cref="ToResultAsync(Task{HttpResponseMessage}, Func{HttpStatusCode, Error?}?)"/>
///     disposes the response when bare strict mapping or a supplied mapper returns a non-null
///     <see cref="Error"/> (the <c>Fail</c> path). When a supplied mapper returns
///     <see langword="null"/> or bare strict mapping sees a successful status code, the response
///     flows through and the caller still owns disposal until a subsequent <c>ReadJson*</c> call
///     consumes it.
///   </description></item>
///   <item><description>
///     The body-aware <c>ToResultAsync</c> overload disposes the response when its mapper returns a
///     non-null <see cref="Error"/>; a <see langword="null"/> return passes through unchanged.
///   </description></item>
///   <item><description>
///     <see cref="HandleNotFoundAsync"/>, <see cref="HandleConflictAsync"/>, and
///     <see cref="HandleUnauthorizedAsync"/> dispose the response on the matched-status
///     <c>Fail</c> path; non-match passes the response through unchanged.
///   </description></item>
///   <item><description>
///     <see cref="ReadJsonAsync"/>, <see cref="ReadJsonMaybeAsync"/>, and
///     <see cref="ReadJsonOrNoneOn404Async{T}"/> always dispose the response after reading
///     (success or failure), and the <c>Task&lt;Result&lt;HttpResponseMessage&gt;&gt;</c> JSON readers
///     short-circuit when the input is already a failure (no response to dispose in that case).
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
    /// When <see langword="null"/> (the default), successful status codes yield
    /// <see cref="Result.Ok{T}(T)"/> and non-success status codes are mapped to
    /// Trellis errors. When supplied, a <see langword="null"/> return passes the
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
        {
            if (message.IsSuccessStatusCode)
                return Result.Ok(message);

            var error = MapStatusToError(message);
            message.Dispose();
            return Result.Fail<HttpResponseMessage>(error);
        }

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
    /// Reads JSON from a successful HTTP response into <see cref="Maybe{T}"/>, treating
    /// <see cref="HttpStatusCode.NotFound"/> as <see cref="Maybe{T}.None"/>.
    /// </summary>
    /// <typeparam name="T">The payload type. Must be a non-nullable reference or value type.</typeparam>
    /// <param name="response">The pending HTTP response.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON metadata for <typeparamref name="T"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A result containing <see cref="Maybe{T}.None"/> for 404, a populated maybe for a
    /// successful JSON body, or a failure for other non-success statuses.
    /// </returns>
    public static async Task<Result<Maybe<T>>> ReadJsonOrNoneOn404Async<T>(
        this Task<HttpResponseMessage> response,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken ct = default)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        var message = await response.ConfigureAwait(false);

        if (message.StatusCode == HttpStatusCode.NotFound)
        {
            message.Dispose();
            return Result.Ok(Maybe<T>.None);
        }

        if (!message.IsSuccessStatusCode)
        {
            var error = MapStatusToError(message);
            message.Dispose();
            return Result.Fail<Maybe<T>>(error);
        }

        return await Result.Ok(message)
            .AsTask()
            .ReadJsonMaybeAsync(jsonTypeInfo, ct)
            .ConfigureAwait(false);
    }

    private static Error MapStatusToError(HttpResponseMessage response)
    {
        var statusCode = response.StatusCode;
        var detail = $"HTTP response returned status code {(int)statusCode} ({statusCode}).";
        var resource = ResourceRef.For("HttpResponse");

        Error error = statusCode switch
        {
            HttpStatusCode.BadRequest => new Error.BadRequest("http.bad_request"),
            HttpStatusCode.Unauthorized => new Error.Unauthorized(ExtractAuthChallenges(response)),
            HttpStatusCode.Forbidden => new Error.Forbidden("http.forbidden"),
            HttpStatusCode.NotFound => new Error.NotFound(resource),
            HttpStatusCode.MethodNotAllowed => new Error.MethodNotAllowed(ExtractAllow(response)),
            HttpStatusCode.NotAcceptable => new Error.NotAcceptable(EquatableArray<string>.Empty),
            HttpStatusCode.Conflict => new Error.Conflict(null, "http.conflict"),
            HttpStatusCode.Gone => new Error.Gone(resource),
            HttpStatusCode.PreconditionFailed => new Error.PreconditionFailed(resource, PreconditionKind.IfMatch),
            HttpStatusCode.RequestEntityTooLarge => new Error.ContentTooLarge(),
            HttpStatusCode.UnsupportedMediaType => new Error.UnsupportedMediaType(EquatableArray<string>.Empty),
            HttpStatusCode.RequestedRangeNotSatisfiable => new Error.RangeNotSatisfiable(
                ExtractCompleteLength(response),
                response.Content?.Headers.ContentRange?.Unit ?? "bytes"),
            HttpStatusCode.UnprocessableEntity => Error.UnprocessableContent.ForRule("http.unprocessable_content"),
            (HttpStatusCode)428 => new Error.PreconditionRequired(PreconditionKind.IfMatch),
            (HttpStatusCode)429 => new Error.TooManyRequests(ExtractRetryAfter(response)),
            HttpStatusCode.NotImplemented => new Error.NotImplemented("http.not_implemented"),
            HttpStatusCode.ServiceUnavailable => new Error.ServiceUnavailable(ExtractRetryAfter(response)),
            _ => new Error.InternalServerError(Guid.NewGuid().ToString("N")),
        };

        return error with { Detail = detail };
    }

    /// <summary>
    /// Extracts the response's <c>Allow</c> header values into an <see cref="EquatableArray{T}"/>.
    /// Returns an empty array when the header is absent so the caller does not need a null check.
    /// </summary>
    private static EquatableArray<string> ExtractAllow(HttpResponseMessage response)
    {
        var allow = response.Content?.Headers.Allow;
        if (allow is null || allow.Count == 0)
            return EquatableArray<string>.Empty;
        return new EquatableArray<string>([.. allow]);
    }

    /// <summary>
    /// Extracts the <c>Content-Range</c> header's complete length (the value after the slash in
    /// <c>bytes &lt;range&gt;/&lt;total&gt;</c>). Returns <c>0</c> when the header is absent or
    /// has no length component.
    /// </summary>
    private static long ExtractCompleteLength(HttpResponseMessage response)
    {
        var contentRange = response.Content?.Headers.ContentRange;
        return contentRange?.Length ?? 0L;
    }

    /// <summary>
    /// Maps the response's <c>Retry-After</c> header to a <see cref="RetryAfterValue"/>, preserving
    /// the seconds-vs-date distinction. Returns <see langword="null"/> when the header is absent
    /// or when the upstream sends a malformed (negative) delta — treating the malformed case as
    /// absent rather than throwing keeps <see cref="MapStatusToError"/> exception-free even with
    /// adversarial upstreams.
    /// </summary>
    private static RetryAfterValue? ExtractRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        if (retryAfter.Delta is { } delta)
        {
            var seconds = (long)delta.TotalSeconds;
            if (seconds < 0)
                return null;
            // Clamp huge deltas to int.MaxValue (RFC permits arbitrary large values; our typed
            // RetryAfterValue takes int seconds, which is ~68 years and more than sufficient).
            return RetryAfterValue.FromSeconds(seconds > int.MaxValue ? int.MaxValue : (int)seconds);
        }

        if (retryAfter.Date is { } date)
            return RetryAfterValue.FromDate(date);

        return null;
    }

    /// <summary>
    /// Extracts the <c>WWW-Authenticate</c> challenges from a 401 response into a typed
    /// <see cref="EquatableArray{T}"/> of <see cref="AuthChallenge"/>. Each challenge captures
    /// its scheme (e.g. <c>Bearer</c>) plus a best-effort parse of its auth parameters
    /// (<c>realm</c>, <c>error</c>, etc.) into an <c>ImmutableDictionary</c>. If the parameter
    /// string fails to parse, the challenge falls back to scheme-only rather than throwing.
    /// </summary>
    private static EquatableArray<AuthChallenge> ExtractAuthChallenges(HttpResponseMessage response)
    {
        var headers = response.Headers.WwwAuthenticate;
        if (headers.Count == 0)
            return EquatableArray<AuthChallenge>.Empty;

        var challenges = new List<AuthChallenge>(headers.Count);
        foreach (var header in headers)
        {
            if (string.IsNullOrEmpty(header.Scheme))
                continue;
            challenges.Add(BuildChallenge(header));
        }

        return challenges.Count == 0
            ? EquatableArray<AuthChallenge>.Empty
            : new EquatableArray<AuthChallenge>([.. challenges]);
    }

    /// <summary>
    /// Builds a single <see cref="AuthChallenge"/> from an
    /// <see cref="System.Net.Http.Headers.AuthenticationHeaderValue"/>, parsing the parameter
    /// string when present so <c>realm</c> / <c>error</c> / etc. round-trip into
    /// <see cref="AuthChallenge.Params"/>. Falls back to scheme-only when the parameter string
    /// is empty or no recognizable auth-params are found.
    /// </summary>
    private static AuthChallenge BuildChallenge(System.Net.Http.Headers.AuthenticationHeaderValue header)
    {
        if (string.IsNullOrEmpty(header.Parameter))
            return new AuthChallenge(header.Scheme);

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        // RFC 7235 auth-param: token "=" ( token / quoted-string ); comma-separated.
        // Capture groups: 1 = key (token), 2 = quoted-value-with-escapes, 3 = unquoted-token-value.
        foreach (System.Text.RegularExpressions.Match match in s_authParamRegex.Matches(header.Parameter))
        {
            var key = match.Groups[1].Value;
            if (string.IsNullOrEmpty(key))
                continue;
            var quoted = match.Groups[2];
            var token = match.Groups[3];
            var value = quoted.Success
                ? UnescapeQuotedPair(quoted.Value)
                : token.Value;
            builder[key] = value;
        }

        return builder.Count == 0
            ? new AuthChallenge(header.Scheme)
            : new AuthChallenge(header.Scheme, builder.ToImmutable());
    }

    // RFC 9110 §5.6.2 token + quoted-string; comma-separated auth-params.
    private static readonly System.Text.RegularExpressions.Regex s_authParamRegex =
        new(@"([A-Za-z0-9!#$%&'*+\-.^_`|~]+)\s*=\s*(?:""((?:[^""\\]|\\.)*)""|([A-Za-z0-9!#$%&'*+\-.^_`|~]+))",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Unescapes RFC 9110 §5.6.4 quoted-pair forms: <c>\X</c> becomes <c>X</c> for any visible
    /// character. Leaves characters that aren't part of an escape sequence unchanged.
    /// </summary>
    private static string UnescapeQuotedPair(string inner)
    {
        if (!inner.Contains('\\'))
            return inner;

        var sb = new System.Text.StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
                sb.Append(inner[++i]);
            else
                sb.Append(inner[i]);
        }

        return sb.ToString();
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
        ArgumentNullException.ThrowIfNull(error);

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
        ArgumentNullException.ThrowIfNull(error);

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
        ArgumentNullException.ThrowIfNull(error);

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

        // The response was awaited and is now owned by this method; the disposal contract
        // (always-dispose) requires the try/finally to cover any exception path including the
        // jsonTypeInfo null-guard. Move the null check INSIDE the try block.
        try
        {
            ArgumentNullException.ThrowIfNull(jsonTypeInfo);

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
                // Use only structured position info (line / byte). Avoid `ex.Message`
                // entirely (can include offending JSON snippet text) and `ex.Path`
                // (can contain user-controlled dictionary keys, e.g.
                // `$.customers['alice@example.com']`). Line + byte are
                // schema-free diagnostics that don't echo upstream-supplied content.
                var location = ex.LineNumber.HasValue
                    ? $" at line {ex.LineNumber}, byte {ex.BytePositionInLine ?? 0}"
                    : string.Empty;

                return Result.Fail<T>(new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                {
                    Detail = $"Failed to deserialize HTTP response to {typeof(T).Name}{location}.",
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

        // Same disposal-contract reasoning as ReadJsonAsync: the jsonTypeInfo null-guard
        // must run inside the try/finally so a null arg cannot leak the awaited response.
        try
        {
            ArgumentNullException.ThrowIfNull(jsonTypeInfo);

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