namespace Trellis.EntityFrameworkCore;

using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

/// <summary>
/// Shared helper for detecting modified dependent entities within an aggregate boundary.
/// Used by both <see cref="AggregateETagInterceptor"/> and <see cref="EntityTimestampInterceptor"/>
/// to promote unchanged aggregate roots when their children change.
/// </summary>
internal static class ChangeTrackerHelper
{
    /// <summary>
    /// Returns <c>true</c> if the given entry has any loaded navigation with
    /// Modified, Added, or Deleted child entries.
    /// </summary>
    internal static bool HasModifiedDependents(EntityEntry entry)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (navigation is CollectionEntry collection)
            {
                if (collection.IsModified)
                    return true;

                if (collection.CurrentValue is IEnumerable items)
                {
                    foreach (var item in items)
                    {
                        var childEntry = entry.Context.Entry(item);
                        if (childEntry.State is EntityState.Modified or EntityState.Added or EntityState.Deleted)
                            return true;
                    }
                }
            }
            else if (navigation is ReferenceEntry { TargetEntry.State: EntityState.Modified
                or EntityState.Added or EntityState.Deleted })
            {
                return true;
            }
        }

        return false;
    }
}
