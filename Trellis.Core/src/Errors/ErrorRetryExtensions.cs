namespace Trellis;

/// <summary>
/// Transport-neutral retry-classification helpers over the closed <see cref="Error"/>
/// catalog. Lets workers, message consumers, and outbound-gateway callers ask "should I
/// retry this, give up on this item, or halt the batch?" without hand-rolling parallel
/// <c>switch</c> blocks against framework error types.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> These helpers answer the generic "retry the same operation later"
/// question. Domain-specific retry shapes (optimistic-concurrency reload-and-retry for
/// <see cref="Error.Conflict"/>; INSERT-with-regenerated-natural-key retry for
/// <c>SaveChangesWithRetryAsync</c>) require their own primitives because they have to
/// mutate state before retrying. Consumers writing such loops should switch on the
/// concrete <see cref="Error"/> shape directly, not call <see cref="Classify"/>.
/// </para>
/// <para>
/// <b>Eventually-consistent reads.</b> <see cref="Error.NotFound"/> and
/// <see cref="Error.Gone"/> default to <see cref="RetryClassification.Permanent"/>.
/// Services whose reads are eventually consistent (read-your-own-writes against a
/// replicated store, message consumers ahead of write replication) should override
/// locally rather than expect the framework default to infer their model.
/// </para>
/// <para>
/// <b>Override pattern.</b> Consumers that disagree with the default mapping for one or
/// more cases should branch on the concrete shape first and only fall back to
/// <see cref="Classify"/> for the cases they don't override:
/// <code language="csharp">
/// var classification = error switch
/// {
///     Error.Conflict c when c.ReasonCode == FaultCodes.ConcurrentModification =&gt; RetryClassification.Transient,
///     _ =&gt; error.Classify(),
/// };
/// </code>
/// </para>
/// </remarks>
public static class ErrorRetryExtensions
{
    /// <summary>
    /// Classifies an <see cref="Error"/> using the framework's default transport-neutral
    /// mapping. Use the result to decide whether the surrounding worker / consumer loop
    /// should retry this item, give up on it, or halt the batch entirely.
    /// </summary>
    /// <param name="error">The error to classify.</param>
    /// <returns>The default <see cref="RetryClassification"/> for <paramref name="error"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Default mapping</b> (every nested case of <see cref="Error"/> is enumerated;
    /// the catalog is closed so the table is exhaustive at framework-publish time):
    /// </para>
    /// <list type="table">
    /// <listheader><term>Error case</term><description>Classification</description></listheader>
    /// <item><term><see cref="Error.Unavailable"/></term><description><see cref="RetryClassification.Transient"/></description></item>
    /// <item><term><see cref="Error.RateLimited"/></term><description><see cref="RetryClassification.Transient"/></description></item>
    /// <item><term><see cref="Error.Unexpected"/></term><description><see cref="RetryClassification.Transient"/> — see below</description></item>
    /// <item><term><see cref="Error.TransportFault"/></term><description><see cref="RetryClassification.Permanent"/> — see below</description></item>
    /// <item><term><see cref="Error.AuthenticationRequired"/></term><description><see cref="RetryClassification.FailFast"/></description></item>
    /// <item><term><see cref="Error.Forbidden"/></term><description><see cref="RetryClassification.Permanent"/></description></item>
    /// <item><term><see cref="Error.InvalidInput"/></term><description><see cref="RetryClassification.Permanent"/></description></item>
    /// <item><term><see cref="Error.InvariantViolation"/></term><description><see cref="RetryClassification.Permanent"/></description></item>
    /// <item><term><see cref="Error.NotFound"/></term><description><see cref="RetryClassification.Permanent"/></description></item>
    /// <item><term><see cref="Error.Gone"/></term><description><see cref="RetryClassification.Permanent"/></description></item>
    /// <item><term><see cref="Error.Conflict"/></term><description><see cref="RetryClassification.Permanent"/> — see below</description></item>
    /// <item><term><see cref="Error.Aggregate"/></term><description>Recursive max-severity: <see cref="RetryClassification.FailFast"/> if any inner classifies as <see cref="RetryClassification.FailFast"/>; otherwise <see cref="RetryClassification.Permanent"/> if any inner classifies as <see cref="RetryClassification.Permanent"/>; otherwise <see cref="RetryClassification.Transient"/>.</description></item>
    /// </list>
    /// <para>
    /// <b>Why <see cref="Error.Unexpected"/> is <see cref="RetryClassification.Transient"/>.</b>
    /// <see cref="Error.Unexpected"/> wraps unhandled exceptions and other "this should not
    /// have happened" situations. A retry can hide a deterministic bug (the same code path
    /// will throw again the next time), but it can also recover a transient failure (a
    /// momentary serialization error, a stale connection). The default favours
    /// availability: producers should reserve <see cref="Error.Unexpected"/> for unknown
    /// internal faults and surface deterministic failures as
    /// <see cref="Error.InvariantViolation"/>, <see cref="Error.InvalidInput"/>, or
    /// <see cref="Error.Conflict"/> instead. Consumers must still cap retries.
    /// </para>
    /// <para>
    /// <b>Why <see cref="Error.TransportFault"/> is <see cref="RetryClassification.Permanent"/>.</b>
    /// <see cref="ITransportFault"/> is opaque from <c>Trellis.Core</c>'s perspective; the
    /// concrete payload is defined by transport-specific packages (for example
    /// <c>HttpError</c> in <c>Trellis.Http.Abstractions</c>). The retryable transient
    /// outcomes those transports produce (HTTP 429, HTTP 503, gRPC <c>UNAVAILABLE</c>) are
    /// mapped at the boundary to <see cref="Error.RateLimited"/> and
    /// <see cref="Error.Unavailable"/> — which carry <see cref="RetryAdvice"/> — and never
    /// reach <see cref="Error.TransportFault"/>. Every <c>HttpError</c> case shipped today
    /// (405, 406, 412, 413, 415, 416, 428) is a caller-side error that will not succeed by
    /// waiting and retrying. Transport packages that surface their own retryable transient
    /// faults via <see cref="Error.TransportFault"/> should provide a transport-aware
    /// classification extension that overrides this default for those specific faults.
    /// </para>
    /// <para>
    /// <b>Why <see cref="Error.Conflict"/> is <see cref="RetryClassification.Permanent"/>.</b>
    /// Resolving a 409-shaped conflict typically requires conflict-specific work (reload
    /// the current rowversion, re-apply the change, regenerate a colliding natural key)
    /// that a generic outer retry loop cannot perform. Domains that have a meaningful
    /// retry strategy for a conflict should override locally as shown above, or use a
    /// dedicated primitive like <c>DbContext.SaveChangesWithRetryAsync</c>.
    /// </para>
    /// <para>
    /// <b>Aggregate semantics.</b> The aggregate classification answers "should I retry
    /// the operation that produced this aggregate <em>as an indivisible unit</em>?" A
    /// mixed aggregate of a permanent and a transient failure is classified
    /// <see cref="RetryClassification.Permanent"/> because retrying the unit will not
    /// change the permanent inner's outcome. Callers that need to retry the transient
    /// parts independently must iterate over <see cref="Error.Aggregate.Errors"/> and
    /// classify each inner separately.
    /// </para>
    /// </remarks>
    public static RetryClassification Classify(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error switch
        {
            Error.Unavailable => RetryClassification.Transient,
            Error.RateLimited => RetryClassification.Transient,
            Error.Unexpected => RetryClassification.Transient,
            Error.TransportFault => RetryClassification.Permanent,
            Error.AuthenticationRequired => RetryClassification.FailFast,
            Error.Forbidden => RetryClassification.Permanent,
            Error.InvalidInput => RetryClassification.Permanent,
            Error.InvariantViolation => RetryClassification.Permanent,
            Error.NotFound => RetryClassification.Permanent,
            Error.Gone => RetryClassification.Permanent,
            Error.Conflict => RetryClassification.Permanent,
            Error.Aggregate agg => ClassifyAggregate(agg),
            _ => RetryClassification.Permanent,
        };
    }

    /// <summary>
    /// Convenience predicate equivalent to
    /// <c><see cref="Classify"/>(error) == <see cref="RetryClassification.Transient"/></c>.
    /// </summary>
    /// <param name="error">The error to classify.</param>
    /// <returns><see langword="true"/> if <paramref name="error"/> is classified as <see cref="RetryClassification.Transient"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static bool IsTransient(this Error error) =>
        Classify(error) == RetryClassification.Transient;

    /// <summary>
    /// Convenience predicate equivalent to
    /// <c><see cref="Classify"/>(error) == <see cref="RetryClassification.Permanent"/></c>.
    /// </summary>
    /// <param name="error">The error to classify.</param>
    /// <returns><see langword="true"/> if <paramref name="error"/> is classified as <see cref="RetryClassification.Permanent"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static bool IsPermanent(this Error error) =>
        Classify(error) == RetryClassification.Permanent;

    /// <summary>
    /// Convenience predicate equivalent to
    /// <c><see cref="Classify"/>(error) == <see cref="RetryClassification.FailFast"/></c>.
    /// </summary>
    /// <param name="error">The error to classify.</param>
    /// <returns><see langword="true"/> if <paramref name="error"/> is classified as <see cref="RetryClassification.FailFast"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    public static bool IsFailFast(this Error error) =>
        Classify(error) == RetryClassification.FailFast;

    /// <summary>
    /// Returns the <see cref="RetryAdvice"/> carried by the error, when the producer
    /// supplied one. Currently only <see cref="Error.RateLimited"/> and
    /// <see cref="Error.Unavailable"/> carry advice; all other cases (including
    /// <see cref="Error.Aggregate"/>) return <see langword="null"/>.
    /// </summary>
    /// <param name="error">The error to inspect.</param>
    /// <returns>The producer-supplied <see cref="RetryAdvice"/>, or <see langword="null"/> when none was supplied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Aggregate returns <see langword="null"/> by design.</b> Returning advice from
    /// the first transient inner would (a) contradict <see cref="Classify"/> when the
    /// aggregate classifies as <see cref="RetryClassification.Permanent"/> or
    /// <see cref="RetryClassification.FailFast"/>, and (b) silently under-wait when later
    /// inners suggest a longer delay. Callers that want a per-inner retry policy should
    /// iterate over <see cref="Error.Aggregate.Errors"/> and apply their own merge rule.
    /// </para>
    /// </remarks>
    public static RetryAdvice? GetRetryAdvice(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error switch
        {
            Error.RateLimited rl => rl.Retry,
            Error.Unavailable un => un.Retry,
            _ => null,
        };
    }

    private static RetryClassification ClassifyAggregate(Error.Aggregate aggregate)
    {
        var max = RetryClassification.Transient;
        foreach (var inner in aggregate.Errors)
        {
            var c = Classify(inner);
            if (c > max) max = c;
            if (max == RetryClassification.FailFast) break;
        }

        return max;
    }
}