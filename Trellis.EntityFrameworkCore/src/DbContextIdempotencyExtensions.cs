namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Idempotency helpers for <see cref="DbContext"/> that convert provider-level
/// unique-constraint violations into <see cref="Result{T}"/> failures with a
/// structured <see cref="Error.Conflict"/> payload, so callers can express
/// "insert this row unless it already exists" without having to write
/// provider-specific exception handling at each call site.
/// </summary>
public static class DbContextIdempotencyExtensions
{
    /// <summary>
    /// Adds <paramref name="entity"/> to <paramref name="context"/>, persists the change
    /// via <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>, and converts a
    /// duplicate-key violation into <c>Result.Fail(new Error.Conflict(null, "duplicate.key"))</c>.
    /// Use this helper to implement idempotent inserts on a unique constraint without
    /// catching <see cref="DbUpdateException"/> at the call site.
    /// </summary>
    /// <typeparam name="TEntity">The reference-type entity to insert.</typeparam>
    /// <param name="context">The <see cref="DbContext"/> to add the entity to.</param>
    /// <param name="entity">The entity instance to insert.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the save.</param>
    /// <returns>
    /// <para>
    /// On success: <see cref="Result.Ok{TValue}(TValue)"/> wrapping the same
    /// <paramref name="entity"/> instance that was passed in (EF Core populates
    /// generated values such as primary keys, row versions, and timestamps in-place
    /// on that instance).
    /// </para>
    /// <para>
    /// On a unique-constraint violation: <see cref="Result.Fail{TValue}(Error)"/>
    /// containing an <see cref="Error.Conflict"/> with
    /// <see cref="Error.Code"/> = <c>"duplicate.key"</c> and the
    /// provider-reported <see cref="Error.Conflict.ConstraintName"/> and
    /// <see cref="Error.Conflict.ConstraintTableName"/> populated on a best-effort
    /// basis (see <see cref="DbExceptionClassifier.ExtractConstraintIdentity(DbUpdateException)"/>).
    /// On this path the change tracker is left exactly as the caller saw it on entry:
    /// every entry this call newly attached as <see cref="EntityState.Added"/> (the
    /// root plus any owned / dependent / join entries reached through the navigation
    /// graph) is detached, and any already-tracked entry that
    /// <see cref="DbContext.Add(object)"/> flipped to <see cref="EntityState.Added"/>
    /// (e.g., an entity the context loaded earlier) is restored to its prior state.
    /// </para>
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Exception propagation.</b> Only <see cref="DbUpdateException"/> with a
    /// duplicate-key inner exception is converted. All other exceptions —
    /// <see cref="DbUpdateConcurrencyException"/>, foreign-key violations, other
    /// <see cref="DbUpdateException"/> variants, connection/timeout exceptions, and
    /// <see cref="OperationCanceledException"/> — propagate to the caller so that
    /// global handlers and retry policies see them. When
    /// <see cref="OperationCanceledException"/> is raised from
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> (the
    /// <paramref name="cancellationToken"/> tripped mid-save) the helper still
    /// performs the same change-tracker cleanup it performs on the duplicate-key
    /// path before rethrowing, so a cancelled call leaves the change tracker
    /// exactly as the caller saw it on entry — a later
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> on the same
    /// DI-scoped context will not silently insert the cancelled entity, and the
    /// clean-context guard will not block a retry on the same context.
    /// </para>
    /// <para>
    /// <b>Clean-context requirement.</b> The helper requires the <see cref="DbContext"/>
    /// to have no pending changes before the call (<c>ChangeTracker.HasChanges()</c>
    /// must be <see langword="false"/>). If the context already has tracked changes,
    /// the helper throws <see cref="InvalidOperationException"/> because a duplicate-key
    /// violation surfaced by <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
    /// cannot be unambiguously attributed to the entity being inserted — it could
    /// belong to one of the pre-existing pending changes. Call
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> first, use a fresh
    /// context, or use the lower-level
    /// <see cref="DbContextExtensions.SaveChangesResultAsync(DbContext, CancellationToken)"/>
    /// helper if you intentionally want to flush mixed changes.
    /// </para>
    /// <para>
    /// <b>Telemetry safety.</b> The <c>Error.Conflict.Detail</c> string is a
    /// safe generic message suitable for API responses. The constraint identity
    /// fields are marked <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/>
    /// on <see cref="Error.Conflict"/> and are intended for structured logging only.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="entity"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="context"/> already has pending changes
    /// (<see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker.HasChanges"/>
    /// is <see langword="true"/>) before the call.
    /// </exception>
    public static async Task<Result<TEntity>> TryInsertUniqueAsync<TEntity>(
        this DbContext context,
        TEntity entity,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                $"{nameof(TryInsertUniqueAsync)} requires a DbContext with no pending changes so " +
                "that a duplicate-key violation can be unambiguously attributed to the entity being " +
                "inserted. Call SaveChangesAsync first, use a fresh DbContext, or use " +
                $"{nameof(DbContextExtensions)}.{nameof(DbContextExtensions.SaveChangesResultAsync)} " +
                "if you intentionally want to flush mixed changes.");
        }

        // Observe pre-cancellation BEFORE mutating the change tracker so a cancelled call
        // does not leave the entity (or its owned/dependent graph) attached as Added.
        cancellationToken.ThrowIfCancellationRequested();

        // Snapshot the prior state of every entry currently in the tracker so we can
        // restore — not just detach — entries whose state transitions to Added as a
        // side effect of context.Add(entity). context.Add(entity) walks the navigation
        // graph and may both (a) attach new owned / dependent / join entities as Added
        // and (b) flip already-tracked Unchanged entities to Added. Leaving any of them
        // tracked after a duplicate failure would (i) trip the HasChanges() guard on a
        // retry call and (ii) cause the next SaveChangesAsync to insert (or re-insert)
        // them.
        //
        // HasChanges() above is true only when at least one tracked entry has a non-
        // Unchanged state; given the guard, every entry currently in the tracker is
        // Unchanged. Capturing the actual prior state keeps the cleanup correct if a
        // future change to the guard ever admits other non-Modified states.
        var priorStates = new Dictionary<object, EntityState>(ReferenceEqualityComparer.Instance);
        foreach (var existingEntry in context.ChangeTracker.Entries())
            priorStates[existingEntry.Entity] = existingEntry.State;

        context.Add(entity);

        var introducedAdded = new List<EntityEntry>();
        var transitionedToAdded = new List<(EntityEntry Entry, EntityState PriorState)>();
        foreach (var trackedEntry in context.ChangeTracker.Entries())
        {
            if (trackedEntry.State != EntityState.Added)
                continue;

            if (priorStates.TryGetValue(trackedEntry.Entity, out var priorState))
                transitionedToAdded.Add((trackedEntry, priorState));
            else
                introducedAdded.Add(trackedEntry);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Ok(entity);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsDuplicateKey(ex))
        {
            RestoreChangeTracker(introducedAdded, transitionedToAdded);

            var (constraintName, tableName) = DbExceptionClassifier.ExtractConstraintIdentity(ex);
            return Result.Fail<TEntity>(new Error.Conflict(
                Resource: null,
                Code: "duplicate.key")
            {
                Detail = "A record with the same unique value already exists.",
                ConstraintName = constraintName,
                ConstraintTableName = tableName,
            });
        }
        catch (OperationCanceledException)
        {
            // Cancellation raised by SaveChangesAsync (token cancelled mid-save) must
            // not leave the entries this call attached as Added: a later
            // SaveChangesAsync on the same DI-scoped DbContext would otherwise silently
            // insert the cancelled entity, and the clean-context guard would block any
            // retry on the same context because HasChanges() would now be true.
            RestoreChangeTracker(introducedAdded, transitionedToAdded);
            throw;
        }

        static void RestoreChangeTracker(
            List<EntityEntry> introducedAdded,
            List<(EntityEntry Entry, EntityState PriorState)> transitionedToAdded)
        {
            // Detach every entry this call newly attached (root + owned/dependent
            // graph) so a caller retrying with a freshly-built instance does not
            // re-flush the original on the next SaveChangesAsync.
            foreach (var added in introducedAdded)
            {
                if (added.State == EntityState.Added)
                    added.State = EntityState.Detached;
            }

            // Restore — do not detach — entries that were already tracked before this
            // call and were flipped to Added by context.Add. Detaching would lose
            // tracking of a row the caller did not stage for removal; leaving them
            // Added would cause silent re-insert attempts on the next SaveChangesAsync.
            foreach (var (entry, priorState) in transitionedToAdded)
            {
                if (entry.State == EntityState.Added)
                    entry.State = priorState;
            }
        }
    }
}