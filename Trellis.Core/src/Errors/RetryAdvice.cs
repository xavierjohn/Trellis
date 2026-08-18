namespace Trellis;

/// <summary>
/// Transport-neutral retry hint attached to <see cref="Error.RateLimited"/> and
/// <see cref="Error.Unavailable"/>. Boundary layers translate to their own wire format
/// (e.g. a header on a synchronous response, an envelope field on a queued message,
/// or a structured field on a CLI exit envelope). Producers in the domain layer
/// describe <em>when</em> the caller may retry; how that is conveyed is the boundary's
/// concern.
/// </summary>
/// <remarks>
/// <para>
/// At most one of <paramref name="After"/> and <paramref name="At"/> is typically populated;
/// producers may set either, neither (an empty hint that still distinguishes "advice given"
/// from "no advice"), or both. When both are present they describe the same instant from
/// different viewpoints — boundaries are free to prefer whichever form maps most naturally
/// to their wire representation, and consumers should treat them as equivalent rather than
/// composing them.
/// </para>
/// </remarks>
/// <param name="After">Relative duration to wait before retrying.</param>
/// <param name="At">Absolute instant when the caller may retry.</param>
public readonly record struct RetryAdvice(TimeSpan? After = null, DateTimeOffset? At = null);