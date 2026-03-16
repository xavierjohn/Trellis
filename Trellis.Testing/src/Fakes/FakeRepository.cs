namespace Trellis.Testing.Fakes;

using Trellis;

/// <summary>
/// In-memory fake repository for testing aggregates.
/// Provides a simple in-memory store with domain event tracking.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type.</typeparam>
/// <typeparam name="TId">The aggregate ID type.</typeparam>
public class FakeRepository<TAggregate, TId>
    where TAggregate : Aggregate<TId>
    where TId : notnull
{
    private readonly Dictionary<TId, TAggregate> _store = new();
    private readonly List<IDomainEvent> _publishedEvents = new();

    /// <summary>
    /// Gets the list of domain events published by saved aggregates.
    /// </summary>
    public IReadOnlyList<IDomainEvent> PublishedEvents => Array.AsReadOnly([.. _publishedEvents]);

    /// <summary>
    /// Gets an aggregate by its ID.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A Result containing the aggregate or a NotFoundError.</returns>
    public Task<Result<TAggregate>> GetByIdAsync(TId id, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var aggregate))
            return Task.FromResult(Result.Success(aggregate));

        return Task.FromResult(Result.Failure<TAggregate>(
            Error.NotFound($"{typeof(TAggregate).Name} with ID {id} not found")));
    }

    /// <summary>
    /// Finds an aggregate by its ID, returning Maybe if not found.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A Result containing Maybe with the aggregate or None.</returns>
    public Task<Result<Maybe<TAggregate>>> FindByIdAsync(TId id, CancellationToken ct = default)
    {
        var maybe = _store.TryGetValue(id, out var aggregate)
            ? Maybe.From(aggregate)
            : Maybe.None<TAggregate>();

        return Task.FromResult(Result.Success(maybe));
    }

    /// <summary>
    /// Saves an aggregate and captures its domain events.
    /// </summary>
    /// <param name="aggregate">The aggregate to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A Result indicating success or failure.</returns>
    public Task<Result<Unit>> SaveAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        var id = aggregate.Id;
        _store[id] = aggregate;
        _publishedEvents.AddRange(aggregate.UncommittedEvents());
        aggregate.AcceptChanges();
        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Deletes an aggregate by its ID.
    /// </summary>
    /// <param name="id">The aggregate ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A Result indicating success or NotFoundError.</returns>
    public Task<Result<Unit>> DeleteAsync(TId id, CancellationToken ct = default)
    {
        if (_store.Remove(id))
            return Task.FromResult(Result.Success());

        return Task.FromResult(Result.Failure<Unit>(
            Error.NotFound($"{typeof(TAggregate).Name} with ID {id} not found")));
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
    public IEnumerable<TAggregate> GetAll() => _store.Values;

    /// <summary>
    /// Gets the count of stored aggregates.
    /// </summary>
    public int Count => _store.Count;
}