namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Base class for EF Core repositories that persist <see cref="Aggregate{TId}"/> instances.
/// Provides standard read and staging methods. Repositories stage changes to the
/// <see cref="DbContext"/> change tracker; the <see cref="IUnitOfWork"/> (typically driven
/// by a pipeline behavior) is responsible for committing staged changes.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The type of the aggregate's unique identifier.</typeparam>
/// <remarks>
/// <para>
/// This base class eliminates the repetitive find/query/add/remove boilerplate that appears in every
/// concrete repository. Repositories with custom queries inherit and add methods.
/// </para>
/// <para>
/// Override <see cref="BuildFindByIdQuery"/> or <see cref="BuildQueryBase"/> to add
/// <c>.Include()</c> chains for eager loading.
/// </para>
/// <para>
/// <b>Staging vs. Committing:</b> Methods like <see cref="Add"/>, <see cref="Remove"/>,
/// and <see cref="RemoveByIdAsync"/> stage changes in the EF Core change tracker but never
/// call <c>SaveChanges</c>. The commit boundary is owned by the pipeline
/// (see <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/>) or by explicitly
/// calling <see cref="IUnitOfWork.CommitAsync"/>.
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

    // ──────────────────────────────────────────────
    // Virtual hooks
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds the base query used by <see cref="FindByIdAsync"/>. Override to add
    /// <c>.Include()</c> chains for eager loading when finding by ID.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> starting from <see cref="DbSet"/>.</returns>
    protected virtual IQueryable<TAggregate> BuildFindByIdQuery() => DbSet;

    /// <summary>
    /// Builds the base query used by <see cref="QueryAsync"/>, <see cref="ExistsAsync(Specification{TAggregate}, CancellationToken)"/>,
    /// and <see cref="CountAsync"/>. Override to add <c>.Include()</c> chains for list/search queries.
    /// The default query is no-tracking to avoid unnecessary change tracking for read operations.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> starting from <see cref="DbSet"/> with no tracking.</returns>
    protected virtual IQueryable<TAggregate> BuildQueryBase() => DbSet.AsNoTracking();

    // ──────────────────────────────────────────────
    // Reads
    // ──────────────────────────────────────────────

    /// <summary>
    /// Finds an aggregate by its unique identifier.
    /// The returned aggregate is tracked by the change tracker (suitable for mutations).
    /// Returns <see cref="Maybe{T}.None"/> if not found.
    /// </summary>
    /// <param name="id">The aggregate identifier to search for.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the aggregate, or None if not found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    public virtual Task<Maybe<TAggregate>> FindByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var predicate = BuildIdPredicate(id);
        return BuildFindByIdQuery().FirstOrDefaultMaybeAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Queries aggregates matching the given specification.
    /// Results are no-tracking by default (via <see cref="BuildQueryBase"/>).
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
    /// Checks whether an aggregate with the given identifier exists.
    /// Uses <see cref="BuildQueryBase"/> (no-tracking, lightweight — no entity materialization)
    /// so that repository-level query customization (e.g., soft-delete or tenant filters) is respected.
    /// </summary>
    /// <param name="id">The aggregate identifier to check.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns><see langword="true"/> if an aggregate with the given ID exists; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    public virtual Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var predicate = BuildIdPredicate(id);
        return BuildQueryBase().AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Checks whether any aggregate matches the given specification.
    /// Uses <see cref="BuildQueryBase"/> (no-tracking by default).
    /// </summary>
    /// <param name="specification">The specification to test.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns><see langword="true"/> if at least one aggregate matches; otherwise <see langword="false"/>.</returns>
    public virtual Task<bool> ExistsAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return BuildQueryBase().Where(specification).AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Counts aggregates matching the given specification.
    /// Uses <see cref="BuildQueryBase"/> (no-tracking by default).
    /// </summary>
    /// <param name="specification">The specification to filter by.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of matching aggregates.</returns>
    public virtual Task<int> CountAsync(
        Specification<TAggregate> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return BuildQueryBase().Where(specification).CountAsync(cancellationToken);
    }

    // ──────────────────────────────────────────────
    // Staging (change tracker only — never calls SaveChanges)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Stages a new aggregate for insertion. If the aggregate is already tracked,
    /// this is a no-op (EF Core will detect modifications automatically).
    /// Does not call <c>SaveChanges</c> — the commit is deferred to the pipeline or
    /// <see cref="IUnitOfWork.CommitAsync"/>.
    /// </summary>
    /// <param name="aggregate">The aggregate to stage for insertion.</param>
    public virtual void Add(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var entry = _context.Entry(aggregate);
        if (entry.State == EntityState.Detached)
            DbSet.Add(aggregate);
    }

    /// <summary>
    /// Stages an aggregate for deletion. The aggregate must be tracked or will be attached
    /// before marking for deletion.
    /// Does not call <c>SaveChanges</c> — the commit is deferred to the pipeline or
    /// <see cref="IUnitOfWork.CommitAsync"/>.
    /// </summary>
    /// <param name="aggregate">The aggregate to stage for deletion.</param>
    public virtual void Remove(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        DbSet.Remove(aggregate);
    }

    /// <summary>
    /// Looks up an aggregate by ID and stages it for deletion.
    /// Uses <see cref="DbSet{TEntity}.FindAsync(object?[], CancellationToken)"/> directly
    /// (not <see cref="BuildFindByIdQuery"/>) to avoid loading heavy Include graphs.
    /// Returns a not-found error if the aggregate does not exist.
    /// Does not call <c>SaveChanges</c> — the commit is deferred to the pipeline or
    /// <see cref="IUnitOfWork.CommitAsync"/>.
    /// <para>
    /// <b>Query filters:</b> Starting with EF Core 8, <c>FindAsync</c> applies global
    /// query filters (soft-delete, multi-tenant). A row excluded by a filter is treated
    /// as not-existing and yields <see cref="Error.NotFound"/> — the safe default. Tests
    /// in <c>RepositoryBaseFilterTests</c> guard this contract. Override this method only
    /// if you intentionally want to operate over filtered rows (in which case use
    /// <c>DbSet.IgnoreQueryFilters().FirstOrDefaultAsync(BuildIdPredicate(id), ct)</c>).
    /// </para>
    /// </summary>
    /// <param name="id">The aggregate identifier to remove.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{TValue}"/> with <see cref="Unit"/> representing success (staged) or not-found failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    public virtual async Task<Result<Unit>> RemoveByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        var entity = await DbSet.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (entity is null)
            return Result.Fail(new Error.NotFound(ResourceRef.For<TAggregate>(id)) { Detail = $"{ResourceRef.FormatTypeName(typeof(TAggregate))} with ID '{id}' not found." });

        DbSet.Remove(entity);
        return Result.Ok();
    }

    // ──────────────────────────────────────────────
    // Internal helpers
    // ──────────────────────────────────────────────

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