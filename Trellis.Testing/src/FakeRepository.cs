namespace Trellis.Testing;

using Trellis;

/// <summary>
/// In-memory fake repository for testing aggregates.
/// Provides a simple in-memory store with domain event tracking.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type.</typeparam>
/// <typeparam name="TId">The aggregate ID type.</typeparam>
/// <remarks>
/// <para>
/// Two persistence surfaces are exposed and they are intentional opposites:
/// </para>
/// <list type="bullet">
/// <item>
/// <description><b>Setup surface</b> — <see cref="Add"/>, <see cref="Remove"/>,
/// <see cref="RemoveByIdAsync"/>. Mirror the production
/// <c>RepositoryBase&lt;TAggregate, TId&gt;</c> staging API so the same
/// <c>IRepository</c> contract works in handlers and in tests. Use these to seed the
/// store and to exercise the same code path the handler will run against EF.</description>
/// </item>
/// <item>
/// <description><b>Result-shape surface</b> — <see cref="SaveAsync"/>,
/// <see cref="DeleteAsync"/>. Return <see cref="Result"/> directly so tests that
/// specifically assert on conflict-handling or not-found-result behavior have a
/// concrete <see cref="Result"/> to assert on. They are not part of the production
/// <c>RepositoryBase</c> surface and should not appear on production
/// <c>IRepository</c> contracts.</description>
/// </item>
/// </list>
/// <para>
/// See cookbook <c>Recipe 16 — Unit of work in handlers: Add staging vs immediate SaveAsync</c>
/// for the full guidance on which method to call from where.
/// </para>
/// </remarks>
public class FakeRepository<TAggregate, TId>
    where TAggregate : Aggregate<TId>
    where TId : notnull
{
    private readonly Dictionary<TId, TAggregate> _store = new();
    private readonly List<IDomainEvent> _publishedEvents = new();
    private readonly List<Func<TAggregate, object?>> _uniqueConstraints = new();

    /// <summary>
    /// Gets the list of domain events published by saved aggregates.
    /// </summary>
    public IReadOnlyList<IDomainEvent> PublishedEvents => _publishedEvents.AsReadOnly();

    /// <summary>
    /// Adds a unique constraint on the specified property. When <see cref="SaveAsync"/> is called,
    /// the repository checks that no other aggregate (with a different ID) has the same value
    /// for this property. Returns an <see cref="Error.Conflict"/> on violation.
    /// </summary>
    /// <param name="propertySelector">A function selecting the property to constrain.</param>
    /// <returns>This repository for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// var repo = new FakeRepository&lt;Customer, CustomerId&gt;()
    ///     .WithUniqueConstraint(c =&gt; c.Email);
    /// </code>
    /// </example>
    public FakeRepository<TAggregate, TId> WithUniqueConstraint(Func<TAggregate, object?> propertySelector)
    {
        ArgumentNullException.ThrowIfNull(propertySelector);
        _uniqueConstraints.Add(propertySelector);
        return this;
    }

    /// <summary>
    /// Gets an aggregate by its ID.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result containing the aggregate or an <see cref="Error.NotFound"/>.</returns>
    public Task<Result<TAggregate>> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(id, out var aggregate))
            return Task.FromResult(Result.Ok(aggregate));

        return Task.FromResult(Result.Fail<TAggregate>(
            new Error.NotFound(new ResourceRef(typeof(TAggregate).Name, id?.ToString())) { Detail = $"{typeof(TAggregate).Name} with ID {id} not found" }));
    }

    /// <summary>
    /// Finds an aggregate by its ID, returning Maybe if not found.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Maybe with the aggregate or None.</returns>
    public Task<Maybe<TAggregate>> FindByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        var maybe = _store.TryGetValue(id, out var aggregate)
            ? Maybe.From(aggregate)
            : Maybe<TAggregate>.None;

        return Task.FromResult(maybe);
    }

    /// <summary>
    /// Stages a new aggregate for insertion into the in-memory store, mirroring
    /// <c>RepositoryBase&lt;TAggregate, TId&gt;.Add</c>. Use this from handlers (and from
    /// test setup that exercises handlers) so the same <c>void Add(T)</c> surface works
    /// in both the EF and fake implementations of an <c>IRepository</c> contract.
    /// </summary>
    /// <param name="aggregate">The aggregate to stage.</param>
    /// <remarks>
    /// <para>
    /// The fake has no separate commit boundary — <c>Add</c> immediately makes the
    /// aggregate visible to subsequent <c>FindByIdAsync</c>/<c>WhereAsync</c> calls and
    /// captures its uncommitted domain events into <see cref="PublishedEvents"/>.
    /// </para>
    /// <para>
    /// Unique constraints registered with <see cref="WithUniqueConstraint"/> are
    /// enforced eagerly here: a violation throws <see cref="InvalidOperationException"/>
    /// rather than returning <see cref="Error.Conflict"/>. The reason is that
    /// <c>Add</c> is a setup affordance — failures usually mean the test itself is
    /// wrong, and a loud throw points to the offending call site immediately. To test
    /// production conflict handling, call <see cref="SaveAsync"/> instead and assert on
    /// the returned <see cref="Result"/>.
    /// </para>
    /// </remarks>
    public void Add(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        var id = aggregate.Id;
        foreach (var constraint in _uniqueConstraints)
        {
            var value = constraint(aggregate);
            var conflict = _store.Values
                .FirstOrDefault(existing => !existing.Id.Equals(id) && Equals(constraint(existing), value));

            if (conflict is not null)
                throw new InvalidOperationException(
                    $"Cannot Add {typeof(TAggregate).Name} with ID '{id}': would violate unique constraint " +
                    $"(another aggregate with the same constrained value already exists). " +
                    $"If you are testing conflict handling, call SaveAsync and assert on the Result instead.");
        }

        _store[id] = aggregate;
        _publishedEvents.AddRange(aggregate.UncommittedEvents());
        aggregate.AcceptChanges();
    }

    /// <summary>
    /// Stages an aggregate for deletion from the in-memory store, mirroring
    /// <c>RepositoryBase&lt;TAggregate, TId&gt;.Remove</c>. No-op if the aggregate is not
    /// in the store (matching the EF semantics where the change tracker accepts
    /// the call without verifying database existence).
    /// </summary>
    /// <param name="aggregate">The aggregate to remove.</param>
    public void Remove(TAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store.Remove(aggregate.Id);
    }

    /// <summary>
    /// Looks up an aggregate by ID and removes it. Returns <see cref="Error.NotFound"/>
    /// if the aggregate does not exist. Mirrors
    /// <c>RepositoryBase&lt;TAggregate, TId&gt;.RemoveByIdAsync</c>.
    /// </summary>
    /// <param name="id">The aggregate ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token (ignored — operation is synchronous).</param>
    /// <returns>A <see cref="Result"/> indicating success or not-found failure.</returns>
    public Task<Result<Unit>> RemoveByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        DeleteAsync(id, cancellationToken);

    /// <summary>
    /// Saves an aggregate and captures its domain events.
    /// </summary>
    /// <param name="aggregate">The aggregate to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result indicating success or failure.</returns>
    public Task<Result<Unit>> SaveAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        var id = aggregate.Id;

        // Check unique constraints against other aggregates (not self)
        foreach (var constraint in _uniqueConstraints)
        {
            var value = constraint(aggregate);
            var conflict = _store.Values
                .FirstOrDefault(existing => !existing.Id.Equals(id) && Equals(constraint(existing), value));

            if (conflict is not null)
                return Task.FromResult(Result.Fail(
                    new Error.Conflict(Resource: new ResourceRef(typeof(TAggregate).Name, id?.ToString()), ReasonCode: "duplicate.unique.constraint") { Detail = $"A {typeof(TAggregate).Name} with the same value already exists." }));
        }

        _store[id] = aggregate;
        _publishedEvents.AddRange(aggregate.UncommittedEvents());
        aggregate.AcceptChanges();
        return Task.FromResult(Result.Ok());
    }

    /// <summary>
    /// Deletes an aggregate by its ID.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result indicating success or <see cref="Error.NotFound"/>.</returns>
    public Task<Result<Unit>> DeleteAsync(TId id, CancellationToken cancellationToken = default)
    {
        if (_store.Remove(id))
            return Task.FromResult(Result.Ok());

        return Task.FromResult(Result.Fail(
            new Error.NotFound(new ResourceRef(typeof(TAggregate).Name, id?.ToString())) { Detail = $"{typeof(TAggregate).Name} with ID {id} not found" }));
    }

    /// <summary>
    /// Clears all stored aggregates and published events.
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _publishedEvents.Clear();
    }

    /// <summary>
    /// Checks if an aggregate with the specified ID exists.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <returns>True if the aggregate exists, false otherwise.</returns>
    public bool Exists(TId id) => _store.ContainsKey(id);

    /// <summary>
    /// Gets an aggregate by ID without wrapping in Result.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <returns>The aggregate or null if not found.</returns>
    public TAggregate? Get(TId id) => _store.GetValueOrDefault(id);

    /// <summary>
    /// Gets all stored aggregates.
    /// </summary>
    /// <returns>All aggregates in the repository.</returns>
    public IEnumerable<TAggregate> GetAll() => [.. _store.Values];

    /// <summary>
    /// Finds the first aggregate matching the predicate, returning <see cref="Maybe{T}.None"/> if no match.
    /// Use in test repository adapters for custom query methods (e.g., <c>FindByEmailAsync</c>).
    /// </summary>
    /// <param name="predicate">The predicate to match.</param>
    /// <returns>Maybe with the first matching aggregate or None.</returns>
    public Task<Maybe<TAggregate>> FindAsync(Func<TAggregate, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var match = _store.Values.FirstOrDefault(predicate);
        return Task.FromResult(match is not null ? Maybe.From(match) : Maybe<TAggregate>.None);
    }

    /// <summary>
    /// Returns all aggregates matching the predicate.
    /// Use in test repository adapters for custom query methods (e.g., <c>GetByCustomerIdAsync</c>).
    /// </summary>
    /// <param name="predicate">The predicate to filter by.</param>
    /// <returns>A list of matching aggregates.</returns>
    public Task<IReadOnlyList<TAggregate>> WhereAsync(Func<TAggregate, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Task.FromResult<IReadOnlyList<TAggregate>>(_store.Values.Where(predicate).ToList());
    }

    /// <summary>
    /// Returns all aggregates matching the specification.
    /// Use in test repository adapters for specification-based queries (e.g., <c>GetOverdueOrdersAsync</c>).
    /// </summary>
    /// <param name="specification">The specification to evaluate.</param>
    /// <returns>A list of matching aggregates.</returns>
    public Task<IReadOnlyList<TAggregate>> WhereAsync(Specification<TAggregate> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return Task.FromResult<IReadOnlyList<TAggregate>>(_store.Values.Where(specification.IsSatisfiedBy).ToList());
    }

    /// <summary>
    /// Gets the count of stored aggregates.
    /// </summary>
    public int Count => _store.Count;
}