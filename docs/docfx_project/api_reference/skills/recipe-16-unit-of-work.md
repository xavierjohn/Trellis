---
name: recipe-16-unit-of-work
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Explains Add staging versus SaveAsync in handlers and committing business changes with audit records. Use when deciding where SaveChanges belongs."
---
# Recipe 16 — Unit of work in handlers: `Add` staging vs immediate `SaveAsync`

**Requires:** `Trellis.Core`, plus `Trellis.EntityFrameworkCore`, `Trellis.FluentValidation`, `Trellis.Mediator`, `Trellis.Mediator.FluentValidation`, `Trellis.ServiceDefaults`, `Trellis.Testing`.

## Contents

- [Handler staging and commit ownership](#handler-staging-and-commit-ownership)
- [Same-commit audit staging](#same-commit-audit-staging)
- [Testing handlers with an application-owned recorder](#testing-handlers-with-an-application-owned-recorder)

| Task within this recipe | Read |
|---|---|
| Understand who persists repository-staged changes | [Handler staging and commit ownership](#handler-staging-and-commit-ownership) |
| Stage audit records atomically with business writes and reject missing audits | [Same-commit audit staging](#same-commit-audit-staging) |
| Test an audit-recording handler without a container, database, or pipeline | [Testing handlers with an application-owned recorder](#testing-handlers-with-an-application-owned-recorder) |

## Handler staging and commit ownership

**Problem.** A command handler creates a new aggregate. Where does the `SaveChanges` call go? The first time you read a Trellis handler that ends with `repo.Add(order); return Result.Ok(order.Id);` the question is unavoidable: *who actually saves it?*

```csharp
public sealed class CreateOrderHandler(IOrderRepository repo)
    : ICommandHandler<CreateOrderCommand, Result<OrderId>>
{
    public ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct) =>
        Order.TryCreate(cmd.Total)
            .Tap(repo.Add)                  // stages — no save here
            .Map(o => o.Id)
            .AsValueTask();                 // handler returns immediately
}
```

> `repo.Add(entity)` stages the aggregate for insertion via EF Core; `TransactionalCommandBehavior`, registered by `services.AddTrellisUnitOfWork<TContext>()` in your ACL composition root, automatically saves after a successful enclosed pipeline response — no explicit save call is needed in the handler.

**What it shows.** Handlers shape domain state; the pipeline owns the commit boundary.
`RepositoryBase.Add` returns `void` to signal "staged, not persisted" — not an awaitable
save result. `TransactionalCommandBehavior` awaits the enclosed application behaviors
and handler, then saves their final successful response before returning. Ordinary
failure or exception prevents that invocation's commit. `IPersistOnFailure` deliberately
enables persistence for an explicit failure outcome.
See the [canonical pipeline](../trellis-api-mediator.md#canonical-pipeline-order) for the
outer authorization, validation, telemetry, and event-dispatch stages.

| Method | Signature | Saves immediately? | When to use |
|---|---|---|---|
| `RepositoryBase.Add(T)` (and `Remove(T)`, `RemoveByIdAsync(TId)`) | `void` / `Task<Result<Unit>>` for not-found | **No** — staged for the UoW | Handlers and any production-shaped repository contract |
| `FakeRepository.Add(T)` | `void` | n/a (in-memory; visible immediately) | **Test setup** — "put this in the store so the handler can find it" |
| `FakeRepository.SaveAsync(T)` | `Task<Result<Unit>>` | n/a (in-memory; visible immediately) | Tests that explicitly assert on the `Result` shape, e.g., conflict-result handling |

**Anti-pattern → fix.**

```csharp
// ❌ Wrong — explicit SaveChangesAsync in the handler. Bypasses TransactionalCommandBehavior,
//   so cross-aggregate behaviors that depend on a single commit boundary (outbox writes,
//   ETag bumps, audit logs) end up in inconsistent states. Also: you've now committed even
//   if a later behavior in the pipeline fails post-handler.
public ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct) =>
    Order.TryCreate(cmd.Total)
        .Tap(repo.Add)
        .TapAsync(_ => dbContext.SaveChangesAsync(ct));   // ❌ — duplicates UoW, racy

// ❌ Wrong — calling SaveAsync from a production handler. SaveAsync is a FakeRepository
//   convenience for tests. EF repositories don't expose it (and shouldn't).
.TapAsync(o => repo.SaveAsync(o, ct))   // ❌ — IRepository<Order>.SaveAsync doesn't exist

// ✅ Correct — stage with Add, let TransactionalCommandBehavior commit on success.
.Tap(repo.Add)
```

**Test setup pattern.** When unit-testing a handler with `FakeRepository`, prefer `Add` for setup and reserve `SaveAsync` for tests that specifically assert on the Result of the save (conflict handling, etc.). The void surface keeps the test intent visually honest: setup should not have a return value to assert on.

```csharp
// ✅ Setup: void Add — no .GetAwaiter().GetResult(), no Result assertion in setup.
var customers = new FakeRepository<Customer, CustomerId>();
customers.Add(MakeAlice());   // helper builds a valid Customer via Customer.TryCreate(...)

// ✅ Conflict-result test: SaveAsync returns the Error.Conflict so the test can assert.
var customers = new FakeRepository<Customer, CustomerId>().WithUniqueConstraint(c => c.Email);
customers.Add(MakeAlice());                                          // first alice — accepted
var result = await customers.SaveAsync(MakeAlice());                 // intentional conflict
result.UnwrapError().Should().BeOfType<Error.Conflict>();
```

> `FakeRepository.Add` enforces unique constraints **eagerly** by throwing `InvalidOperationException` — setup-time violations are almost always test bugs and should fail loud at the offending call site, not at a deferred Result assertion further down. Use `SaveAsync` when you specifically want to test handler behavior on conflict (where the `Error.Conflict` Result is the system-under-test, not a setup mistake).

**DI prerequisites checklist.**

```csharp
services
    .AddTrellisBehaviors()                              // exception/tracing/logging/authorization/validation
    .AddTrellisFluentValidation(typeof(MyValidator).Assembly)
    .AddTrellisUnitOfWork<AppDbContext>()               // ⬅ registers TransactionalCommandBehavior
    .AddScoped<IOrderRepository, EfOrderRepository>();
```

Without a transaction behavior and matching `IUnitOfWork`, `repo.Add(order)` only stages
the entity. Tests against `FakeRepository` can hide that omission because `Add` is
immediately visible there. Wire `AddTrellisUnitOfWork<TContext>()` in the EF composition
root, not each handler. `AddInsideTransactionBehavior` also installs the transaction
behavior, but never supplies the store itself.

## Same-commit audit staging

**Problem.** Stage audit rows after a handler but before the same commit, without
changing its entity response. A normal outer behavior's post-handler code can run after
persistence; relying on manual registration order is fragile.

**Solution.** Use `AddInsideTransactionBehavior` or its builder slot. Keep the recorder
application-owned: the behavior captures its starting count, drains only records added
by that invocation, and cleans up in `finally` on every exit. Serial nested behaviors
use nested checkpoints; no framework-owned state frame is needed.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.ServiceDefaults;

public sealed partial class AuditEntryId : RequiredGuid<AuditEntryId>;

public sealed record AuditRecorded(AuditEntryId AuditEntryId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class AuditEntry : Aggregate<AuditEntryId>
{
    public OrderId OrderId { get; private set; } = default!;
    public string Action { get; private set; } = string.Empty;

    private AuditEntry(AuditEntryId id) : base(id) { }

    public static AuditEntry ForOrderCreated(OrderId orderId, TimeProvider clock)
    {
        var entry = new AuditEntry(AuditEntryId.NewUniqueV7(clock))
        {
            OrderId = orderId,
            Action = "order.created",
        };
        entry.DomainEvents.Add(new AuditRecorded(entry.Id, clock.GetUtcNow()));
        return entry;
    }
}

public interface IOrderAuditRecorder
{
    IReadOnlyList<AuditEntry> Recorded { get; }
    void Record(AuditEntry entry);
    IReadOnlyList<AuditEntry> TakeFrom(int checkpoint);
}

public sealed class OrderAuditRecorder : IOrderAuditRecorder
{
    private readonly List<AuditEntry> _recorded = [];
    public IReadOnlyList<AuditEntry> Recorded => _recorded;

    public void Record(AuditEntry entry) => _recorded.Add(entry);

    public IReadOnlyList<AuditEntry> TakeFrom(int checkpoint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(checkpoint);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checkpoint, _recorded.Count);
        if (checkpoint == _recorded.Count)
            return [];

        var records = _recorded.GetRange(checkpoint, _recorded.Count - checkpoint);
        _recorded.RemoveRange(checkpoint, _recorded.Count - checkpoint);
        return records;
    }
}

public interface IAuditedCommand;

public sealed record CreateAuditedOrderCommand(Money Total) : ICommand<Result<Order>>, IAuditedCommand;

public sealed class CreateAuditedOrderHandler(
    IOrderRepository orders, IOrderAuditRecorder recorder, TimeProvider clock)
    : ICommandHandler<CreateAuditedOrderCommand, Result<Order>>
{
    public ValueTask<Result<Order>> Handle(
        CreateAuditedOrderCommand command, CancellationToken cancellationToken) =>
        Order.TryCreate(command.Total)
            .Tap(orders.Add)
            .Tap(order => recorder.Record(AuditEntry.ForOrderCreated(order.Id, clock)))
            .AsValueTask();
}

public interface IAuditEntryRepository
{
    void Add(AuditEntry entry);
}

public sealed class EfAuditEntryRepository(DbContext context)
    : RepositoryBase<AuditEntry, AuditEntryId>(context), IAuditEntryRepository;

public sealed partial class RequiredAuditBehavior<TMessage, TResponse>(
    IOrderAuditRecorder recorder, IAuditEntryRepository audits, ILogger<RequiredAuditBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>, IAuditedCommand
    where TResponse : IResult, IFailureFactory<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var checkpoint = recorder.Recorded.Count;
        try
        {
            var response = await next(message, cancellationToken).ConfigureAwait(false);
            var mine = recorder.TakeFrom(checkpoint);
            if (!response.IsSuccess && response is not IPersistOnFailure { PersistOnFailure: true })
                return response;

            cancellationToken.ThrowIfCancellationRequested();
            if (mine.Count == 0)
            {
                var faultId = Guid.NewGuid().ToString("N");
                LogMissingAudit(logger, faultId);
                return TResponse.CreateFailure(new Error.Unexpected("audit.not-recorded", faultId)
                {
                    Detail = "An unexpected error occurred while processing the request.",
                });
            }

            foreach (var entry in mine)
                audits.Add(entry);

            return response;
        }
        finally
        {
            recorder.TakeFrom(checkpoint);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Order creation recorded no audit entry; commit refused. Fault {FaultId}.")]
    private static partial void LogMissingAudit(ILogger logger, string faultId);
}
```

Register the recorder as scoped. Both repositories must resolve the same concrete
context and stage only; they must not save independently:

```csharp
services.AddSingleton(TimeProvider.System);
services.AddScoped<IOrderAuditRecorder, OrderAuditRecorder>();
services.AddScoped<IOrderRepository>(provider =>
    new EfOrderRepository(provider.GetRequiredService<Recipe16DbContext>()));
services.AddScoped<IAuditEntryRepository>(provider =>
    new EfAuditEntryRepository(provider.GetRequiredService<Recipe16DbContext>()));
services.AddTrellis(options => options
    .UseInsideTransactionBehavior(typeof(RequiredAuditBehavior<,>))
    .UseEntityFrameworkUnitOfWork<Recipe16DbContext>());
```

Without the builder, register standard behaviors, use
`services.AddInsideTransactionBehavior(typeof(RequiredAuditBehavior<,>))`, and register
`AddTrellisUnitOfWork<Recipe16DbContext>()`. Either helper order preserves placement.
The open behavior's marker constraint selects matching commands; the typed
`AddInsideTransactionBehavior<TMessage,TResponse,TBehavior>()` overload selects one binding.
Multiple selected behaviors nest in binding order and unwind in reverse order.

**Outcome and cleanup contract.** The behavior inspects the actual response: success
and `IPersistOnFailure { PersistOnFailure: true }` may stage rows. Ordinary failure
returns unchanged; missing required data returns an ordinary failure **without**
persistence intent. `finally` removes this invocation's remaining records on success,
failure, exception, cancellation, or staging error while retaining the parent's prefix.
Handlers keep injecting the recorder interface and can be tested directly.

**Limits.** Placement does not provide permanent rejection or rollback. Propagate a
nested failure when its owner must not commit: if the owner ignores it, it can persist
the failed child's tracked changes. Discard the context after a rejected request.
Checkpoint cleanup removes recorder entries, not already staged EF entities. Commands
sharing one recorder/unit of work must execute serially; concurrent commands sharing a
`DbContext` are unsupported. Use separate DI scopes for independent concurrent work.

The application owns command-marker coverage, audit schemas and metadata, and whether
successful no-ops record entries. This example requires an entry for each successful
marked command. Unmarked commands and direct store writes are outside this policy.
Never commit, publish, or perform irreversible external effects from this behavior.
For post-commit events from behavior-added aggregates, select
`UseTrackedAggregateDomainEvents(...)`: response-shape dispatch discovers only returned
aggregates. Outbox capture sees behavior-added aggregates in the same owning save.

> **Compiled and behavior-pinned.** `Examples/CookbookSnippets/Recipe16_UnitOfWork.cs`
> contains the complete model, recorder, behavior, adapters, context, and registration.
> `Examples/CookbookSnippets.Tests/Recipe16AuditBehaviorTests.cs` uses real SQLite to
> prove same-save atomicity, rollback, nested checkpoints, outcome handling, and
> post-commit events. It also pins the explicit limitation that ignored inner failures
> do not permanently reject the owner. The outbox suite covers behavior-added aggregates.

## Testing handlers with an application-owned recorder

No transaction frame or fake unit of work is needed to test the handler:

```csharp
using FluentAssertions;
using Trellis.Testing;

var orders = new Orders();
var recorder = new OrderAuditRecorder();
var handler = new CreateAuditedOrderHandler(orders, recorder, TimeProvider.System);

var result = await handler.Handle(
    new CreateAuditedOrderCommand(new Money(10m)), CancellationToken.None);

orders.GetAll().Should().ContainSingle().Which.Should().BeSameAs(result.Unwrap());
recorder.Recorded.Should().ContainSingle().Which.OrderId.Should().Be(result.Unwrap().Id);

sealed class Orders : FakeRepository<Order, OrderId>, IOrderRepository;
```

An application may substitute its own recorder double through the same interface.
`Examples/CookbookSnippets.Tests/Recipe16AuditHandlerTests.cs` also invokes the real
behavior around a real handler without a container to verify staging, cleanup on all
exit paths, and preservation of original exceptions. Fake repositories are immediately
visible and do not prove transaction atomicity; keep the SQLite tests for that boundary.

To include the real `TransactionalCommandBehavior` in a database-free test, pass a
[`FakeUnitOfWork`](../trellis-api-testing-reference.md#fakeunitofwork) as its `IUnitOfWork`.
Configure `CommitResult` to exercise commit failure and compare `CommitCallCount`
with `CommitAttemptCount` to distinguish deferred nested calls from owner attempts.
The fake supplies no repository rollback or persistence atomicity.
