namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// EF Core <see cref="IConsumerCheckpointStore"/>: reads and writes the resume cursor in its own short unit of
/// work on a fresh DI scope, so advancing the checkpoint is durable on return and isolated from any work the
/// caller has staged on its own <typeparamref name="TContext"/>. The checkpoint is performance state, so it is
/// deliberately decoupled from the caller's correctness transaction rather than enrolled in it (unlike the
/// inbox dedup row).
/// </summary>
/// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that owns the checkpoint table.</typeparam>
internal sealed class EfConsumerCheckpointStore<TContext> : IConsumerCheckpointStore
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;

    public EfConsumerCheckpointStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
    }

    private static void ValidateConsumerId(string consumerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        if (consumerId.Length > InboxOptions.MaxConsumerIdLength)
            throw new ArgumentException(
                $"consumerId must be at most {InboxOptions.MaxConsumerIdLength} characters; it is stored in a fixed-width key column.",
                nameof(consumerId));
    }

    /// <inheritdoc />
    public async Task<Maybe<string>> GetAsync(string consumerId, CancellationToken cancellationToken)
    {
        ValidateConsumerId(consumerId);

        var scope = _scopeFactory.CreateAsyncScope();
        await using var scopeLifetime = scope.ConfigureAwait(false);
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var position = await context.Set<ConsumerCheckpoint>()
            .AsNoTracking()
            .Where(c => c.ConsumerId == consumerId)
            .Select(c => c.Position)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return Maybe<string>.From(position);
    }

    /// <inheritdoc />
    public async Task SetAsync(string consumerId, string position, CancellationToken cancellationToken)
    {
        ValidateConsumerId(consumerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(position);

        var scope = _scopeFactory.CreateAsyncScope();
        await using var scopeLifetime = scope.ConfigureAwait(false);
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var checkpoints = context.Set<ConsumerCheckpoint>();

        var existing = await checkpoints
            .FirstOrDefaultAsync(c => c.ConsumerId == consumerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Advance(position, _timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        checkpoints.Add(ConsumerCheckpoint.Create(consumerId, position, _timeProvider.GetUtcNow()));
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsDuplicateKey(ex))
        {
            // A concurrent first write inserted the row between our read and save. Re-read the committed row
            // and advance it so SetAsync resolves to last-writer-wins rather than surfacing the race.
            context.ChangeTracker.Clear();
            var winner = await checkpoints
                .FirstAsync(c => c.ConsumerId == consumerId, cancellationToken)
                .ConfigureAwait(false);
            winner.Advance(position, _timeProvider.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}