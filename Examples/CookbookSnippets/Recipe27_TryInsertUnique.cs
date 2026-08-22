// Cookbook Recipe 27 — Idempotent inserts on a unique constraint with TryInsertUniqueAsync.
namespace CookbookSnippets.Recipe27;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.EntityFrameworkCore;

// Domain: a worker records each (EventId, DestinationId) it has dispatched.
public sealed class DispatchedDelivery
{
    public required Guid EventId { get; init; }

    public required Guid DestinationId { get; init; }

    public required DateTimeOffset DispatchedAt { get; init; }
}

// EF Core: composite primary key on (EventId, DestinationId) gives the unique constraint
// TryInsertUniqueAsync relies on; no separate HasIndex().IsUnique() is needed. If your model
// already has a surrogate PK, add the unique index explicitly instead:
// e.HasIndex(d => new { d.EventId, d.DestinationId }).IsUnique().
public sealed class DispatchLogDbContext(DbContextOptions<DispatchLogDbContext> options) : DbContext(options)
{
    public DbSet<DispatchedDelivery> Deliveries => Set<DispatchedDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DispatchedDelivery>()
            .HasKey(d => new { d.EventId, d.DestinationId });
    }
}

public enum DeliveryOutcome
{
    Recorded,
    AlreadyRecorded,
}

public sealed class DispatchLogger(
    DispatchLogDbContext db,
    TimeProvider time,
    ILogger<DispatchLogger> log)
{
    public async Task<Result<DeliveryOutcome>> RecordDeliveryAsync(
        Guid eventId, Guid destinationId, CancellationToken cancellationToken)
    {
        var entry = new DispatchedDelivery
        {
            EventId = eventId,
            DestinationId = destinationId,
            DispatchedAt = time.GetUtcNow(),
        };

        var result = await db.TryInsertUniqueAsync(entry, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
            return Result.Ok(DeliveryOutcome.Recorded);

        if (result.Error is Error.Conflict conflict && conflict.Code == "duplicate.key")
        {
            // The second delivery — exactly what idempotency promises. No-op, do not fail.
#pragma warning disable CA1848, CA1873 // Cookbook snippet mirrors the recipe text; LoggerMessage delegates would obscure the point.
            log.LogInformation(
                "Duplicate delivery suppressed for event {EventId} / destination {DestinationId} (table {Table}, constraint {Constraint}).",
                eventId,
                destinationId,
                conflict.ConstraintTableName,
                conflict.ConstraintName);
#pragma warning restore CA1848, CA1873
            return Result.Ok(DeliveryOutcome.AlreadyRecorded);
        }

        return Result.Fail<DeliveryOutcome>(result.Error);
    }
}
