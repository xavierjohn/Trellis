namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="IInboxStore"/>: records dedup rows in the consumer's <typeparamref name="TContext"/>
/// so they enrol in the same unit of work (and transaction) as the handler side effects.
/// </summary>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that owns the inbox table.</typeparam>
internal sealed class EfInboxStore<TContext> : IInboxStore
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly bool _useSingleParameterForFilterUnprocessed;

    public EfInboxStore(TContext context, TimeProvider timeProvider, InboxOptions options)
    {
        _context = context;
        _timeProvider = timeProvider;
        _useSingleParameterForFilterUnprocessed = options.UseSingleParameterForFilterUnprocessed;
    }

    /// <inheritdoc />
    public async Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken)
    {
        InboxConsumerId.Validate(consumerId);
        ArgumentNullException.ThrowIfNull(record);

        var alreadyProcessed = await _context.Set<InboxMessage>()
            .AnyAsync(m => m.ConsumerId == consumerId && m.MessageId == record.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyProcessed)
            return false;

        // Enrol the dedup row in the caller's unit of work — it is persisted by the dispatcher's SaveChanges
        // alongside the handler side effects. The composite primary key still guards a concurrent duplicate.
        _context.Set<InboxMessage>().Add(
            InboxMessage.Create(consumerId, record, _timeProvider.GetUtcNow()));
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> FilterUnprocessedAsync(
        string consumerId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken)
    {
        InboxConsumerId.Validate(consumerId);
        ArgumentNullException.ThrowIfNull(messageIds);
        if (messageIds.Count == 0)
            return [];

        if (_useSingleParameterForFilterUnprocessed && _context.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new InvalidOperationException(
                $"InboxOptions.{nameof(InboxOptions.UseSingleParameterForFilterUnprocessed)} requires the EF Core SQL Server provider (SQL Server or Azure SQL).");

        // Anti-join: fetch the already-recorded ids from this window in one round trip, then keep the rest
        // in the caller's order. A pure read — nothing is staged in the unit of work.
        var messages = _context.Set<InboxMessage>().AsNoTracking();
        var processedMessages = _useSingleParameterForFilterUnprocessed
            ? messages.Where(m => m.ConsumerId == consumerId && EF.Parameter(messageIds).Contains(m.MessageId))
            : messages.Where(m => m.ConsumerId == consumerId && messageIds.Contains(m.MessageId));
        var processed = (await processedMessages
            .Select(m => m.MessageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToHashSet();

        return processed.Count == 0
            ? messageIds.ToList()
            : messageIds.Where(id => !processed.Contains(id)).ToList();
    }
}