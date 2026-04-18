namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Base class for EF Core repositories that persist <see cref="Aggregate{TId}"/> instances.
/// Provides standard <see cref="FindByIdAsync"/>, <see cref="SaveAsync"/>, and
/// <see cref="QueryAsync"/> implementations that compose the existing Trellis EF Core helpers.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The type of the aggregate's unique identifier.</typeparam>
/// <remarks>
/// <para>
/// This base class eliminates the repetitive save/find/query boilerplate that appears in every
/// concrete repository. Repositories with custom queries inherit and add methods.
/// </para>
/// <para>
/// Override <see cref="BuildFindByIdQuery"/> or <see cref="BuildQueryBase"/> to add
/// <c>.Include()</c> chains for eager loading.
/// </para>
/// <para>
/// <see cref="SaveAsync"/> uses detached-entity detection: if the aggregate's change tracker
/// state is <see cref="EntityState.Detached"/>, it is added to the <see cref="DbSet"/>.
/// This means new aggregates are inserted and already-tracked aggregates are updated.
/// If your scenario requires disconnected-update semantics (attaching a deserialized
/// aggregate that was never tracked), override <see cref="SaveAsync"/> in the concrete
/// repository.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class OrderRepository : RepositoryBase&lt;Order, OrderId&gt;
/// {
///     public OrderRepository(AppDbContext context) : base(context) { }
///
///     protected override IQueryable&lt;Order&gt; BuildFindByIdQuery()
///         =&gt; base.BuildFindByIdQuery().Include(o =&gt; o.LineItems);
/// }
/// </code>
/// </example>
public abstract class RepositoryBase<TAggregate, TId>
    where TAggregate : Aggregate<TId>
    where TId : notnull
{
    private static readonly Expression<Func<TAggregate, TId>> s_idSelector =
        entity => entity.Id;

    private readonly DbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryBase{TAggregate, TId}"/> class.
    /// </summary>
    /// <param name="context">The <see cref="DbContext"/> used for persistence.</param>
    protected RepositoryBase(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// Gets the <see cref="DbSet{TEntity}"/> for this aggregate type.
    /// </summary>
    protected DbSet<TAggregate> DbSet => _context.Set<TAggregate>();

    /// <summary>
    /// Gets the underlying <see cref="DbContext"/>.
    /// Use sparingly — prefer the repository methods for standard operations.
    /// </summary>
    protected DbContext Context => _context;

    /// <summary>
    /// Builds the base query used by <see cref="FindByIdAsync"/>. Override to add
    /// <c>.Include()</c> chains for eager loading when finding by ID.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> starting from <see cref="DbSet"/>.</returns>
    protected virtual IQueryable<TAggregate> BuildFindByIdQuery() => DbSet;

    /// <summary>
    /// Builds the base query used by <see cref="QueryAsync"/>. Override to add
    /// <c>.Include()</c> chains for list/search queries.
    /// The default query is no-tracking to avoid unnecessary change tracking for read operations.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> starting from <see cref="DbSet"/> with no tracking.</returns>
    protected virtual IQueryable<TAggregate> BuildQueryBase() => DbSet.AsNoTracking();

    /// <summary>
    /// Finds an aggregate by its unique identifier.
    /// Returns <see cref="Maybe{T}.None"/> if not found.
    /// </summary>
    /// <param name="id">The aggregate identifier to search for.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the aggregate, or None if not found.</returns>
    public virtual Task<Maybe<TAggregate>> FindByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        var predicate = BuildIdPredicate(id);
        return BuildFindByIdQuery().FirstOrDefaultMaybeAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Persists a new or modified aggregate. New (detached) aggregates are added to the
    /// <see cref="DbSet"/>; already-tracked aggregates are updated in place.
    /// To update an existing aggregate, first retrieve it with <see cref="FindByIdAsync"/>
    /// (which starts change tracking), mutate it, then call this method.
    /// Passing a detached entity that already exists in the database will attempt an insert
    /// and fail with a duplicate-key exception.
    /// </summary>
    /// <param name="aggregate">The aggregate to save.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{Unit}"/> representing success or failure.</returns>
    public virtual Task<Result<Unit>> SaveAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        var entry = _context.Entry(aggregate);
        if (entry.State == EntityState.Detached)
            DbSet.Add(aggregate);

        return _context.SaveChangesResultUnitAsync(cancellationToken);
    }

    /// <summary>
    /// Queries aggregates matching the given specification.
    /// </summary>
    /// <param name="specification">The specification to filter by.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A read-only list of matching aggregates.</returns>
    public virtual async Task<IReadOnlyList<TAggregate>> QueryAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return await BuildQueryBase().Where(specification).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds an expression predicate that compares <c>entity.Id == id</c>.
    /// Uses expression trees to ensure EF Core can translate the comparison via value converters.
    /// </summary>
    private static Expression<Func<TAggregate, bool>> BuildIdPredicate(TId id)
    {
        var parameter = s_idSelector.Parameters[0];
        var idAccess = s_idSelector.Body;
        var constant = Expression.Constant(id, typeof(TId));
        var equality = Expression.Equal(idAccess, constant);
        return Expression.Lambda<Func<TAggregate, bool>>(equality, parameter);
    }
}
