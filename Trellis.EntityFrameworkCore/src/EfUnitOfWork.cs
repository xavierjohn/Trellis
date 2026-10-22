namespace Trellis.EntityFrameworkCore;

using System.Threading;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> and <see cref="ITrackedAggregateSource"/>.
/// Delegates to <see cref="DbContextExtensions.SaveChangesResultUnitAsync(DbContext, CancellationToken)"/>
/// which already maps <see cref="DbUpdateConcurrencyException"/> to <see cref="Error.Conflict"/>,
/// duplicate-key exceptions to <see cref="Error.Conflict"/>,
/// and foreign-key violations to <see cref="Error.Conflict"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nested-scope tracking.</b> Each call to <see cref="BeginScope"/> increments an internal
/// depth counter; the matching <c>Dispose</c> decrements it. <see cref="CommitAsync"/> defers
/// (returns success without touching the database) when the depth is greater than one — only
/// the outermost scope's commit actually persists changes. This makes a successful inner
/// command's commit a no-op so a failing outer command can still abort, addressing the GPT-5.5
/// review's "nested commands commit too early" finding.
/// </para>
/// <para>
/// <b>Tracked aggregate snapshot.</b> Each outermost <see cref="CommitAsync"/> clears the
/// previous snapshot, captures every <see cref="IAggregate"/> entity the change tracker holds
/// (in any state — Added, Modified, Unchanged, Deleted) immediately before calling
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>, and assigns the snapshot to
/// <see cref="CommittedAggregates"/> only after the save returns success. Failed commits and
/// commits that throw (cancellation, connection failure, etc.) leave <see cref="CommittedAggregates"/>
/// as the empty list. Deferred nested commits (depth &gt; 1) do not touch the snapshot.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The concrete <see cref="DbContext"/> type registered in DI.</typeparam>
public class EfUnitOfWork<TContext> : IUnitOfWork, ITrackedAggregateSource
    where TContext : DbContext
{
    private static readonly IReadOnlyList<IAggregate> EmptyAggregates = Array.Empty<IAggregate>();

    private readonly TContext _context;
    private int _scopeDepth;
    private IReadOnlyList<IAggregate> _committedAggregates = EmptyAggregates;

    /// <summary>
    /// Initializes a new instance of <see cref="EfUnitOfWork{TContext}"/>.
    /// </summary>
    /// <param name="context">The scoped <see cref="DbContext"/> to commit through.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public EfUnitOfWork(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public IReadOnlyList<IAggregate> CommittedAggregates => _committedAggregates;

    /// <inheritdoc />
    public async Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
    {
        // Defer until the outermost scope unwinds. A nested scope's depth is at least 2
        // (its own +1 plus the outer's +1); the outermost commit happens at depth 1.
        // Volatile.Read pairs with the Interlocked operations in BeginScope/ScopeReleaser.
        // Deferred commits MUST NOT mutate the snapshot — the outer commit still owns it.
        if (Volatile.Read(ref _scopeDepth) > 1)
            return Result.Ok();

        // Clear before the save attempt so an exception (cancellation, connection failure,
        // etc.) or a failure Result leaves CommittedAggregates as the empty list, never as a
        // stale prior snapshot.
        _committedAggregates = EmptyAggregates;

        var snapshot = SnapshotTrackedAggregates();

        var result = await _context.SaveChangesResultUnitAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
            _committedAggregates = snapshot;

        return result;
    }

    private IReadOnlyList<IAggregate> SnapshotTrackedAggregates()
    {
        List<IAggregate>? snapshot = null;
        foreach (var entry in _context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAggregate aggregate)
                (snapshot ??= new List<IAggregate>()).Add(aggregate);
        }

        return snapshot is null ? EmptyAggregates : snapshot;
    }

    /// <inheritdoc />
    public IUnitOfWorkScope BeginScope()
    {
        var depth = Interlocked.Increment(ref _scopeDepth);
        return new ScopeReleaser(this, depth == 1);
    }

    private sealed class ScopeReleaser : IUnitOfWorkScope
    {
        private readonly EfUnitOfWork<TContext> _owner;
        private bool _disposed;

        public ScopeReleaser(EfUnitOfWork<TContext> owner, bool isOwner)
        {
            _owner = owner;
            IsOwner = isOwner;
        }

        public bool IsOwner { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Decrement(ref _owner._scopeDepth);
        }
    }
}