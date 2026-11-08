---
name: recipe-21-parallel-loads
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows Result.ParallelAsync and WhenAllAsync for independent loads in a handler. Use when loading from HTTP plus a database, or from factory-created DbContexts, in parallel."
---
# Recipe 21 — Parallel independent loads in handlers: `Result.ParallelAsync` + `WhenAllAsync`

**Requires:** `Trellis.Core`, plus `Trellis.Mediator`.

## Contents

- Problem
- What it shows
- When NOT to use it
- Anti-pattern → fix

**Problem.** A handler needs two (or more) loads that are *genuinely* independent — a customer record from one upstream service, a product record from another; an HTTP call to authn plus a DB read for profile; or two reads against two distinct EF Core `DbContext` instances. Awaiting each load before starting the next serializes their latency. Starting both before awaiting either allows concurrency. `Task.WhenAll` preserves their `Result<T>` values but does not combine them into one result; the tuple `.WhenAllAsync()` extension supplies that fold.

`Result.ParallelAsync(...)` is the framework's opinionated entry point: factory-takes-no-args, eagerly invokes each factory so both tasks actually run concurrently, returns a tuple of `Task<Result<T>>` that the matching `.WhenAllAsync()` extension awaits with `Task.WhenAll` and folds via `Result.Combine` into a single `Result<(T1, T2, …)>`. Failures combine through `Error.Combine`, so two `Error.InvalidInput` failures merge their fields, heterogeneous failures become an `Error.Aggregate`.

> 🛑 **Danger — `Result.ParallelAsync` against repositories that share a `DbContext` instance will race and throw.** The hard rule, independent of DI: **never start a second operation on a `DbContext` before the first one has completed.** EF Core's `DbContext` is documented as not thread-safe; the second concurrent operation throws `InvalidOperationException: A second operation was started on this context instance before a previous operation completed.` The typical Trellis EF wiring triggers this case by construction: `services.AddDbContext<TContext>(...)` registers the context with its default scoped lifetime (this is the default of the parameterless overload — `AddDbContext<TContext>(..., ServiceLifetime)` can override it), and `services.AddTrellisUnitOfWork<TContext>()` registers `IUnitOfWork` as scoped and consumes the scoped `TContext`. So **every repository resolved from the same request scope shares one `DbContext` instance**, and parallelising two repository reads parallelises two operations on that one context. Keep them sequential — see "When NOT to use it" below. `Result.ParallelAsync` is for genuinely cross-resource concurrency (HTTP + DB, two distinct upstream services, factory-created independent contexts), not for two repository reads against the same store.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Trellis;

public sealed record CheckoutCommand(UserId UserId, ProductSku Sku) : ICommand<Result<CheckoutQuote>>;

// Genuinely cross-resource: one upstream HTTP call + one read against a DIFFERENT
// store (a pricing cache). These have no shared state and run safely in parallel.
public sealed class CheckoutHandler(
    IUserDirectoryClient directory,   // outbound HTTP
    IPricingCache pricing)            // independent in-memory store
    : ICommandHandler<CheckoutCommand, Result<CheckoutQuote>>
{
    public ValueTask<Result<CheckoutQuote>> Handle(CheckoutCommand command, CancellationToken cancellationToken) =>
        new(Result.ParallelAsync(
                //  ↑ takes parameterless factory funcs — NOT pre-started tasks.
                //    Each factory is invoked eagerly here so both fetches execute concurrently.
                () => directory.FindUserAsync(command.UserId, cancellationToken),
                () => pricing.GetQuoteAsync(command.Sku, cancellationToken))
            .WhenAllAsync()
            //  ↑ awaits Task.WhenAll, folds the two Result<T> into Result<(User, Quote)>
            //    via Result.Combine. Two Error.InvalidInput failures merge their
            //    Fields/Rules; heterogeneous errors flatten into Error.Aggregate.
            .BindAsync(t => CheckoutQuote.Create(t.Item1, t.Item2, command.Sku)));
}
```

**What it shows.**

- `Result.ParallelAsync` takes `Func<Task<Result<T>>>` factories, NOT `Task<Result<T>>` instances. It invokes each factory without awaiting the returned tasks. Already-started tasks can also run concurrently: `(LoadUserAsync(), LoadQuoteAsync()).WhenAllAsync()` is supported. Both shapes invoke the synchronous portions of the operations in order; neither makes blocking work parallel.
- `.WhenAllAsync()` on the tuple is the matching extension. Without it you still have a tuple of `Task<Result<T>>` — which isn't awaitable on its own; you'd have to await each task individually and combine the results by hand. `.WhenAllAsync()` is the one-line fold.
- The combined `Result<(T1, T2)>` flows back into the standard ROP chain (`BindAsync`, `MapAsync`, `TapAsync`) — no `match` / `if (success)` branches.
- `Result.ParallelAsync` ships overloads for 2–9 factories. For dynamic collections, `TraverseAsync` is a **sequential, fail-fast alternative**, not parallel fan-in; `TraverseAllAsync` is also sequential but accumulates failures ([Recipe 20](recipe-20-fail-fast-vs-accumulating.md)). If collection loads need concurrency, use an explicitly bounded concurrency design over independent resources and deliberately choose failure aggregation and cancellation behavior.
- Task faults and cancellation propagate from `.WhenAllAsync()` after all supplied tasks complete; they are not converted to Result failures. A factory that throws synchronously escapes from `ParallelAsync` immediately, so later factories are not invoked.

**When NOT to use it.**

1. **Two or more repositories sharing the same scoped `DbContext`.** The most common case in a typical Trellis service. The repos look independent at the C# level, but they all derive from `RepositoryBase<TAggregate, TId>` over the same scoped `TContext`. Parallelising them races the underlying context and throws `InvalidOperationException`. **Keep them sequential with `BindZipAsync`** (it awaits the first, runs the second only on success, and zips both into a tuple — short-circuiting on failure). This is a correctness requirement regardless of database latency. Use independent contexts only when their consistency and unit-of-work boundaries fit the operation.
2. **The second factory's body references a value produced by the first.** Not independent — keep the sequential `BindAsync` chain. The rule is mechanical: if the second load requires data the first one produced (an id, a filter, a cursor), the two are sequential by definition.
3. **Side-effecting writes.** `Result.ParallelAsync` is for reads. Parallel `repository.Add(...)` calls against a shared context have the same race as parallel reads, plus tracker contention; parallel writes against per-scope contexts need transaction coordination outside this helper.

**Anti-pattern → fix.**

```csharp
// ❌ UNSAFE — two repository calls against repositories that share a scoped DbContext.
// Looks "obviously parallelisable" but races the underlying EF context. `Task.WhenAll`
// waits for both returned tasks to complete before surfacing a task fault. The failure mode
// is an `InvalidOperationException("A second operation was started on this context...")`
// thrown by EF Core when the second concurrent operation hits the shared connection.
// Reproduction is timing-dependent: the throw is reliable under contention but can be
// missed on a near-instant warm cache, which lulls authors into thinking it's correct.
public ValueTask<Result<DraftOrderId>> Handle(CreateDraftOrderCommand command, CancellationToken cancellationToken) =>
    new(Result.ParallelAsync(
            () => _customers.FindByIdAsync(command.CustomerId, cancellationToken),  // shared DbContext
            () => _products.FindByIdAsync(command.ProductId, cancellationToken))    // shared DbContext
        .WhenAllAsync()
        .BindAsync(t => DraftOrder.CreateDraft(t.Item1, t.Item2, command.Quantity))
        .TapAsync(_orders.Add)
        .MapAsync(o => o.Id));

// ✅ Sequential against a shared DbContext — correct by construction, and fluent.
// `BindZipAsync` awaits the first read, runs the second ONLY if the first succeeded
// (short-circuits), and zips both into `Result<(Customer, Product)>`. The two reads
// never overlap, so the shared context is never raced. Latency = the sum of two
// reads; correctness does not depend on those reads being fast.
public ValueTask<Result<DraftOrderId>> Handle(CreateDraftOrderCommand command, CancellationToken cancellationToken) =>
    new(_customers.FindByIdAsync(command.CustomerId, cancellationToken)
        .BindZipAsync(_ => _products.FindByIdAsync(command.ProductId, cancellationToken))
        .BindAsync((customer, product) => DraftOrder.CreateDraft(customer, product, command.Quantity))
        .TapAsync(_orders.Add)
        .MapAsync(o => o.Id));

// Alternative — eager await + Result.Combine. Also sequential and safe, but it does
// NOT short-circuit: the second read runs even when the first already failed. Prefer
// this only when you deliberately want to ACCUMULATE both failures — Combine merges
// their errors (e.g. report "customer not found" AND "product not found" together).
public async ValueTask<Result<DraftOrderId>> HandleAccumulating(CreateDraftOrderCommand command, CancellationToken cancellationToken)
{
    var customerResult = await _customers.FindByIdAsync(command.CustomerId, cancellationToken);
    var productResult  = await _products.FindByIdAsync(command.ProductId, cancellationToken);

    return Result.Combine(customerResult, productResult)
        .Bind(t => DraftOrder.CreateDraft(t.Item1, t.Item2, command.Quantity))
        .Tap(_orders.Add)
        .Map(o => o.Id);
}

// ✅ Truly parallel — only when the two loads hit independent resources. The HTTP
// call and the in-memory cache have no shared state; ParallelAsync is safe here.
//
// For two EF reads in genuine parallel, inject `IDbContextFactory<TContext>` and
// open two short-lived contexts inside the handler:
//
//   await using var aCtx = await _factory.CreateDbContextAsync(ct);
//   await using var bCtx = await _factory.CreateDbContextAsync(ct);
//   // ... use aCtx and bCtx independently inside ParallelAsync factories ...
//
// The trade-off: two extra contexts and their connections per request, in exchange
// for parallel reads. Worth it only when the queries are slow.
```
