namespace Trellis;

using System;
using System.Collections.Generic;

/// <summary>
/// Exposes the set of aggregates a unit of work tracked at the most recent successful commit.
/// </summary>
/// <remarks>
/// <para>
/// Implemented alongside a unit-of-work abstraction (e.g.
/// <c>Trellis.EntityFrameworkCore.EfUnitOfWork&lt;TContext&gt;</c>) so a pipeline behavior can
/// auto-dispatch domain events for aggregates the unit of work persisted, without reflecting on
/// the command's response shape.
/// </para>
/// <para>
/// <b>Contract.</b>
/// <list type="bullet">
/// <item>Returns an empty list before any commit has run on this instance.</item>
/// <item>Returns the aggregates tracked at commit time when the most recent commit succeeded.</item>
/// <item>Returns an empty list when the most recent commit failed or threw.</item>
/// <item>Nested-scope commits that defer (do not touch the database) MUST NOT mutate the snapshot.</item>
/// </list>
/// </para>
/// <para>
/// The snapshot is captured by reference: callers that iterate <see cref="CommittedAggregates"/>
/// observe the live aggregate instances, so events raised on them after commit are visible. This
/// is the deliberate enabler for the auto-dispatch pipeline behavior.
/// </para>
/// </remarks>
public interface ITrackedAggregateSource
{
    /// <summary>
    /// Gets the aggregates the unit of work tracked at its most recent successful commit.
    /// Empty before any commit has run, after a failed commit, or after a deferred nested
    /// commit.
    /// </summary>
    IReadOnlyList<IAggregate> CommittedAggregates { get; }
}