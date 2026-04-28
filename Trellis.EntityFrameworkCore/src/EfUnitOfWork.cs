namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>.
/// Delegates to <see cref="DbContextExtensions.SaveChangesResultUnitAsync(DbContext, CancellationToken)"/>
/// which already maps <see cref="DbUpdateConcurrencyException"/> to <see cref="Error.Conflict"/>,
/// duplicate-key exceptions to <see cref="Error.Conflict"/>,
/// and foreign-key violations to <see cref="Error.Conflict"/>.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="DbContext"/> type registered in DI.</typeparam>
public class EfUnitOfWork<TContext>(TContext context) : IUnitOfWork
    where TContext : DbContext
{
    /// <inheritdoc />
    public Task<Result> CommitAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesResultUnitAsync(cancellationToken);
}