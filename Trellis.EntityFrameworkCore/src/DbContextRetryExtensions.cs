namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Retry-on-collision save extensions for <see cref="DbContext"/>. Adds
/// <see cref="SaveChangesWithRetryAsync(DbContext, Func{DbUpdateException, bool}, Func{IReadOnlyList{EntityEntry}, int, CancellationToken, ValueTask{bool}}, int, CancellationToken)"/>
/// for the system-generated unique-key INSERT-and-regenerate pattern.
/// </summary>
public static class DbContextRetryExtensions
{
    /// <summary>
    /// Saves changes. On <see cref="DbUpdateException"/> classified as retryable by
    /// <paramref name="shouldRetry"/>, detaches only the entities reported by
    /// <see cref="DbUpdateException.Entries"/> (NEVER the full change tracker), invokes
    /// <paramref name="regenerate"/> so the caller can mutate the conflicting entities'
    /// natural keys in place, re-Adds them, and retries up to <paramref name="maxAttempts"/>
    /// total attempts. Designed for the INSERT-with-system-generated-unique-key pattern
    /// (e.g. short codes, slugs, tokens).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Concurrency exceptions bypass retry.</b> <see cref="DbUpdateConcurrencyException"/>
    /// is mapped to <see cref="Error.Conflict"/> with reason code
    /// <c>concurrent-modification</c> WITHOUT calling <paramref name="shouldRetry"/> —
    /// regenerating a natural key cannot resolve a stale rowversion conflict.
    /// </para>
    /// <para>
    /// <b>Only <see cref="EntityState.Added"/> entries are retryable.</b> If
    /// <see cref="DbUpdateException.Entries"/> contains any entry that is not in the
    /// <see cref="EntityState.Added"/> state when retry would otherwise begin, this method
    /// throws <see cref="InvalidOperationException"/>. The helper does not preserve original
    /// values, per-property <see cref="PropertyEntry.IsModified"/> flags, or temporary-value
    /// metadata across the detach/re-attach cycle that <see cref="EntityState.Modified"/>
    /// would require.
    /// </para>
    /// <para>
    /// <b>Detach scope.</b> Only entries reported by <see cref="DbUpdateException.Entries"/>
    /// are detached. Sibling aggregates pending in the change tracker (an outbox row staged
    /// by a domain-event handler; an unrelated <see cref="EntityState.Added"/> entity from
    /// the same logical operation; an entity promoted via <c>db.Entry(x).State = Added</c>
    /// from outside <see cref="DbSet{TEntity}.Add(TEntity)"/>) are unaffected.
    /// </para>
    /// <para>
    /// <b>Detach is conditional on attempting a retry.</b> If
    /// <paramref name="shouldRetry"/> returns <see langword="false"/>, OR if the failure
    /// occurs on the final allowed attempt (<paramref name="maxAttempts"/> exhausted), no
    /// detach is performed and the change tracker is left untouched. Exhaustion returns
    /// <see cref="Error.Conflict"/> with reason code <c>retry.exhausted</c>. If
    /// <paramref name="regenerate"/> returns <see langword="false"/>, the conflicting
    /// entries remain detached and the method returns <see cref="Error.Conflict"/> with
    /// reason code <c>retry.aborted</c>. These reason codes are distinct from the
    /// caller-classifier-related <c>duplicate.key</c> / <c>referential.integrity</c>
    /// codes (which are only returned when <paramref name="shouldRetry"/> returns
    /// <see langword="false"/> for a recognised non-retryable <see cref="DbUpdateException"/>),
    /// so a broader <paramref name="shouldRetry"/> classifier (e.g. one accepting a
    /// provider-specific transient error) does not surface a misleading
    /// <c>duplicate.key</c> reason code on exhaust/abort.
    /// </para>
    /// <para>
    /// <b>Failure mapping for non-retryable <see cref="DbUpdateException"/>.</b> Matches
    /// <see cref="DbContextExtensions.SaveChangesResultAsync(DbContext, CancellationToken)"/>:
    /// foreign-key violations map to reason code <c>referential.integrity</c>; duplicate-key
    /// violations map to <c>duplicate.key</c>; unrecognized <see cref="DbUpdateException"/>
    /// shapes (connection failures wrapped as DbUpdateException, trigger failures, etc.)
    /// are <b>rethrown</b> rather than silently mapped to a conflict.
    /// </para>
    /// <para>
    /// <b>Callback exceptions propagate.</b> An exception thrown by
    /// <paramref name="shouldRetry"/> propagates out without any detach having occurred.
    /// An exception thrown by <paramref name="regenerate"/> propagates out with the
    /// conflicting entries already detached.
    /// </para>
    /// <para>
    /// <b>Graph and owned types.</b> Only the entries in
    /// <see cref="DbUpdateException.Entries"/> are detached and re-added. Dependent or owned
    /// entities must be reachable from a re-added aggregate root by EF's normal
    /// state-cascade rules, or be tracked by the caller before the next attempt. Test
    /// against the target provider when in doubt.
    /// </para>
    /// <para>
    /// <b>Explicit transactions.</b> Provider behavior after a constraint violation inside
    /// an open transaction varies. SQLite accepts further commands on the same transaction
    /// after a unique-constraint failure; SQL Server may abort the transaction depending on
    /// the error and isolation level. Test against the target provider before relying on
    /// retry inside <c>TransactionalCommandBehavior</c> or a hand-rolled transaction scope.
    /// </para>
    /// </remarks>
    /// <param name="db">The <see cref="DbContext"/> to save changes on.</param>
    /// <param name="shouldRetry">Classifies a <see cref="DbUpdateException"/>. Return
    /// <see langword="true"/> to detach-regenerate-retry; return <see langword="false"/>
    /// to map to <see cref="Error.Conflict"/> and return immediately. Concurrency
    /// exceptions bypass this classifier.</param>
    /// <param name="regenerate">Invoked with the entries that were detached and the
    /// 1-based attempt number (where <c>attempt</c>=1 is the first retry preparation
    /// after the initial save failure, <c>attempt</c>=2 is the second, etc.). Return
    /// <see langword="true"/> to continue with the next save attempt; return
    /// <see langword="false"/> to abort retry, leave entries detached, and return
    /// <see cref="Error.Conflict"/>.</param>
    /// <param name="maxAttempts">Maximum total attempts including the initial save.
    /// Must be at least <c>1</c>. Defaults to <c>3</c> (initial + 2 retries).</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to
    /// complete.</param>
    /// <returns>A <see cref="Result{TValue}"/> with <see cref="Unit"/> representing
    /// success, or an <see cref="Error.Conflict"/> failure on classified collision.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/>,
    /// <paramref name="shouldRetry"/>, or <paramref name="regenerate"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    /// <paramref name="maxAttempts"/> is less than <c>1</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when retry would proceed but
    /// <see cref="DbUpdateException.Entries"/> contains an entry not in the
    /// <see cref="EntityState.Added"/> state.</exception>
    public static async Task<Result<Unit>> SaveChangesWithRetryAsync(
        this DbContext db,
        Func<DbUpdateException, bool> shouldRetry,
        Func<IReadOnlyList<EntityEntry>, int, CancellationToken, ValueTask<bool>> regenerate,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(shouldRetry);
        ArgumentNullException.ThrowIfNull(regenerate);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Ok(Unit.Value);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                return Result.Fail<Unit>(new Error.Conflict(
                    Resource: null,
                    ReasonCode: FaultCodes.ConcurrentModification)
                {
                    Detail = $"One or more entities were modified by another process. {ex.Entries.Count} entities affected."
                });
            }
            catch (DbUpdateException ex)
            {
                if (!shouldRetry(ex))
                {
                    if (DbExceptionClassifier.IsDuplicateKey(ex))
                    {
                        var (constraintName, constraintTable) = DbExceptionClassifier.ExtractConstraintIdentity(ex);
                        return Result.Fail<Unit>(new Error.Conflict(Resource: null, ReasonCode: "duplicate.key")
                        {
                            Detail = "A record with the same unique value already exists.",
                            ConstraintName = constraintName,
                            ConstraintTableName = constraintTable,
                        });
                    }

                    if (DbExceptionClassifier.IsForeignKeyViolation(ex))
                    {
                        var (constraintName, constraintTable) = DbExceptionClassifier.ExtractConstraintIdentity(ex);
                        return Result.Fail<Unit>(new Error.Conflict(Resource: null, ReasonCode: "referential.integrity")
                        {
                            Detail = "Operation violates a referential integrity constraint.",
                            ConstraintName = constraintName,
                            ConstraintTableName = constraintTable,
                        });
                    }

                    throw;
                }

                if (attempt >= maxAttempts)
                    return Result.Fail<Unit>(new Error.Conflict(Resource: null, ReasonCode: "retry.exhausted")
                    { Detail = $"Maximum retry attempts ({maxAttempts}) exhausted; the caller-supplied classifier kept reporting the save failure as retryable." });

                ValidateAllEntriesAreAdded(ex.Entries);

                var snapshot = new (object Entity, EntityEntry Entry)[ex.Entries.Count];
                for (var i = 0; i < ex.Entries.Count; i++)
                    snapshot[i] = (ex.Entries[i].Entity, ex.Entries[i]);

                foreach (var s in snapshot)
                    s.Entry.State = EntityState.Detached;

                var entriesForCallback = new EntityEntry[snapshot.Length];
                for (var i = 0; i < snapshot.Length; i++)
                    entriesForCallback[i] = snapshot[i].Entry;

                var keepGoing = await regenerate(entriesForCallback, attempt, cancellationToken).ConfigureAwait(false);
                if (!keepGoing)
                    return Result.Fail<Unit>(new Error.Conflict(Resource: null, ReasonCode: "retry.aborted")
                    { Detail = $"Retry aborted by regenerate callback after attempt {attempt}." });

                foreach (var s in snapshot)
                    db.Entry(s.Entity).State = EntityState.Added;
            }
        }
    }

    private static void ValidateAllEntriesAreAdded(IReadOnlyList<EntityEntry> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.State == EntityState.Added)
                continue;

            throw new InvalidOperationException(
                $"SaveChangesWithRetryAsync only supports retrying entries in the Added state. " +
                $"Entry for '{entry.Entity.GetType().FullName ?? entry.Entity.GetType().Name}' is " +
                $"'{entry.State}'. Regenerate the natural key before calling SaveChanges, or use a " +
                $"different retry primitive for modified-entity collisions.");
        }
    }
}