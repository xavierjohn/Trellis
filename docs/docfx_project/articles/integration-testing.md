---
title: Integration Testing
package: Trellis.Testing
topics: [testing, assertions, result, maybe, fakerepository, webfactory, actor-headers]
related_api_reference: [trellis-api-testing-reference.md, trellis-api-testing-aspnetcore.md, trellis-api-testing-worker.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Integration Testing

`Trellis.Testing` and `Trellis.Testing.AspNetCore` keep tests aligned with production code: success and failure stay typed (`Result<T>`, `Maybe<T>`, closed `Error` ADT), authorization stays explicit, and ASP.NET Core integration tests run against the real pipeline.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Assert a sync `Result<T>` succeeded / failed | `result.Should().BeSuccess()` / `.BeFailureOfType<TError>()` | [Result assertions](#result-assertions) |
| Assert an async `Result<T>` succeeded / failed | `await task.BeSuccessAsync()` / `.BeFailureAsync()` (extension on `Task`/`ValueTask`) | [Result assertions](#result-assertions) |
| Assert a `Maybe<T>` carries / is empty | `maybe.Should().HaveValue()` / `.BeNone()` | [Maybe assertions](#maybe-assertions) |
| Assert error code / detail / payload type | `error.Should().HaveCode(...)` / `.HaveDetailContaining(...)` / `.BeOfType<TError>()` | [Error assertions](#error-assertions) |
| Assert a validation field violation | `unprocessable.Should().HaveFieldError(...)` | [Validation error assertions](#validation-error-assertions) |
| Stub a repository with unique-constraint and not-found semantics | `new FakeRepository<TAggregate, TId>().WithUniqueConstraint(...)` | [FakeRepository](#fakerepository) |
| Provide an actor inside a unit test | `new TestActorProvider(id, perms)` / `.WithActor(...)` scope | [TestActorProvider](#testactorprovider) |
| Send `X-Test-Actor` from an integration test client | `factory.CreateClientWithActor(...)` | [WebApplicationFactory helpers](#webapplicationfactory-helpers) |
| Replace EF provider in `WebApplicationFactory` | `services.ReplaceDbProvider<TContext>(...)` | [WebApplicationFactory helpers](#webapplicationfactory-helpers) |
| Replace a resource loader for tests | `services.ReplaceResourceLoader<TMessage, TResource>(...)` | [WebApplicationFactory helpers](#webapplicationfactory-helpers) |
| Pin time deterministically in integration tests | `factory.WithFakeTimeProvider(out var fake)` | [WebApplicationFactory helpers](#webapplicationfactory-helpers) |
| Replay a `.http` file against the test host | `HttpFileParser.ParseFile` + `HttpFileRunner.RunAsync` + `HttpFileAssertions.AssertExpectationsMet` | [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md#http-file-replay-helpers) |
| Acquire a real Entra token in gated E2E tests | `MsalTestTokenProvider` + `factory.CreateClientWithEntraTokenAsync(...)` | [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md#msal--entra-e2e-token-helpers) |
| Test a `BackgroundService` with deterministic time + event capture | `WorkerHarness<TWorker>.CreateAsync(...)` + `Time.Advance(...)` + `WaitForEventAsync<TEvent>(...)` | [Background workers](#background-workers) |

## Use this guide when

- You are writing handler / domain unit tests that consume Trellis `Result<T>`, `Maybe<T>`, or the closed `Error` ADT.
- You are writing ASP.NET Core integration tests with `WebApplicationFactory<TEntryPoint>` and need actor headers, DI replacement, deterministic time, or `.http` replay.
- You want fakes (`FakeRepository<,>`, `TestActorProvider`) instead of hand-rolled mocks for repositories and authorization.

## Surface at a glance

| Type | Package | Purpose |
|---|---|---|
| `ResultAssertions<TValue>` | `Trellis.Testing` | FluentAssertions surface for sync `Result<T>` (success, failure, error code/detail). |
| `ResultAssertionsAsyncExtensions` | `Trellis.Testing` | `BeSuccessAsync` / `BeFailureAsync` / `BeFailureOfTypeAsync` on `Task<Result<T>>` and `ValueTask<Result<T>>`. |
| `MaybeAssertions<T>` | `Trellis.Testing` | `HaveValue` / `BeNone` / `HaveValueEqualTo` / `HaveValueMatching`. |
| `ErrorAssertions` | `Trellis.Testing` | `HaveCode` / `HaveDetail(Containing)` / `BeOfType<TError>` against the closed `Error` ADT. |
| `ValidationErrorAssertions` | `Trellis.Testing` | Field-shape assertions over `Error.InvalidInput`. |
| `UnwrapExtensions` | `Trellis.Testing` | Test-only `Unwrap()` / `UnwrapError()` (sync, async, `Result<Unit>`, `Maybe<T>`). |
| `FakeRepository<TAggregate, TId>` | `Trellis.Testing` | In-memory repository with unique-constraint, not-found, and domain-event capture. |
| `FakeSharedResourceLoader<TResource, TId>` | `Trellis.Testing` | Test double over `SharedResourceLoaderById<TResource, TId>` backed by `FakeRepository`. |
| `TestActorProvider` / `TestActorScope` | `Trellis.Testing` | `IActorProvider` for handler tests, with scoped `WithActor(...)` overrides. |
| `AggregateTestMutator` | `Trellis.Testing` | `SetMaybeField` / `ClearMaybeField` for whitebox aggregate setup. |
| `WebApplicationFactoryExtensions` | `Trellis.Testing.AspNetCore` | `CreateClientWithActor(...)`, `CreateClientWithEntraTokenAsync(...)`. |
| `WebApplicationFactoryTimeExtensions` | `Trellis.Testing.AspNetCore` | `WithFakeTimeProvider(...)` and `DefaultTestStartInstant` (`2024-01-01T00:00:00Z`). |
| `ServiceCollectionExtensions` | `Trellis.Testing.AspNetCore` | `ReplaceResourceLoader<,>` / `ReplaceSingleton<T>`. |
| `ServiceCollectionDbProviderExtensions` | `Trellis.Testing.AspNetCore` | `ReplaceDbProvider<TContext>(...)` for SQLite/in-memory swaps. |
| `MsalTestTokenProvider` (+ `MsalTestOptions`, `TestUserCredentials`) | `Trellis.Testing.AspNetCore` | MSAL ROPC token acquisition for gated E2E tests against a dedicated test tenant. |
| `HttpFileParser` / `HttpFileRunner` / `HttpFileAssertions` | `Trellis.Testing.AspNetCore.Http` | Parse, run, and assert `.http` files against a `WebApplicationFactory` client. |
| `WorkerHarness<TWorker>` (+ `WorkerHarnessOptions`, `IWorkerTickSignal`, `WorkerHarnessTimeoutException`) | `Trellis.Testing.Worker` | Integration-test harness for `BackgroundService` workers: `FakeTimeProvider`, `TestActorProvider`, domain-event capture, race-proof `WaitForEventAsync` / `WaitForTickAsync`. |

Full signatures: [trellis-api-testing-reference.md](../api_reference/trellis-api-testing-reference.md), [trellis-api-testing-aspnetcore.md](../api_reference/trellis-api-testing-aspnetcore.md), [trellis-api-testing-worker.md](../api_reference/trellis-api-testing-worker.md).

## Installation

```bash
dotnet add package Trellis.Testing
dotnet add package Trellis.Testing.AspNetCore
dotnet add package Trellis.Testing.Worker
```

`Trellis.Testing.AspNetCore` and `Trellis.Testing.Worker` already reference `Trellis.Testing` transitively. Install `Trellis.Testing.AspNetCore` when the project owns ASP.NET Core integration tests, and `Trellis.Testing.Worker` when it has integration tests for `BackgroundService` workers.

## Quick start

A handler test that asserts both the success path and an expected typed failure, using `FakeRepository` for persistence and `TestActorProvider` for authorization context.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Trellis.Authorization;
using Trellis.Testing;

public sealed record OrderId(Guid Value);

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id, string email) : base(id) => Email = email;

    public string Email { get; }
}

var actorProvider = new TestActorProvider("user-1", "Orders.Read", "Orders.Write");
var actor = (await actorProvider.GetCurrentActorAsync()).GetValueOrThrow();

actor.HasPermission("Orders.Read").Should().BeTrue();

var repo = new FakeRepository<Order, OrderId>()
    .WithUniqueConstraint(order => order.Email);

var order = new Order(new OrderId(Guid.NewGuid()), "ada@example.com");

(await repo.SaveAsync(order, CancellationToken.None)).Should().BeSuccess();
(await repo.GetByIdAsync(order.Id)).Should().BeSuccess().Which.Should().BeSameAs(order);

var duplicate = new Order(new OrderId(Guid.NewGuid()), "ada@example.com");
(await repo.SaveAsync(duplicate, CancellationToken.None))
    .Should().BeFailureOfType<Error.Conflict>();
```

## Result assertions

Every command returns `Result<Unit>`. Assert on results directly instead of unpacking booleans by hand.

| Assertion | Receiver | Notes |
|---|---|---|
| `BeSuccess()` | `Result<T>` via `.Should()` | `AndWhich.Which` exposes the value. |
| `BeFailure()` | `Result<T>` via `.Should()` | `AndWhich.Which` exposes the `Error`. |
| `BeFailureOfType<TError>()` | `Result<T>` via `.Should()` | Asserts the closed-ADT case (e.g. `Error.NotFound`). |
| `HaveValue(expected)` / `HaveValueMatching(predicate)` / `HaveValueEquivalentTo(expected)` | `Result<T>` via `.Should()` | Value-shape checks without `.Which`. |
| `HaveErrorCode(code)` / `HaveErrorDetail(text)` / `HaveErrorDetailContaining(substring)` | `Result<T>` via `.Should()` | Error-shape checks. |
| `BeSuccessAsync()` / `BeFailureAsync()` / `BeFailureOfTypeAsync<TError>()` | `Task<Result<T>>` **or** `ValueTask<Result<T>>` directly | **Not** an extension on `ResultAssertions<T>` — see warning below. |

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var success = Result.Ok(42);
success.Should().BeSuccess().Which.Should().Be(42);

var notFound = Result.Fail<int>(
    new Error.NotFound(ResourceRef.For("Order", "123")) { Detail = "Order 123 not found" });

notFound.Should().BeFailureOfType<Error.NotFound>();
notFound.Should().HaveErrorCode("not.found.error");
notFound.Should().HaveErrorDetailContaining("Order 123");

Task<Result<int>> taskResult = Task.FromResult(success);
ValueTask<Result<int>> valueTaskResult = ValueTask.FromResult(Result.Ok(7));

(await taskResult.BeSuccessAsync()).Which.Should().Be(42);
(await valueTaskResult.BeSuccessAsync()).Which.Should().Be(7);
```

> [!WARNING]
> Do **not** write `await result.Should().BeSuccessAsync()`. Async assertions are extensions on `Task<Result<T>>` and `ValueTask<Result<T>>`, not on `ResultAssertions<T>`. Call them directly on the awaitable.

## Maybe assertions

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

Maybe.From("Ada").Should().HaveValue().Which.Should().Be("Ada");
Maybe<string>.None.Should().BeNone();
Maybe.From("Ada").Should().HaveValueEqualTo("Ada");
```

`HaveValueMatching(predicate)` and `HaveValueEquivalentTo(expected)` are also available; full signatures in [trellis-api-testing-reference.md](../api_reference/trellis-api-testing-reference.md#maybeassertionst).

## Error assertions

`Error` is a closed ADT (see [`trellis-api-core.md`](../api_reference/trellis-api-core.md)). Assert on the case, not on a string discriminator.

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

Error error = new Error.NotFound(ResourceRef.For("Order", "123"))
{
    Detail = "Order 123 not found",
};

error.Should().BeOfType<Error.NotFound>()
    .Which.Resource.Should().Be(ResourceRef.For("Order", "123"));

error.Should().HaveCode("not.found.error");
error.Should().HaveDetailContaining("123");
```

> [!NOTE]
> `Error.Instance` was removed in v3. The ASP wire layer populates `ProblemDetails.Instance` from the server-relative request path+query, and typed payloads expose `ResourceRef` (e.g. `Error.NotFound.Resource`) directly. Assert against the typed `ResourceRef` field, not a string `Instance` on the `Error`.

### Validation error assertions

`Error.InvalidInput` carries an `EquatableArray<FieldViolation>`. Use the dedicated assertions instead of pattern-matching the array yourself.

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var error = new Error.InvalidInput(EquatableArray.Create(
    new FieldViolation(InputPointer.ForProperty("email"), "validation.error")
    {
        Detail = "Email is required.",
    }));

error.Should().HaveFieldError("email");
error.Should().HaveFieldErrorWithDetail("email", "Email is required.");
error.Should().HaveFieldCount(1);
```

`HaveFieldError` accepts either `"email"` or `"/email"` — the field name is normalized via `InputPointer.ForProperty`.

## FakeRepository

`FakeRepository<TAggregate, TId>` mirrors the production `IRepository` contract and adds a result-shape surface for tests that need to assert on persistence failures. It is almost always preferable to a hand-rolled mock.

| Surface | Members | Use from |
|---|---|---|
| Setup (mirrors `RepositoryBase<TAggregate, TId>`) | `Add(TAggregate)`, `Remove(TAggregate)`, `RemoveByIdAsync(TId, ct)` | Production handlers; test setup that exercises the real `IRepository` contract. |
| Result-shape (fake-only) | `SaveAsync(TAggregate, ct)`, `DeleteAsync(TId, ct)` — both `Task<Result<Unit>>` | Tests that explicitly assert on conflict-result or not-found-result handling. |
| Read | `GetByIdAsync(TId, ct)` → `Task<Result<TAggregate>>`, `FindByIdAsync(TId, ct)` → `Task<Maybe<TAggregate>>`, `FindAsync(predicate)`, `WhereAsync(predicate)`, `WhereAsync(Specification<TAggregate>)` | Test bodies. |
| Inspection | `Count`, `Exists(TId)`, `Get(TId)`, `GetAll()`, `Clear()`, `PublishedEvents` | Test assertions. |
| Constraint registration | `WithUniqueConstraint(Func<TAggregate, object?>)` | Test setup; eagerly enforced by `Add` (throws `InvalidOperationException`) and at-call by `SaveAsync` (returns `Error.Conflict`). |

Detail strings:

- `GetByIdAsync` / `DeleteAsync` / `RemoveByIdAsync` not-found: `"{AggregateTypeName} with ID '{id}' not found."`.
- Unique-constraint conflict from `SaveAsync`: `"A {AggregateTypeName} with the same value already exists."`

```csharp
using System;
using System.Threading;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

public sealed record OrderId(Guid Value);

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id, string email) : base(id) => Email = email;
    public string Email { get; }
}

var repo = new FakeRepository<Order, OrderId>()
    .WithUniqueConstraint(order => order.Email);

var order = new Order(new OrderId(Guid.NewGuid()), "ada@example.com");

(await repo.SaveAsync(order, CancellationToken.None)).Should().BeSuccess();
(await repo.GetByIdAsync(order.Id)).Should().BeSuccess().Which.Should().BeSameAs(order);
repo.Exists(order.Id).Should().BeTrue();
repo.Count.Should().Be(1);

var dupe = new Order(new OrderId(Guid.NewGuid()), "ada@example.com");
(await repo.SaveAsync(dupe, CancellationToken.None))
    .Should().BeFailureOfType<Error.Conflict>();
```

> [!NOTE]
> `SaveAsync` / `DeleteAsync` are not on `RepositoryBase<TAggregate, TId>`. If a handler accepts the `IRepository` contract, it cannot call them; use the staging API (`Add` / `Remove` / `RemoveByIdAsync`) instead. See cookbook **Recipe 16 — Unit of work in handlers**.

## TestActorProvider

`TestActorProvider` is an `IActorProvider` for unit tests. Use the `(string userId, params string[] permissions)` constructor for the simple case and the `(Actor)` constructor when the test needs `ForbiddenPermissions` or `Attributes`.

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Trellis.Authorization;
using Trellis.Testing;

var provider = new TestActorProvider("admin", "Orders.Read", "Orders.Write");

await using (provider.WithActor("user-1", "Orders.Read"))
{
    var actor = (await provider.GetCurrentActorAsync()).GetValueOrThrow();
    actor.Id.Should().Be("user-1");
    actor.HasPermission("Orders.Read").Should().BeTrue();
    actor.HasPermission("Orders.Write").Should().BeFalse();
}

var richActor = new Actor(
    id: "user-2",
    permissions: new HashSet<string> { "Orders.Read" },
    forbiddenPermissions: new HashSet<string> { "Orders.Delete" },
    attributes: new Dictionary<string, string> { ["tenant"] = "acme" });

await using (provider.WithActor(richActor))
{
    var actor = (await provider.GetCurrentActorAsync()).GetValueOrThrow();
    actor.Attributes["tenant"].Should().Be("acme");
}
```

`WithActor` returns a `TestActorScope` (`IAsyncDisposable`/`IDisposable`) that restores the previous actor when disposed.

## WebApplicationFactory helpers

The ASP.NET Core integration helpers live in `Trellis.Testing.AspNetCore`. Configure DI through `ConfigureTestServices` **before** `CreateClient()`; do not mutate the service collection afterward.

### Authenticated test clients

`CreateClientWithActor` writes the `X-Test-Actor` header that the development/test actor provider expects (id, permissions, forbidden permissions, attributes).

```csharp
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Authorization;
using Trellis.Testing.AspNetCore;

WebApplicationFactory<Program> factory = default!;

var simpleClient = factory.CreateClientWithActor("user-1", "Orders.Read", "Orders.Write");

var richActor = new Actor(
    id: "user-2",
    permissions: new HashSet<string> { "Orders.Read" },
    forbiddenPermissions: new HashSet<string> { "Orders.Delete" },
    attributes: new Dictionary<string, string> { ["tenant"] = "acme" });

var richClient = factory.CreateClientWithActor(richActor);
```

For real Entra tokens in gated E2E suites, use `MsalTestTokenProvider` plus `factory.CreateClientWithEntraTokenAsync(provider, testUserName, ct)`. Full surface: [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md#msaltesttokenprovider).

### Replacing services

| Helper | Effect |
|---|---|
| `services.ReplaceSingleton<TService>(instance)` | Removes all `TService` registrations and adds the supplied singleton. |
| `services.ReplaceResourceLoader<TMessage, TResource>(factory)` | Removes existing `IResourceLoader<TMessage, TResource>` registrations and adds the supplied scoped factory. |
| `services.ReplaceDbProvider<TContext>(configureOptions)` | Removes the existing `TContext`, `DbContextOptions<TContext>`, and EF Core provider-scoped services, then re-registers via `AddDbContext<TContext>(configureOptions)`. |

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Testing.AspNetCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options) { }

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureTestServices(services =>
            services.ReplaceDbProvider<AppDbContext>(options => options.UseSqlite(_connection)));
    }

    protected override void Dispose(bool disposing)
    {
        _connection.Dispose();
        base.Dispose(disposing);
    }
}
```

> [!WARNING]
> `ReplaceDbProvider<TContext>` re-registers the context via `AddDbContext<TContext>`. If your app uses `AddDbContextFactory` or `AddPooledDbContextFactory`, replace those registrations directly instead.

> [!NOTE]
> SQLite cannot translate `DateTimeOffset` in `ORDER BY` clauses. If your aggregate queries sort on inherited audit columns (`CreatedAt`, `LastModified`), register an Acl layer `ValueConverter` per the canonical pattern in [Provider-specific column mapping](../api_reference/trellis-api-efcore.md#provider-specific-column-mapping). Without it, integration tests using the SQLite swap above will fail with a `DateTimeOffset` translation error at query time.

### Deterministic time

`WithFakeTimeProvider` registers a `FakeTimeProvider` as the singleton `TimeProvider`. The `out` overloads default to `WebApplicationFactoryTimeExtensions.DefaultTestStartInstant` (`2024-01-01T00:00:00Z`).

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Trellis.Testing.AspNetCore;

WebApplicationFactory<Program> factory = default!;

factory = factory.WithFakeTimeProvider(out var fakeTime);
fakeTime.Advance(System.TimeSpan.FromDays(3));
```

## Composition

Once a handler returns `Result<T>` (or a command returns `Result<Unit>`), the testing surface composes naturally with the rest of Trellis: chain `Bind`/`Map`/`Ensure` on production code, then assert on the terminal result with `BeSuccess` / `BeFailureOfType<TError>`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Trellis.Authorization;
using Trellis.Testing;

public sealed record OrderId(Guid Value);

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id, string email) : base(id) => Email = email;
    public string Email { get; }
}

public sealed record PlaceOrder(string Email);

public sealed class PlaceOrderHandler(FakeRepository<Order, OrderId> repo, TestActorProvider actors)
{
    public async Task<Result<OrderId>> HandleAsync(PlaceOrder cmd, CancellationToken ct)
    {
        var maybeActor = await actors.GetCurrentActorAsync(ct);
        if (!maybeActor.TryGetValue(out var actor))
            return Result.Fail<OrderId>(new Error.AuthenticationRequired { Detail = "Authentication required." });
        if (!actor.HasPermission("Orders.Write"))
            return Result.Fail<OrderId>(new Error.Forbidden("Orders.Write"));

        var order = new Order(new OrderId(Guid.NewGuid()), cmd.Email);
        var save = await repo.SaveAsync(order, ct);
        return save.Map(_ => order.Id);
    }
}

var repo = new FakeRepository<Order, OrderId>().WithUniqueConstraint(o => o.Email);
var actors = new TestActorProvider("user-1", "Orders.Write");
var handler = new PlaceOrderHandler(repo, actors);

(await handler.HandleAsync(new PlaceOrder("ada@example.com"), CancellationToken.None))
    .Should().BeSuccess();

(await handler.HandleAsync(new PlaceOrder("ada@example.com"), CancellationToken.None))
    .Should().BeFailureOfType<Error.Conflict>();
```

## Background workers

`Trellis.Testing.Worker` builds an `IHost` around a `BackgroundService`, wires a `FakeTimeProvider`, registers a `TestActorProvider` for the worker's `IActorProvider`, and inserts an open-generic capture handler into the mediator's `IDomainEventHandler<>` pipeline. Tests advance the clock, then await the first domain event or a named tick signal.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator;
using Trellis.Testing.Worker;

await using var harness = await WorkerHarness<SubscriptionRenewalWorker>.CreateAsync(opts =>
{
    opts.SystemActor = Actor.Create(
        "subscription-renewal-worker",
        new HashSet<string>(["Subscriptions.Read", "Subscriptions.Write"]));

    opts.ConfigureServices(services =>
    {
        services.AddDomainEventDispatch();
        services.AddDbContext<RemindersDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<ISubscriptionsRepository, SubscriptionsRepository>();
    });

    opts.SeedAsync((sp, ct) =>
        sp.GetRequiredService<RemindersDbContext>().SeedTestSubscriptionsAsync(ct));
});

await harness.StartAsync(TestContext.Current.CancellationToken);
await harness.SettleAsync(cancellationToken: TestContext.Current.CancellationToken);

harness.Time.Advance(TimeSpan.FromHours(24));

var reminder = await harness.WaitForEventAsync<RenewalReminderQueued>(
    TimeSpan.FromSeconds(5),
    TestContext.Current.CancellationToken);

reminder.SubscriptionId.Should().Be(expectedId);
harness.Events<RenewalReminderQueued>().Should().HaveCount(1);
```

Key rules:

- The harness owns `AddHostedService<TWorker>()`. Adding the worker again inside `ConfigureServices` fails fast — the harness throws `InvalidOperationException` at `CreateAsync` so a test never runs two copies of the worker.
- The harness does **not** call `AddDomainEventDispatch()` for you. Worker tests are integration tests of the production composition root; include the same mediator-dispatch registration the worker's production host uses inside `ConfigureServices`. Without it, `WaitForEventAsync` times out with a diagnostic message that calls this out.
- Wait timeouts run on real time. `Time.Advance(...)` drives the worker's `Task.Delay` / `PeriodicTimer` continuations; it does not consume the wait budget.
- `AutoStart` defaults to `false`. Call `await harness.StartAsync(ct)` after configuring any post-build wait subscriptions; flip `opts.AutoStart = true` when the test does not need to subscribe ahead of the first tick.
- **Avoid the startup race.** `IHost.StartAsync` returns once the worker's `ExecuteAsync` has been scheduled — not after the worker has registered its first `Task.Delay` / `PeriodicTimer` callback with the `FakeTimeProvider`. A test that calls `await harness.StartAsync(); harness.Time.Advance(period);` back-to-back can race the worker and time out. Either call `await harness.SettleAsync(cancellationToken: ct)` (real-time yield, defaults to 200ms) after `StartAsync`, or — for deterministic tests — have the worker REGISTER its first `Task.Delay(period, time, ct)` before calling `IWorkerTickSignal.SignalAsync("ready", ct)`, then `await` the saved delay, and use `await harness.WaitForTickAsync("ready", cancellationToken: ct)` in the test. The `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` overload eagerly calls `timeProvider.CreateTimer(...)`, so signaling AFTER the `Task.Delay(...)` call (not before) is what guarantees the callback is observable to a subsequent `Time.Advance`.
- When the worker has no domain-visible outcome (a no-op probe, an infrastructure-only tick), resolve the optional `IWorkerTickSignal` from DI and call `SignalAsync(name, ct)` at the end of each tick. Tests then call `await harness.WaitForTickAsync(name, timeout, ct)`. Prefer `WaitForEventAsync<TEvent>` when the tick already emits a domain event.
- **Waiting for successive ticks of the same name.** `WaitForTickAsync(name, ...)` returns immediately if a matching tick is anywhere in the captured history — that's intended for the deterministic-ready pattern (the worker signals once at startup, the test waits once). For workers that emit the same tick name on every iteration, capture a global index and pass it as `after:` so the next wait blocks until a **new** tick arrives:

  ```csharp
  var first = await harness.WaitForTickAsync("probe", cancellationToken: ct);
  harness.Time.Advance(TimeSpan.FromMinutes(1));
  var second = await harness.WaitForTickAsync("probe", after: first, cancellationToken: ct);
  ```

  When you need a baseline cursor before the first wait, call `harness.LastTickIndexOf("probe")` (returns the global index of the most recent matching tick, or `-1` if none) — this is correct even when other tick names are interleaved in the history. For the unnamed overload use `harness.TickCount - 1` (which becomes `-1` when no signals have arrived yet, matching the documented "no cursor" sentinel); passing `TickCount` directly would skip the next signal, because `after:` is an exclusive lower bound on the global signal index. Do **not** use `harness.TickCountOf(name) - 1` as an `after:` cursor: `TickCountOf` is a per-name count, not a global index, so with interleaved names it can fall below the global index of an already-recorded matching tick and the wait would return immediately for that old tick. `TickCountOf` is still appropriate for assertions such as `harness.TickCountOf("probe").Should().Be(3)`.

Full surface: [`trellis-api-testing-worker.md`](../api_reference/trellis-api-testing-worker.md).

## Practical guidance

- **Prefer fakes over mocks for repositories.** `FakeRepository` reproduces real not-found behavior, unique constraints, and domain-event capture; mocks usually drift from the real `IRepository` contract.
- **Pick the right repository surface.** Production handlers should consume the staging API (`Add` / `Remove` / `RemoveByIdAsync`). The fake-only `SaveAsync` / `DeleteAsync` (both `Task<Result<Unit>>`) exist so tests can assert on conflict / not-found result shapes.
- **Assert on error case, not error string.** Use `BeFailureOfType<TError>()` against the closed `Error` ADT; the typed payload (`Error.NotFound.Resource`, `Error.Conflict.ReasonCode`, etc.) carries the meaningful state.
- **Async assertions live on the awaitable.** `BeSuccessAsync` / `BeFailureAsync` / `BeFailureOfTypeAsync` are extensions on `Task<Result<T>>` and `ValueTask<Result<T>>` — not on `ResultAssertions<T>`. `Unwrap()` / `UnwrapError()` are test-only; never copy them into production code.
- **Use the rich `Actor` overload when authorization is non-trivial.** When a policy reads `ForbiddenPermissions` or `Attributes`, pass a full `Actor` to `TestActorProvider` and `CreateClientWithActor`; the simple `(id, perms)` overload only sets granted permissions.
- **Configure DI before `CreateClient()`.** Use `ConfigureTestServices` for `ReplaceSingleton` / `ReplaceResourceLoader` / `ReplaceDbProvider`; mutating services after the host is built has no effect.
- **Default to deterministic time.** `factory.WithFakeTimeProvider(out var fake)` starts at `2024-01-01T00:00:00Z` so tests asserting on absolute timestamps do not flake. Use the explicit-instant overload only when a fixture needs a specific date.
- **Keep Entra-token tests gated.** `CreateClientWithActor` covers fast local and CI tests. Use `CreateClientWithEntraTokenAsync` + `MsalTestTokenProvider` only for E2E suites against a dedicated test tenant with MFA disabled for test users.

## Cross-references

- API surface (assertions, fakes, actor provider): [`trellis-api-testing-reference.md`](../api_reference/trellis-api-testing-reference.md)
- API surface (WebApplicationFactory, `.http` replay, MSAL): [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
- API surface (background-worker harness): [`trellis-api-testing-worker.md`](../api_reference/trellis-api-testing-worker.md)
- `Result<T>`, `Maybe<T>`, closed `Error` ADT, `ResourceRef`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Authorization primitives (`Actor`, `IActorProvider`): [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- Cookbook recipes (incl. **Recipe 16 — Unit of work in handlers**): [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md)
