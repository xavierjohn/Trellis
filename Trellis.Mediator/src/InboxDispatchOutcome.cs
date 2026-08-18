namespace Trellis.Mediator;

/// <summary>
/// The outcome of dispatching one message through <see cref="IInboxDispatcher.DispatchAsync"/>: whether this
/// call committed the handlers' side effects, or the message was already processed so this call committed
/// nothing. Both outcomes mean the message is durably accounted for, so a pull consumer can safely advance
/// its checkpoint on either; the distinction exists for metrics, logging, and overlap / anti-join bookkeeping.
/// </summary>
public enum InboxDispatchOutcome
{
    /// <summary>
    /// The message was new: its handlers ran and their side effects committed atomically with the dedup
    /// record in this call.
    /// </summary>
    Processed,

    /// <summary>
    /// The <c>(ConsumerId, MessageId)</c> pair was already processed, so this call committed nothing. Usually
    /// a redelivery caught on the fast path before any handler runs; if instead a concurrent dispatch won the
    /// race, the handlers ran but their writes rolled back with the duplicate-key save. Either way no local
    /// side effects were applied by this call.
    /// </summary>
    SkippedDuplicate,
}