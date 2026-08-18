namespace Trellis.Asp.Idempotency;

using System;
using System.Collections.Generic;
using System.Net.Http;

/// <summary>
/// Configuration for <c>IdempotencyMiddleware</c>: header name, TTL, size limits, opted-in
/// HTTP methods, and the set of request headers that contribute to the request fingerprint in
/// addition to the body.
/// </summary>
/// <remarks>
/// <para>
/// All defaults match the published Trellis contract; changing them is a breaking change for
/// consumers. Override individual values via <c>AddTrellisIdempotency(o =&gt; ...)</c>.
/// </para>
/// </remarks>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// Name of the request header carrying the client-supplied idempotency key. Defaults to
    /// <c>"Idempotency-Key"</c> per IETF draft <c>draft-ietf-httpapi-idempotency-key-header</c>.
    /// </summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>
    /// Name of the response header added to replayed responses so clients can detect that the
    /// body originated from a cached snapshot rather than a fresh handler invocation. Defaults
    /// to <c>"Idempotent-Replayed"</c>.
    /// </summary>
    public string ReplayHeaderName { get; set; } = "Idempotent-Replayed";

    /// <summary>
    /// How long a completed snapshot stays available for replay. After the TTL elapses the
    /// stored entry is treated as not present, so a retry with the same key is processed as a
    /// fresh request. Defaults to 24 hours.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum time a single reservation may remain open before another request with the same
    /// key is allowed to take over. Bounds the window during which a hung or crashed handler
    /// could prevent a retry from making progress. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ReservationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum length, in characters, of the idempotency key once unwrapped from any RFC 8941
    /// sf-string quoting. Requests with a key longer than this fail with a 400 ProblemDetails
    /// before the handler runs. Defaults to 200, matching common provider conventions.
    /// </summary>
    public int MaxKeyLength { get; set; } = 200;

    /// <summary>
    /// Maximum request body size, in bytes, that the middleware will buffer in order to compute
    /// a fingerprint. Larger bodies fail with 413 Payload Too Large before the handler runs.
    /// Defaults to 1 MiB.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 1L * 1024 * 1024;

    /// <summary>
    /// Maximum response body size, in bytes, that the middleware will capture for replay. On
    /// overflow the original response is still emitted to the client, but the snapshot is
    /// abandoned and a subsequent retry will re-execute the handler. Defaults to 1 MiB.
    /// </summary>
    public long MaxResponseBodyBytes { get; set; } = 1L * 1024 * 1024;

    /// <summary>
    /// HTTP status code to return when the same key is reused with a different request body.
    /// Defaults to 422 (Unprocessable Entity), matching the convention used by Stripe and Adyen.
    /// </summary>
    public int MismatchStatusCode { get; set; } = 422;

    /// <summary>
    /// When <c>true</c>, opted-in endpoints reject requests that omit the idempotency header
    /// with a 400 ProblemDetails. When <c>false</c>, missing-key requests pass through to the
    /// handler with no idempotency processing. Defaults to <c>true</c>.
    /// </summary>
    public bool RequireKeyOnOptedInEndpoints { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, <c>Set-Cookie</c> response headers are included verbatim in captured
    /// snapshots. When <c>false</c> (default) they are stripped from snapshots so a replay does
    /// not re-issue session or authentication cookies that have since been rotated.
    /// </summary>
    public bool IncludeSetCookieInSnapshot { get; set; }

    /// <summary>
    /// HTTP methods on which an <c>[Idempotent]</c>-marked endpoint is treated as idempotent.
    /// Defaults to <c>POST</c> and <c>PATCH</c>. PUT and DELETE are already idempotent per
    /// RFC 9110 and are not added by default.
    /// </summary>
    public ICollection<string> Methods { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethod.Post.Method,
        HttpMethod.Patch.Method,
    };

    /// <summary>
    /// Names of additional request headers that contribute to the request fingerprint beyond
    /// method, path, canonical query string, body bytes, <c>Content-Type</c>, and
    /// <c>Content-Encoding</c>. Add headers here when their value materially changes the
    /// response representation (for example <c>Accept</c>, <c>Accept-Language</c>, <c>Prefer</c>).
    /// </summary>
    /// <remarks>
    /// Header values are normalised before hashing: leading and trailing whitespace are
    /// trimmed, and multiple values are concatenated with U+001F between entries.
    /// </remarks>
    public ICollection<string> AdditionalFingerprintHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}