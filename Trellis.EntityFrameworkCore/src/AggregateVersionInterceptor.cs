namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Interceptor that automatically increments <see cref="IAggregate.Version"/>
/// on modified aggregate entities before <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> executes.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor works with <see cref="AggregateVersionConvention"/> to provide
/// automatic optimistic concurrency control:
/// <list type="bullet">
/// <item><see cref="AggregateVersionConvention"/> marks <c>Version</c> as a concurrency token</item>
/// <item>This interceptor increments <c>Version</c> on modified aggregates before save</item>
/// <item>EF Core generates <c>WHERE Version = @original</c> in the SQL</item>
/// <item>Concurrent modifications cause <see cref="DbUpdateConcurrencyException"/></item>
/// </list>
/// </para>
/// <para>
/// Registered automatically by <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors"/>.
/// </para>
/// </remarks>
internal sealed class AggregateVersionInterceptor : SaveChangesInterceptor
{
    private const string VersionPropertyName = nameof(IAggregate.Version);

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        IncrementVersions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        IncrementVersions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SyncVersionOriginalValues(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        SyncVersionOriginalValues(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private static void IncrementVersions(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
                continue;

            if (entry.Entity is not IAggregate)
                continue;

            var versionProperty = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == VersionPropertyName);
            if (versionProperty is null)
                continue;

            var currentVersion = (long)versionProperty.CurrentValue!;
            var originalVersion = (long)versionProperty.OriginalValue!;

            // Only increment once per save cycle. When acceptAllChangesOnSuccess is false,
            // the entry stays Modified across multiple saves. Without this guard, each save
            // would increment again, creating a mismatch with the database value.
            if (currentVersion == originalVersion)
                versionProperty.CurrentValue = currentVersion + 1;
        }
    }

    /// <summary>
    /// After a successful save, sync the Version property's OriginalValue to CurrentValue
    /// on aggregate entries that are still Modified (i.e., acceptAllChangesOnSuccess was false).
    /// This ensures subsequent saves generate the correct WHERE clause.
    /// </summary>
    private static void SyncVersionOriginalValues(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
                continue;

            if (entry.Entity is not IAggregate)
                continue;

            var versionProperty = entry.Properties
                .FirstOrDefault(p => p.Metadata.Name == VersionPropertyName);
            if (versionProperty is null)
                continue;

            // Entry is still Modified after save → acceptAllChangesOnSuccess was false.
            // Sync OriginalValue so the next save's WHERE clause matches the DB value.
            versionProperty.OriginalValue = versionProperty.CurrentValue;
        }
    }
}
