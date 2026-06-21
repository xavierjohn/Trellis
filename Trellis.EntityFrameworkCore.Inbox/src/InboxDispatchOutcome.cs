namespace Trellis.EntityFrameworkCore;

/// <summary>
/// The outcome of dispatching one message through <see cref="IInboxDispatcher.DispatchAsync"/>: whether its
/// handlers ran in this call, or it was recognized as an already-processed redelivery and skipped. Both
/// outcomes mean the message is durably accounted for, so a pull consumer can safely advance its checkpoint
/// on either; the distinction exists for metrics, logging, and overlap / anti-join bookkeeping.
/// </summary>
public enum InboxDispatchOutcome
{
    /// <summary>
    /// The message was new: its handlers ran and their side effects committed atomically with the dedup
    /// record in this call.
    /// </summary>
    Processed,

    /// <summary>
    /// The <c>(ConsumerId, MessageId)</c> pair had already been recorded, so this call invoked no handlers
    /// and changed nothing — a safe, expected no-op for a redelivery.
    /// </summary>
    SkippedDuplicate,
}
