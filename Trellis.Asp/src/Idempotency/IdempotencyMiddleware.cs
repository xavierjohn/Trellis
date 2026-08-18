namespace Trellis.Asp.Idempotency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Middleware that implements the IETF Idempotency-Key header semantics. When an endpoint is
/// marked with <c>IdempotentAttribute</c> and the configured method matches, the middleware
/// buffers the request body, computes a deterministic fingerprint, and consults an
/// <c>IIdempotencyStore</c> to either reserve, replay, conflict, or reject the request.
/// </summary>
/// <remarks>
/// <para>
/// The middleware is fail-open by design for endpoints that did not opt in: if the
/// <c>IdempotentAttribute</c> metadata is absent the request flows through untouched. The
/// store is consulted exclusively for opted-in endpoints carrying a parseable Idempotency-Key
/// header.
/// </para>
/// <para>
/// <see cref="IIdempotencyStore"/> and <see cref="IIdempotencyScopeResolver"/> are resolved
/// per-request via <see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService{T}(IServiceProvider)"/>
/// on <see cref="HttpContext.RequestServices"/> only after the endpoint is confirmed to opt
/// in and the request has a usable key and fingerprint. This lazy resolution preserves the
/// per-request scope for registrations with <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped"/>
/// (for example an EF-backed store that depends on a scoped <c>DbContext</c>) while avoiding
/// the cost of constructing those services for pass-through requests.
/// </para>
/// </remarks>
public sealed partial class IdempotencyMiddleware
{
    private static readonly TimeSpan FinalizationTimeout = TimeSpan.FromSeconds(5);

    private readonly RequestDelegate _next;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    /// <summary>Initializes a new instance of the <see cref="IdempotencyMiddleware"/> class.</summary>
    public IdempotencyMiddleware(
        RequestDelegate next,
        IOptions<IdempotencyOptions> options,
        ILogger<IdempotencyMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Processes a single HTTP request.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <remarks>
    /// <see cref="IIdempotencyStore"/> and <see cref="IIdempotencyScopeResolver"/> are resolved
    /// explicitly from <see cref="HttpContext.RequestServices"/> only after the endpoint is
    /// confirmed to opt in and the request has both a usable key and a fingerprint. Resolving
    /// them as <c>InvokeAsync</c> parameters would force ASP.NET Core to construct them for
    /// every request — including pass-through requests on non-opted-in endpoints — which is
    /// wasteful when an EF-backed store creates a scoped <c>DbContext</c> per request.
    /// </remarks>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();
        var idempotentMeta = endpoint?.Metadata.GetMetadata<IdempotentAttribute>();
        if (idempotentMeta is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!_options.Methods.Contains(context.Request.Method))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var rawValues) || rawValues.Count == 0)
        {
            if (_options.RequireKeyOnOptedInEndpoints)
            {
                await WriteProblemAsync(context, 400, "idempotency.key_required",
                    $"This endpoint requires the {_options.HeaderName} header.").ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        if (rawValues.Count > 1)
        {
            await WriteProblemAsync(context, 400, "idempotency.key_duplicate",
                $"Multiple {_options.HeaderName} headers were received.").ConfigureAwait(false);
            return;
        }

        if (!IdempotencyKeyParser.TryParse(rawValues[0], _options.HeaderName, out var key, out var parseError))
        {
            await WriteProblemAsync(context, 400, "idempotency.key_invalid", parseError ?? $"Invalid {_options.HeaderName} header.").ConfigureAwait(false);
            return;
        }

        if (key.Length > _options.MaxKeyLength)
        {
            await WriteProblemAsync(context, 400, "idempotency.key_too_long",
                $"{_options.HeaderName} length {key.Length} exceeds maximum {_options.MaxKeyLength}.").ConfigureAwait(false);
            return;
        }

        var bodyBuffer = await BufferRequestBodyAsync(context, _options.MaxRequestBodyBytes).ConfigureAwait(false);
        if (bodyBuffer is null)
        {
            await WriteProblemAsync(context, 413, "idempotency.request_body_too_large",
                $"Request body exceeds idempotency size limit of {_options.MaxRequestBodyBytes} bytes.").ConfigureAwait(false);
            return;
        }

        context.Request.Body = new MemoryStream(bodyBuffer);

        var fingerprint = IdempotencyFingerprint.Compute(context, bodyBuffer, _options);

        // Resolve store and scope resolver lazily, after pre-checks have decided that this
        // request needs idempotency. Eager InvokeAsync parameter injection would build them
        // (and any scoped EF-backed DbContext) for every request that ultimately passes through.
        var store = context.RequestServices.GetRequiredService<IIdempotencyStore>();
        var scopeResolver = context.RequestServices.GetRequiredService<IIdempotencyScopeResolver>();

        var scope = await scopeResolver.ResolveAsync(context, context.RequestAborted).ConfigureAwait(false);

        var outcome = await store.TryReserveAsync(scope, key, fingerprint, context.RequestAborted).ConfigureAwait(false);

        switch (outcome)
        {
            case IdempotencyReservationOutcome.Reserved reserved:
                await ExecuteAndCaptureAsync(context, store, scope, key, reserved.ReservationId, fingerprint).ConfigureAwait(false);
                return;

            case IdempotencyReservationOutcome.AlreadyInFlight inFlight:
                await WriteInFlightAsync(context, inFlight.RetryAfter, _options.HeaderName).ConfigureAwait(false);
                return;

            case IdempotencyReservationOutcome.Replay replay:
                await WriteReplayAsync(context, replay.Snapshot).ConfigureAwait(false);
                return;

            case IdempotencyReservationOutcome.BodyHashMismatch:
                await WriteProblemAsync(
                    context,
                    _options.MismatchStatusCode,
                    "idempotency.key_reused_with_different_body",
                    $"The {_options.HeaderName} value was reused with a different request body or headers.").ConfigureAwait(false);
                return;

            default:
                throw new InvalidOperationException($"Unhandled idempotency outcome {outcome.GetType().Name}.");
        }
    }

    private static async Task<byte[]?> BufferRequestBodyAsync(HttpContext context, long maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        long total = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(chunk.AsMemory(0, chunk.Length), context.RequestAborted).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var json = SerializeProblemDocument(statusCode, ReasonPhrase(statusCode), detail, context.Request.GetEncodedPathAndQuery(), code);
        await context.Response.Body.WriteAsync(json, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteInFlightAsync(HttpContext context, TimeSpan retryAfter, string headerName)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = 409;
        context.Response.ContentType = "application/problem+json";
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);

        var json = SerializeProblemDocument(409, "Conflict",
            $"A request with this {headerName} is already in flight.",
            context.Request.GetEncodedPathAndQuery(),
            "idempotency.in_flight");
        await context.Response.Body.WriteAsync(json, context.RequestAborted).ConfigureAwait(false);
    }

    private void WriteReplayPreamble(HttpContext context, IdempotencyResponseSnapshot snapshot)
    {
        context.Response.Clear();
        context.Response.StatusCode = snapshot.StatusCode;
        foreach (var header in snapshot.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        context.Response.Headers[_options.ReplayHeaderName] = "true";
    }

    private async Task WriteReplayAsync(HttpContext context, IdempotencyResponseSnapshot snapshot)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        WriteReplayPreamble(context, snapshot);

        if (snapshot.Body.Length > 0)
        {
            await context.Response.Body.WriteAsync(snapshot.Body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static byte[] SerializeProblemDocument(int status, string title, string detail, string instance, string code)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "about:blank");
            writer.WriteString("title", title);
            writer.WriteNumber("status", status);
            writer.WriteString("detail", detail);
            writer.WriteString("instance", instance);
            writer.WriteString("code", code);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static readonly System.Collections.Generic.HashSet<string> ResponseHeadersExcludedFromSnapshot = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date",
        "Server",
        "Transfer-Encoding",
        "Connection",
        "Keep-Alive",
        "Upgrade",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
    };

    private async Task ExecuteAndCaptureAsync(HttpContext context, IIdempotencyStore store, string scope, string key, string reservationId, string fingerprint)
    {
        // Lazy: hash only the requests that actually take an abandon/log path (the happy path
        // pays zero SHA-256 cost). The cached string is reused if multiple log sites fire.
        string? cachedKeyHash = null;
        string GetKeyHash() => cachedKeyHash ??= RedactKeyForLogs(key);

        var originalBodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
        if (originalBodyFeature is null)
        {
            LogCaptureSkippedNoFeature(_logger, context.Request.Path);
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch
            {
                await SafeAbandonAsync(store, _logger, scope, key, reservationId, GetKeyHash()).ConfigureAwait(false);
                throw;
            }

            await SafeAbandonAsync(store, _logger, scope, key, reservationId, GetKeyHash()).ConfigureAwait(false);
            return;
        }

        using var capture = new CapturingResponseBodyFeature(originalBodyFeature, _options.MaxResponseBodyBytes);
        context.Features.Set<IHttpResponseBodyFeature>(capture);

        var headerSnapshot = new HeaderSnapshot();
        var includeSetCookie = _options.IncludeSetCookieInSnapshot;
        context.Response.OnStarting(state =>
        {
            var snap = (HeaderSnapshot)state;
            snap.StatusCode = context.Response.StatusCode;
            foreach (var h in context.Response.Headers)
            {
                if (ResponseHeadersExcludedFromSnapshot.Contains(h.Key))
                    continue;
                if (!includeSetCookie && string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    continue;
                snap.Headers[h.Key] = h.Value.Select(v => v ?? string.Empty).ToArray();
            }

            snap.Captured = true;
            return Task.CompletedTask;
        }, headerSnapshot);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch
        {
            context.Features.Set<IHttpResponseBodyFeature>(originalBodyFeature);
            await SafeAbandonAsync(store, _logger, scope, key, reservationId, GetKeyHash()).ConfigureAwait(false);
            throw;
        }

        // Drain any bytes the handler wrote via Response.BodyWriter without an explicit
        // FlushAsync. Without this, those bytes would sit in the cached PipeWriter buffer
        // until Dispose() runs (after the snapshot is read) — the client would still receive
        // them, but the persisted snapshot body would be empty or truncated. Use the bounded
        // FinalizationTimeout token rather than context.RequestAborted so a late client
        // disconnect does not leak a reservation we still need to abandon.
        using (var flushCts = new CancellationTokenSource(FinalizationTimeout))
        {
            try
            {
                await capture.FlushCachedWriterAsync(flushCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var keyHash = GetKeyHash();
                LogCaptureFlushFailed(_logger, ex, keyHash);
                context.Features.Set<IHttpResponseBodyFeature>(originalBodyFeature);
                await SafeAbandonAsync(store, _logger, scope, key, reservationId, keyHash).ConfigureAwait(false);
                return;
            }
        }

        context.Features.Set<IHttpResponseBodyFeature>(originalBodyFeature);

        var bytes = capture.GetCapturedBytes();
        if (bytes is null)
        {
            // Capture aborted (response too large, SendFileAsync, or explicit abort) — abandon
            // the reservation so the next retry can re-execute.
            var keyHash = GetKeyHash();
            LogCaptureAbandoned(_logger, keyHash);
            await SafeAbandonAsync(store, _logger, scope, key, reservationId, keyHash).ConfigureAwait(false);
            return;
        }

        if (!headerSnapshot.Captured)
        {
            // Handler completed without flushing the response (typical for 204 No Content or
            // any endpoint that writes only headers/status before returning). Snapshot the
            // current Response state directly so the next retry replays the same outcome.
            headerSnapshot.StatusCode = context.Response.StatusCode;
            foreach (var h in context.Response.Headers)
            {
                if (ResponseHeadersExcludedFromSnapshot.Contains(h.Key))
                    continue;
                if (!includeSetCookie && string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    continue;
                headerSnapshot.Headers[h.Key] = h.Value.Select(v => v ?? string.Empty).ToArray();
            }
        }

        var trailersFeature = context.Features.Get<IHttpResponseTrailersFeature>();
        if (trailersFeature?.Trailers is { Count: > 0 })
        {
            // Response trailers cannot be replayed by the snapshot writer (which only restores
            // status + headers + body), so any response that wrote trailers is treated as
            // non-cacheable and the reservation is released for retry.
            var keyHash = GetKeyHash();
            LogTrailersAbandoned(_logger, keyHash);
            await SafeAbandonAsync(store, _logger, scope, key, reservationId, keyHash).ConfigureAwait(false);
            return;
        }

        if (headerSnapshot.StatusCode is >= 500 and <= 599)
        {
            // 5xx responses are treated as transient per the IIdempotencyStore.AbandonAsync
            // contract: caching them would deny the client a real retry that might succeed.
            var keyHash = GetKeyHash();
            LogServerErrorAbandoned(_logger, keyHash, headerSnapshot.StatusCode);
            await SafeAbandonAsync(store, _logger, scope, key, reservationId, keyHash).ConfigureAwait(false);
            return;
        }

        var snapshot = new IdempotencyResponseSnapshot(
            StatusCode: headerSnapshot.StatusCode,
            Headers: headerSnapshot.Headers,
            Body: bytes,
            Fingerprint: fingerprint);

        using var cts = new CancellationTokenSource(FinalizationTimeout);
        try
        {
            await store.CompleteAsync(scope, key, reservationId, snapshot, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogCompleteTimedOut(_logger, GetKeyHash());
            await SafeAbandonAsync(store, _logger, scope, key, reservationId, GetKeyHash()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCompleteFailed(_logger, ex, GetKeyHash());
            await SafeAbandonAsync(store, _logger, scope, key, reservationId, GetKeyHash()).ConfigureAwait(false);
        }
    }

    private static async Task SafeAbandonAsync(IIdempotencyStore store, ILogger<IdempotencyMiddleware> logger, string scope, string key, string reservationId, string keyHash)
    {
        using var cts = new CancellationTokenSource(FinalizationTimeout);
        try
        {
            await store.AbandonAsync(scope, key, reservationId, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAbandonFailed(logger, ex, keyHash);
        }
    }

    // 12 hex chars = 48 bits of entropy. With 1M distinct keys/day, the birthday-paradox
    // collision probability is ~0.18% per day — low enough that hash collisions in operator
    // logs are vanishingly rare. 8 chars (32 bits) was too short (~100% collision/day at that
    // volume). The raw key is still used at the IIdempotencyStore layer; this redaction only
    // affects log templates.
    private static string RedactKeyForLogs(string key) =>
        string.IsNullOrEmpty(key)
            ? "<empty>"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        409 => "Conflict",
        413 => "Payload Too Large",
        422 => "Unprocessable Entity",
        _ => "Error",
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Idempotency capture skipped: no IHttpResponseBodyFeature on request {Path}")]
    static partial void LogCaptureSkippedNoFeature(ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Idempotency snapshot not persisted (response capture aborted: response body too large, SendFileAsync used, or explicit abort) for key hash {KeyHash}")]
    static partial void LogCaptureAbandoned(ILogger logger, string keyHash);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Idempotency store CompleteAsync timed out for key hash {KeyHash}")]
    static partial void LogCompleteTimedOut(ILogger logger, string keyHash);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Idempotency store CompleteAsync failed for key hash {KeyHash}")]
    static partial void LogCompleteFailed(ILogger logger, Exception ex, string keyHash);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Idempotency store AbandonAsync failed for key hash {KeyHash}")]
    static partial void LogAbandonFailed(ILogger logger, Exception ex, string keyHash);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Idempotency reservation abandoned for key hash {KeyHash}: response wrote trailers, which cannot be replayed")]
    static partial void LogTrailersAbandoned(ILogger logger, string keyHash);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Idempotency reservation abandoned for key hash {KeyHash}: handler returned status {StatusCode} (5xx responses are treated as transient)")]
    static partial void LogServerErrorAbandoned(ILogger logger, string keyHash, int statusCode);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Idempotency reservation abandoned for key hash {KeyHash}: flushing the captured response body writer failed")]
    static partial void LogCaptureFlushFailed(ILogger logger, Exception ex, string keyHash);

    private sealed class HeaderSnapshot
    {
        public int StatusCode { get; set; } = 200;

        public Dictionary<string, string[]> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Captured { get; set; }
    }
}