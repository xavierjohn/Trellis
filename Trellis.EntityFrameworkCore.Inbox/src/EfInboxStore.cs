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

    public EfInboxStore(TContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<bool> TryRecordAsync(string consumerId, IntegrationEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var alreadyProcessed = await _context.Set<InboxMessage>()
            .AnyAsync(m => m.ConsumerId == consumerId && m.MessageId == envelope.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyProcessed)
            return false;

        // Enrol the dedup row in the caller's unit of work — it is persisted by the dispatcher's SaveChanges
        // alongside the handler side effects. The composite primary key still guards a concurrent duplicate.
        _context.Set<InboxMessage>().Add(
            InboxMessage.Create(consumerId, envelope, _timeProvider.GetUtcNow()));
        return true;
    }
}
