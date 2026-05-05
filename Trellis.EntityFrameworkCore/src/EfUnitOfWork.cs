namespace Trellis.EntityFrameworkCore;

using System.Threading;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>.
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
/// </remarks>
/// <typeparam name="TContext">The concrete <see cref="DbContext"/> type registered in DI.</typeparam>
public class EfUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext
{
    private readonly TContext _context;
    private int _scopeDepth;

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
    public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
    {
        // Defer until the outermost scope unwinds. A nested scope's depth is at least 2
        // (its own +1 plus the outer's +1); the outermost commit happens at depth 1.
        // Volatile.Read pairs with the Interlocked operations in BeginScope/ScopeReleaser.
        if (Volatile.Read(ref _scopeDepth) > 1)
            return Task.FromResult(Result.Ok());

        return _context.SaveChangesResultUnitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable BeginScope()
    {
        Interlocked.Increment(ref _scopeDepth);
        return new ScopeReleaser(this);
    }

    private sealed class ScopeReleaser : IDisposable
    {
        private readonly EfUnitOfWork<TContext> _owner;
        private bool _disposed;

        public ScopeReleaser(EfUnitOfWork<TContext> owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Decrement(ref _owner._scopeDepth);
        }
    }
}