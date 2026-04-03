namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Trellis;

/// <summary>
/// EF Core SaveChanges interceptor that automatically sets <see cref="IEntity.CreatedAt"/>
/// and <see cref="IEntity.LastModified"/> on entities before save.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEntity.CreatedAt"/> is set once when the entity is first added (<c>EntityState.Added</c>)
/// and only if it has not already been set (preserves caller-supplied historical values for data migration).
/// <see cref="IEntity.LastModified"/> is set on every save (<c>EntityState.Added</c> or <c>EntityState.Modified</c>).
/// </para>
/// <para>
/// For aggregate roots, <see cref="IEntity.LastModified"/> is also updated when changes are
/// detected in loaded child entities (tracked navigation properties) that are added, modified,
/// or deleted — mirroring the promotion logic of <see cref="AggregateETagInterceptor"/>.
/// </para>
/// <para>
/// Registered automatically by
/// <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors(DbContextOptionsBuilder)"/>.
/// </para>
/// </remarks>
public sealed class EntityTimestampInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityTimestampInterceptor"/> class.
    /// </summary>
    /// <param name="timeProvider">
    /// The time provider to use for timestamps. Defaults to <see cref="TimeProvider.System"/> if <c>null</c>.
    /// </param>
    public EntityTimestampInterceptor(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Sets timestamps on tracked entities before saving.
    /// </summary>
    /// <param name="eventData">The event data associated with the current save operation.</param>
    /// <param name="result">The current interception result.</param>
    /// <returns>The interception result.</returns>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        SetTimestamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>
    /// Sets timestamps on tracked entities before saving asynchronously.
    /// </summary>
    /// <param name="eventData">The event data associated with the current save operation.</param>
    /// <param name="result">The current interception result.</param>
    /// <param name="cancellationToken">A token used to cancel the asynchronous save operation.</param>
    /// <returns>The result of the asynchronous interception operation.</returns>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        SetTimestamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetTimestamps(DbContext? context)
    {
        if (context is null) return;

        var now = _timeProvider.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IEntity entity)
                continue;

            if (entry.State == EntityState.Added)
            {
                if (entity.CreatedAt == default)
                    entity.CreatedAt = now;
                entity.LastModified = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entity.LastModified = now;
            }
            else if (entry.State == EntityState.Unchanged
                     && entry.Entity is IAggregate
                     && ChangeTrackerHelper.HasModifiedDependents(entry))
            {
                entity.LastModified = now;
                var prop = entry.Properties
                    .FirstOrDefault(p => p.Metadata.Name == nameof(IEntity.LastModified));
                if (prop is not null)
                    prop.IsModified = true;
            }
        }
    }
}
