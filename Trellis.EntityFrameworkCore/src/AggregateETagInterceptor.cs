namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor that automatically generates a new <see cref="IAggregate.ETag"/> value
/// on new and modified aggregate entities before <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> executes.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor works with <see cref="AggregateETagConvention"/> to provide
/// automatic optimistic concurrency control per RFC 9110:
/// <list type="bullet">
/// <item><see cref="AggregateETagConvention"/> marks <c>ETag</c> as a concurrency token</item>
/// <item>This interceptor generates a new GUID-based ETag on added and modified aggregates before save</item>
/// <item>EF Core generates <c>WHERE ETag = @original</c> in the SQL</item>
/// <item>Concurrent modifications cause <see cref="DbUpdateConcurrencyException"/></item>
/// </list>
/// </para>
/// <para>
/// Registered automatically by <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors(DbContextOptionsBuilder)"/>.
/// </para>
/// </remarks>
internal sealed class AggregateETagInterceptor : SaveChangesInterceptor
{
    private const string ETagPropertyName = nameof(IAggregate.ETag);

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        GenerateETags(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        GenerateETags(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SyncETagOriginalValues(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        SyncETagOriginalValues(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private static void GenerateETags(DbContext? context)
    {
        if (context is null)
            return;

        PromoteAggregatesWithModifiedDependents(context);

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (entry.Entity is not IAggregate)
                continue;

            var etagProperty = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == ETagPropertyName);
            if (etagProperty is null)
                continue;

            var currentETag = etagProperty.CurrentValue as string;
            var originalETag = etagProperty.OriginalValue as string;

            // For Added entries: always generate an ETag.
            // For Modified entries: only generate if not already changed in this save cycle
            // (guards against double-generation with acceptAllChangesOnSuccess: false).
            if (entry.State == EntityState.Added || currentETag == originalETag)
                etagProperty.CurrentValue = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Scans Unchanged aggregate roots for loaded navigations with Modified, Added, or Deleted
    /// child entries. When found, marks the aggregate root's ETag property as modified so that
    /// <see cref="GenerateETags"/> will generate a new value and EF Core will include it in the
    /// UPDATE statement's WHERE clause.
    /// </summary>
    private static void PromoteAggregatesWithModifiedDependents(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Unchanged)
                continue;

            if (entry.Entity is not IAggregate)
                continue;

            if (!ChangeTrackerHelper.HasModifiedDependents(entry))
                continue;

            var etagProperty = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == ETagPropertyName);
            if (etagProperty is not null)
                etagProperty.IsModified = true;
        }
    }

    /// <summary>
    /// After a successful save, sync the ETag property's OriginalValue to CurrentValue
    /// on aggregate entries that are still Modified (i.e., acceptAllChangesOnSuccess was false).
    /// This ensures subsequent saves generate the correct WHERE clause.
    /// </summary>
    private static void SyncETagOriginalValues(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
                continue;

            if (entry.Entity is not IAggregate)
                continue;

            var etagProperty = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == ETagPropertyName);
            if (etagProperty is null)
                continue;

            etagProperty.OriginalValue = etagProperty.CurrentValue;
        }
    }
}
