namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Supplies authoritative, gap-aware source/consumer lag evidence to outbox readiness.
/// </summary>
/// <remarks>
/// Implementations are application or transport adapters. They must account for out-of-order
/// processing, redeliveries, retained deduplication evidence, and every required source partition.
/// Reading evidence must not dispatch, settle, checkpoint, prune, replay, or otherwise mutate work.
/// </remarks>
public interface IConsumerLagProvider
{
    /// <summary>Reads one conservative lag snapshot within the caller's probe budget.</summary>
    /// <param name="cancellationToken">The shared readiness probe deadline.</param>
    /// <returns>Authoritative evidence with its original source sample time.</returns>
    ValueTask<ConsumerLagEvidence> GetLagAsync(CancellationToken cancellationToken);
}