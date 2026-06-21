namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Operator-facing maintenance for the transactional outbox: inspect dead-lettered (parked) messages and
/// replay them once the cause of their failure has been fixed (for example after deploying the missing
/// handler assembly or correcting a bad payload). Resolve it from a scope — an admin endpoint, a CLI, or a
/// maintenance job — alongside the relay.
/// </summary>
/// <remarks>
/// This targets the single outbox context registered in the service collection. Registering more than one
/// outbox context in one container is not supported — like <see cref="OutboxOptions"/>, this facade resolves
/// to the first one registered; run one outbox per composition.
/// </remarks>
public interface IOutboxMaintenance
{
    /// <summary>
    /// Returns up to <paramref name="limit"/> dead-lettered messages — rows that exhausted
    /// <see cref="OutboxOptions.MaxAttempts"/> and are no longer retried — oldest first, for inspection.
    /// </summary>
    /// <param name="limit">The maximum number of rows to return. Must be greater than zero.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays a single dead-lettered message by <see cref="OutboxMessage.Id"/>: resets its attempt count
    /// and clears its lease so the relay drains it again. Returns the number of rows replayed — 0 if the
    /// message does not exist or is not dead-lettered, 1 otherwise.
    /// </summary>
    /// <param name="id">The <see cref="OutboxMessage.Id"/> of the message to replay.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<int> ReplayAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays every currently dead-lettered message: resets their attempt counts and clears their leases so
    /// the relay drains them again. Returns the number of rows replayed. The set is a snapshot taken when the
    /// statement runs — messages that dead-letter after this call are not included.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<int> ReplayAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IOutboxMaintenance"/> over the application's <typeparamref name="TContext"/>. Registered by
/// <see cref="OutboxServiceCollectionExtensions.AddTrellisOutbox{TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the outbox table.</typeparam>
internal sealed class OutboxMaintenance<TContext> : IOutboxMaintenance
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly OutboxOptions _options;

    public OutboxMaintenance(TContext context, OutboxOptions options)
    {
        _context = context;
        _options = options;
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetDeadLetteredAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await _context.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.Attempts >= _options.MaxAttempts)
            .OrderBy(m => m.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> ReplayAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Set<OutboxMessage>()
            .Where(m => m.Id == id && m.ProcessedAt == null && m.Attempts >= _options.MaxAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Attempts, 0)
                    .SetProperty(m => m.LastError, (string?)null)
                    .SetProperty(m => m.LockedUntil, (DateTime?)null)
                    .SetProperty(m => m.LockedBy, (Guid?)null),
                cancellationToken);

    public Task<int> ReplayAllAsync(CancellationToken cancellationToken = default) =>
        _context.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.Attempts >= _options.MaxAttempts)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.Attempts, 0)
                    .SetProperty(m => m.LastError, (string?)null)
                    .SetProperty(m => m.LockedUntil, (DateTime?)null)
                    .SetProperty(m => m.LockedBy, (Guid?)null),
                cancellationToken);
}
