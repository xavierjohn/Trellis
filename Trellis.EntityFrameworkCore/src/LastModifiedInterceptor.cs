namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Trellis;

/// <summary>
/// EF Core SaveChanges interceptor that automatically sets <see cref="ITrackLastModified.LastModified"/>
/// on Added and Modified entities, using the provided <see cref="TimeProvider"/>.
/// </summary>
public sealed class LastModifiedInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="LastModifiedInterceptor"/> class.
    /// </summary>
    /// <param name="timeProvider">
    /// The time provider to use for timestamps. Defaults to <see cref="TimeProvider.System"/> if <c>null</c>.
    /// </param>
    public LastModifiedInterceptor(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        SetLastModified(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        SetLastModified(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SetLastModified(DbContext? context)
    {
        if (context is null) return;
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITrackLastModified tracked &&
                (entry.State == EntityState.Added || entry.State == EntityState.Modified))
            {
                tracked.LastModified = now;
            }
        }
    }
}
