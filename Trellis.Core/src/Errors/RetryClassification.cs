namespace Trellis;

/// <summary>
/// Transport-neutral classification of <see cref="Error"/> values for outer retry loops
/// (background workers, message-broker consumers, outbound-gateway callers). Answers the
/// question "given this failure, what should the surrounding job do next?" without
/// committing to any specific transport or retry policy.
/// </summary>
/// <remarks>
/// <para>
/// Three values are defined; the numeric ordering encodes severity so that
/// <see cref="ErrorRetryExtensions.Classify(Error)"/> can use <c>max</c> semantics when
/// classifying an <see cref="Error.Aggregate"/>:
/// <see cref="Transient"/> &lt; <see cref="Permanent"/> &lt; <see cref="FailFast"/>.
/// Consumers should treat the ordering as an implementation detail and prefer pattern
/// matching on the enum members rather than numeric comparisons.
/// </para>
/// </remarks>
public enum RetryClassification
{
    /// <summary>
    /// Retrying the same operation later is likely to succeed. Honour any
    /// <see cref="RetryAdvice"/> returned by
    /// <see cref="ErrorRetryExtensions.GetRetryAdvice(Error)"/> when scheduling the retry;
    /// fall back to the consumer's default backoff policy when no advice is present.
    /// </summary>
    Transient = 0,

    /// <summary>
    /// Retrying the same operation will not change the outcome. The caller should drop
    /// this work item, surface the failure to its observability sink, and move on to the
    /// next item.
    /// </summary>
    Permanent = 1,

    /// <summary>
    /// The failure cannot be recovered in-process and indicates a precondition for the
    /// whole job is no longer met (e.g. credentials revoked). The caller should halt
    /// the surrounding batch / consumer loop rather than process further items.
    /// </summary>
    FailFast = 2,
}