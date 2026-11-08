---
name: recipe-27-idempotent-insert
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows idempotent inserts with TryInsertUniqueAsync on a unique constraint. Use when saving unless a row exists or matching duplicate-key and retry conflicts."
---
# Recipe 27 — Idempotent inserts on a unique constraint with `TryInsertUniqueAsync`

**Problem.** A worker (or any caller) processes events that may be redelivered. It must record "I handled event X for destination Y" exactly once. A `(EventId, DestinationId)` unique index in the database is the source of truth — the second delivery should silently no-op, not crash, not double-process. Doing this with `Any(...)` + `Add` + `SaveChangesAsync` is a TOCTOU race: two concurrent deliveries both see "not present", both `Add`, one wins and the loser throws `DbUpdateException`. Catching `DbUpdateException` and string-matching on the inner exception's message is provider-specific (SQL Server says one thing, PostgreSQL another, SQLite a third) and easy to get subtly wrong.

**Solution.** `DbContext.TryInsertUniqueAsync(entity, ct)` (from `Trellis.EntityFrameworkCore.DbContextIdempotencyExtensions`) adds the entity, calls `SaveChangesAsync`, and converts a provider-level unique-constraint violation into `Result.Fail(new Error.Conflict(Resource: null, Code: FaultCodes.DuplicateKey))` with a generic safe `Detail`. Constraint identity (`ConstraintName`, `ConstraintTableName`) is extracted on a best-effort basis and attached to the `Error.Conflict` payload for structured logging. All other failures — concurrency, foreign-key, cancellation, connection errors — propagate to the caller so retry policies and global handlers see them. The helper requires a clean `DbContext` (no pending changes) so a duplicate-key violation can be unambiguously attributed to the entity being inserted.

```csharp
// Domain: a worker records each (EventId, DestinationId) it has dispatched.
public sealed class DispatchedDelivery
{
    public required Guid EventId { get; init; }
    public required Guid DestinationId { get; init; }
    public required DateTimeOffset DispatchedAt { get; init; }
}

// EF Core: composite primary key on (EventId, DestinationId) gives the unique
// constraint TryInsertUniqueAsync relies on; no separate HasIndex().IsUnique()
// is needed. If your model already has a surrogate PK, add the unique index
// explicitly instead: e.HasIndex(d => new { d.EventId, d.DestinationId }).IsUnique().
public sealed class DispatchLogDbContext(DbContextOptions<DispatchLogDbContext> options) : DbContext(options)
{
    public DbSet<DispatchedDelivery> Deliveries => Set<DispatchedDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DispatchedDelivery>()
            .HasKey(d => new { d.EventId, d.DestinationId });
    }
}

public sealed class DispatchLogger(DispatchLogDbContext db, TimeProvider time, ILogger<DispatchLogger> log)
{
    public async Task<Result<DeliveryOutcome>> RecordDeliveryAsync(
        Guid eventId, Guid destinationId, CancellationToken ct)
    {
        var entry = new DispatchedDelivery
        {
            EventId = eventId,
            DestinationId = destinationId,
            DispatchedAt = time.GetUtcNow(),
        };

        var result = await db.TryInsertUniqueAsync(entry, ct);

        if (result.IsSuccess)
            return Result.Ok(DeliveryOutcome.Recorded);

        if (result.Error is Error.Conflict conflict && conflict.Code == FaultCodes.DuplicateKey)
        {
            // The second delivery — exactly what idempotency promises. No-op, do not fail.
            log.LogInformation(
                "Duplicate delivery suppressed for event {EventId} / destination {DestinationId} (table {Table}, constraint {Constraint}).",
                eventId, destinationId, conflict.ConstraintTableName, conflict.ConstraintName);
            return Result.Ok(DeliveryOutcome.AlreadyRecorded);
        }

        return Result.Fail<DeliveryOutcome>(result.Error);
    }
}

public enum DeliveryOutcome { Recorded, AlreadyRecorded }
```

**What it shows.** `TryInsertUniqueAsync` is the framework idiom for "insert unless a unique constraint says it already exists". The success path returns `Result.Ok(entity)` and the entity has its EF-populated generated values (PK, row version, sequence-assigned columns) in place on the same instance the caller passed in. The duplicate path returns a failed `Result<TEntity>` carrying an `Error.Conflict` whose `Code` is `"duplicate.key"` and whose `ConstraintName` / `ConstraintTableName` telemetry fields are populated from the underlying provider exception, and the helper detaches the attempted entity from the change tracker so a retry with a freshly-constructed entity does not re-flush the original on the next `SaveChangesAsync`. `ConstraintName` and `ConstraintTableName` are best-effort and marked `[JsonIgnore]` on `Error.Conflict` — they are telemetry fields for structured logs, never serialized to API responses; the safe-for-clients message lives in `Detail`. Foreign-key violations, `DbUpdateConcurrencyException`, connection-level exceptions, and `OperationCanceledException` all propagate normally so retry policies still see them. The clean-context precondition (throws `InvalidOperationException` if `ChangeTracker.HasChanges()` is `true` on entry) prevents the failure from being mis-attributed to the inserted entity when unrelated pending changes exist; flush them first or use a fresh context. Pair the helper with `SaveChangesWithRetryAsync` (from `Trellis.EntityFrameworkCore.DbContextRetryExtensions`) when the retry shape is "regenerate a key and try again" rather than "second writer wins" — the two helpers are complementary, not substitutes.

Match `FaultCodes.DuplicateKey`, not a copied wire literal. The shared Core vocabulary also provides
`FaultCodes.ReferentialIntegrity`, `FaultCodes.RetryAborted`, and `FaultCodes.RetryExhausted` for
Result-returning save and retry helpers. Their spellings are frozen; the constants are available to
clients and tests without an EF Core dependency.

`DbExceptionClassifier.ExtractConstraintIdentity(DbUpdateException)` is the lower-level building block the helper uses; the same identity is also now populated on the `Error.Conflict` returned by `SaveChangesResultAsync` and `SaveChangesWithRetryAsync` for the `duplicate.key` and `referential.integrity` reason codes, so existing code paths get the new telemetry fields for free.
