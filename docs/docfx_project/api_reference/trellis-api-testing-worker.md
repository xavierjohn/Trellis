---
package: Trellis.Testing.Worker
namespaces: [Trellis.Testing.Worker]
types: [WorkerHarness`1, WorkerHarnessOptions, IWorkerTickSignal, WorkerHarnessTimeoutException]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Testing.Worker &mdash; API Reference

**Package:** `Trellis.Testing.Worker`  
**Namespace:** `Trellis.Testing.Worker`  
**Purpose:** Integration-test harness for `BackgroundService` workers. Builds an `IHost` with a deterministic `FakeTimeProvider`, a configurable `TestActorProvider`, and a domain-event capture wired through the Trellis mediator pipeline so tests can advance time, wait for specific events or named tick signals, and assert what the worker emitted.

Use this package from test projects only. It depends on `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.TimeProvider.Testing`, `Trellis.Authorization`, `Trellis.Mediator`, and `Trellis.Testing`.

## Use this file when

- You are writing integration tests for a `BackgroundService` that publishes Trellis domain events.
- You need deterministic control of a worker's `Task.Delay` / `PeriodicTimer` continuations via `FakeTimeProvider`.
- You want a race-proof primitive for waiting on the first domain event of a given type or a named tick signal.
- You need the worker's `IActorProvider` to resolve to a deterministic system actor outside an HTTP context.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Build a harness for `MyWorker` and start it manually | `await using var harness = await WorkerHarness<MyWorker>.CreateAsync(opts => opts.ConfigureServices(...));` then `await harness.StartAsync(ct);` | [`WorkerHarness<TWorker>`](#workerharnesstworker) |
| Build and auto-start in one call | `WorkerHarness<MyWorker>.CreateAsync(opts => { opts.AutoStart = true; opts.ConfigureServices(...); });` | [`WorkerHarnessOptions`](#workerharnessoptions) |
| Advance the worker's clock | `harness.Time.Advance(TimeSpan.FromMinutes(5));` | [`WorkerHarness<TWorker>.Time`](#workerharnesstworker) |
| Wait for the first event of a type | `var ev = await harness.WaitForEventAsync<OrderShipped>(TimeSpan.FromSeconds(5), ct);` | [`WorkerHarness<TWorker>`](#workerharnesstworker) |
| Wait for a filtered event | `await harness.WaitForEventAsync<OrderShipped>(e => e.OrderId == id, timeout, ct);` | [`WorkerHarness<TWorker>`](#workerharnesstworker) |
| Wait for a named tick signal | `await harness.WaitForTickAsync("reminder-job", timeout, ct);` | [`IWorkerTickSignal`](#iworkerticksignal) |
| Read every captured event of a type | `var all = harness.Events<OrderShipped>();` | [`WorkerHarness<TWorker>`](#workerharnesstworker) |
| Seed the database before the host starts | `opts.SeedAsync((sp, ct) => sp.GetRequiredService<MyDbContext>().SeedAsync(ct));` | [`WorkerHarnessOptions`](#workerharnessoptions) |
| Run the worker under a specific system actor | `opts.SystemActor = Actor.Create("subscription-renewal-worker", new HashSet<string>(["..."]));` | [`WorkerHarnessOptions`](#workerharnessoptions) |
| Detect a missing wait | catch `WorkerHarnessTimeoutException` | [`WorkerHarnessTimeoutException`](#workerharnesstimeoutexception) |

## Composition rules

- The harness owns the `IHostedService` registration for `TWorker`. **Do not** call `services.AddHostedService<TWorker>()` from your `ConfigureServices` callback — `CreateAsync` will throw `InvalidOperationException` so the test does not silently run two copies of the worker.
- The harness does **not** call `AddDomainEventDispatch()` on your behalf. Worker tests are integration tests of the production composition root, so include the same mediator-dispatch registration the worker's production host uses inside `ConfigureServices`. Without it the capture handler is registered but the mediator pipeline never publishes events, and `WaitForEventAsync` will time out with a diagnostic that calls this out.
- Wait timeouts measure **real time**, not the harness's fake clock. Calling `harness.Time.Advance(...)` does not consume the wait budget. The fake clock only drives the worker's `Task.Delay` / `PeriodicTimer` continuations.
- `AutoStart` defaults to `false` so tests can subscribe to events / configure waits before the worker starts producing them.
- **Startup race.** `IHost.StartAsync` returns once `ExecuteAsync` is scheduled, not once the worker has registered its first `Task.Delay` callback with the `FakeTimeProvider`. Either call `await harness.SettleAsync(cancellationToken: ct)` (real-time yield, default 200ms) after `StartAsync`, or — deterministically — have the worker REGISTER its first `Task.Delay(period, time, ct)` before calling `IWorkerTickSignal.SignalAsync("ready", ct)`, then `await` the saved delay, and use `await harness.WaitForTickAsync("ready", cancellationToken: ct)` in the test. The `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` overload eagerly registers the callback with the `TimeProvider`, so signaling AFTER the call guarantees the callback exists by the time the test resumes and calls `Time.Advance`.

## API

### `WorkerHarness<TWorker>`

```csharp
public sealed class WorkerHarness<TWorker> : IAsyncDisposable
    where TWorker : BackgroundService
```

The `TWorker` type parameter carries `[DynamicallyAccessedMembers(PublicConstructors)]` so AOT trimming keeps the worker's constructor reachable.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<WorkerHarness<TWorker>> CreateAsync(Action<WorkerHarnessOptions>? configure = null, CancellationToken cancellationToken = default)` | `Task<WorkerHarness<TWorker>>` | Applies defaults, runs `ConfigureServices` and `ConfigureLogging` callbacks, registers `TWorker` as a hosted service (fail-fast on duplicate), builds the host, runs seeds in dedicated scopes, and optionally starts the host when `AutoStart` is `true`. |
| `public IHost Host { get; }` | `IHost` | The underlying host. Exposed for advanced lifecycle scenarios. |
| `public IServiceProvider Services { get; }` | `IServiceProvider` | The root provider of the worker host. Tests typically create their own scope for resolving scoped services. |
| `public FakeTimeProvider Time { get; }` | `FakeTimeProvider` | The singleton `TimeProvider`. Advance with `Time.Advance(TimeSpan)` to drive the worker. |
| `public TestActorProvider Actor { get; }` | `TestActorProvider` | The harness-managed actor provider. Call `Actor.WithActor(actor)` inside a scope to override the default system actor. |
| `public IWorkerTickSignal TickSignal { get; }` | `IWorkerTickSignal` | The tick signal registered with the host. Tests typically observe it via `WaitForTickAsync` rather than calling it directly. |
| `public Task StartAsync(CancellationToken cancellationToken = default)` | `Task` | Starts the worker host. Idempotent — subsequent calls return without restarting. |
| `public Task SettleAsync(TimeSpan? duration = null, CancellationToken cancellationToken = default)` | `Task` | Real-time yield (default 200ms) so the worker's `ExecuteAsync` takes its first scheduling turn and registers its initial `Task.Delay` / `PeriodicTimer` callback with the `FakeTimeProvider`. Call after `StartAsync` when the worker has no other readiness signal. For a deterministic alternative, have the worker register the first `Task.Delay(period, time, ct)` BEFORE calling `IWorkerTickSignal.SignalAsync("ready", ct)` (so the callback is provably registered when the signal fires), then `await` the saved delay. |
| `public Task StopAsync(CancellationToken cancellationToken = default)` | `Task` | Stops the worker host. Idempotent. |
| `public IReadOnlyList<TEvent> Events<TEvent>() where TEvent : IDomainEvent` | `IReadOnlyList<TEvent>` | Returns every captured event assignable to `TEvent`, in dispatch order. Snapshot semantics: the returned list does not change when later events are captured. |
| `public IReadOnlyList<IDomainEvent> AllEvents { get; }` | `IReadOnlyList<IDomainEvent>` | Snapshot of every captured event regardless of type. |
| `public Task<TEvent> WaitForEventAsync<TEvent>(TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TEvent : IDomainEvent` | `Task<TEvent>` | Returns the first event of type `TEvent`. Returns immediately if one was captured before the call. Throws `WorkerHarnessTimeoutException` on timeout. |
| `public Task<TEvent> WaitForEventAsync<TEvent>(Func<TEvent, bool> predicate, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TEvent : IDomainEvent` | `Task<TEvent>` | Same, but only events satisfying `predicate` complete the wait. |
| `public Task<int> WaitForTickAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | `Task<int>` | Completes when any tick is signaled. Returns immediately if a tick was recorded before the call. Returns the global signal index of the satisfying tick. Throws `WorkerHarnessTimeoutException` on timeout. |
| `public Task<int> WaitForTickAsync(int after, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | `Task<int>` | Completes when any tick with a global signal index strictly greater than `after` is signaled. Pass the index returned by an earlier call (or `-1` to wait for any tick). Use for periodic workers where the same name is emitted on every iteration. |
| `public Task<int> WaitForTickAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | `Task<int>` | Completes when a tick with the supplied `name` is signaled (ordinal equality). Returns immediately if a matching tick is anywhere in the recorded history. |
| `public Task<int> WaitForTickAsync(string name, int after, TimeSpan? timeout = null, CancellationToken cancellationToken = default)` | `Task<int>` | Completes when a tick with `name` is signaled at a global index strictly greater than `after`. Pass the index returned by the previous wait, or `LastTickIndexOf(name)` for a baseline cursor. |
| `public int TickCount { get; }` | `int` | Total number of ticks captured so far (across all names). `TickCount - 1` is a valid `after:` cursor for the unnamed overload. |
| `public int TickCountOf(string name)` | `int` | Number of ticks captured so far with the given `name`. **Not** a valid `after:` cursor for the named overload (it is a per-name count, not a global index); use `LastTickIndexOf(name)` instead. Appropriate for assertions such as `harness.TickCountOf("probe").Should().Be(3)`. |
| `public int LastTickIndexOf(string name)` | `int` | Global signal index of the most recent tick captured with the given `name`, or `-1` if none. Pass this value as `after:` to `WaitForTickAsync(name, after, ...)` for a baseline named cursor that is correct even when other tick names are interleaved in the history. |
| `public ValueTask DisposeAsync()` | `ValueTask` | Stops the host (capped at a 10-second real-time budget) and disposes the underlying `IHost`. Idempotent. |

`Events<TEvent>()` and `WaitForEventAsync<TEvent>` both use runtime `is` matching, so derived events appear in queries for their base type.

### `WorkerHarnessOptions`

```csharp
public sealed class WorkerHarnessOptions
```

| Member | Type | Description |
| --- | --- | --- |
| `public static readonly DateTimeOffset DefaultTestStartInstant` | `DateTimeOffset` | `2024-01-01T00:00:00Z`. Matches `Trellis.Testing.AspNetCore.WebApplicationFactoryTimeExtensions.DefaultTestStartInstant`. |
| `Actor SystemActor { get; set; }` | `Actor` | The actor returned by the harness's `TestActorProvider`. Defaults to `Actor.Create("system", new HashSet<string>())` — an empty-permission system actor. |
| `DateTimeOffset InitialTime { get; set; }` | `DateTimeOffset` | The instant the harness's `FakeTimeProvider` reports at start. Defaults to `DefaultTestStartInstant`. |
| `TimeSpan DefaultWaitTimeout { get; set; }` | `TimeSpan` | Fallback timeout for `WaitForEventAsync` / `WaitForTickAsync` when the caller does not supply one. Real time. Defaults to 5 seconds. |
| `bool AutoStart { get; set; }` | `bool` | When `true`, `CreateAsync` also calls `host.StartAsync()` before returning. Defaults to `false`. |
| `WorkerHarnessOptions ConfigureServices(Action<IServiceCollection> configure)` | `WorkerHarnessOptions` | Appends a registration delegate, executed after harness defaults so callers can `Replace`/`RemoveAll` them. |
| `WorkerHarnessOptions ConfigureLogging(Action<ILoggingBuilder> configure)` | `WorkerHarnessOptions` | Appends a logging-configuration delegate. Tests opt into `FakeLogger` here. |
| `WorkerHarnessOptions SeedAsync(Func<IServiceProvider, CancellationToken, Task> seed)` | `WorkerHarnessOptions` | Appends a seed delegate invoked inside a dedicated DI scope between host build and host start. |

### `IWorkerTickSignal`

```csharp
public interface IWorkerTickSignal
```

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask SignalAsync(CancellationToken cancellationToken = default)` | `ValueTask` | Signals an unnamed tick. The harness records it under the empty string. |
| `ValueTask SignalAsync(string name, CancellationToken cancellationToken = default)` | `ValueTask` | Signals a named tick. Tests can wait for the specific name via `WorkerHarness.WaitForTickAsync(string, ...)`. Throws `ArgumentNullException` for null `name`. |

Production builds typically do not register an implementation; the worker resolves the interface as optional (`GetService<IWorkerTickSignal>()`) and no-ops when absent. The harness registers an implementation so tests can observe ticks.

Prefer `WaitForEventAsync<TEvent>` when a tick already emits a domain event — it asserts a specific business outcome rather than a generic boundary.

### `WorkerHarnessTimeoutException`

```csharp
public sealed class WorkerHarnessTimeoutException : TimeoutException
```

| Signature | Description |
| --- | --- |
| `WorkerHarnessTimeoutException()` | Parameterless constructor. |
| `WorkerHarnessTimeoutException(string message)` | Constructor with diagnostic message. |
| `WorkerHarnessTimeoutException(string message, Exception innerException)` | Constructor with message and inner exception. |

Thrown by `WaitForEventAsync` and `WaitForTickAsync`. The message names the awaited condition, the timeout that elapsed, the count of captured events / recorded tick names so far, and (for event waits) a reminder to register `AddDomainEventDispatch()`.

Caller cancellation propagates as plain `OperationCanceledException`, not `WorkerHarnessTimeoutException`, so tests can distinguish "test gave up" from "wait expired".

## Common examples

### Build a harness and drive one tick

```csharp
using Trellis.Mediator;
using Trellis.Testing.Worker;

await using var harness = await WorkerHarness<SubscriptionRenewalWorker>.CreateAsync(opts =>
{
    opts.ConfigureServices(services =>
    {
        services.AddDomainEventDispatch();
        services.AddDbContext<RemindersDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<ISubscriptionsRepository, SubscriptionsRepository>();
    });
    opts.SeedAsync((sp, ct) =>
    {
        var db = sp.GetRequiredService<RemindersDbContext>();
        return db.SeedTestSubscriptionsAsync(ct);
    });
});

await harness.StartAsync(TestContext.Current.CancellationToken);
await harness.SettleAsync(cancellationToken: TestContext.Current.CancellationToken);

harness.Time.Advance(TimeSpan.FromHours(24));

var reminder = await harness.WaitForEventAsync<RenewalReminderQueued>(
    TimeSpan.FromSeconds(5),
    TestContext.Current.CancellationToken);

reminder.SubscriptionId.Should().Be(expectedId);
```

### Use a named system actor and a custom permission set

```csharp
var workerActor = Actor.Create(
    "subscription-renewal-worker",
    new HashSet<string>(["Subscriptions.Read", "Subscriptions.Write"]));

await using var harness = await WorkerHarness<SubscriptionRenewalWorker>.CreateAsync(opts =>
{
    opts.SystemActor = workerActor;
    opts.ConfigureServices(services =>
    {
        services.AddDomainEventDispatch();
        // ...rest of the production composition root...
    });
});
```

### Wait for a named tick when the worker emits no domain event

The harness always registers `IWorkerTickSignal` for its own process — but the production
worker's host typically does **not** register it (the signal is a test-only concern). Resolve
it with `GetService<IWorkerTickSignal>()` rather than constructor-injecting `IWorkerTickSignal?`,
because .NET DI does not treat nullable annotations as optional dependencies; constructor
injection of an unregistered service fails activation in production.

```csharp
public sealed class HealthProbeWorker(IServiceProvider services, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resolved once at start-up; null in production (not registered), non-null in tests.
        var ticks = services.GetService<IWorkerTickSignal>();

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeAsync(stoppingToken);
            if (ticks is not null) await ticks.SignalAsync("probe", stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken);
        }
    }
}

// In the test — capture a baseline cursor BEFORE each Advance so successive waits do not
// re-match the previous "probe" tick (HealthProbeWorker emits the same name on every
// iteration). LastTickIndexOf(name) returns the global signal index of the most recent
// matching tick (or -1), which remains correct even when other tick names interleave.
var cursor = harness.LastTickIndexOf("probe");
harness.Time.Advance(TimeSpan.FromSeconds(30));
cursor = await harness.WaitForTickAsync("probe", after: cursor, TimeSpan.FromSeconds(5), cancellationToken);

harness.Time.Advance(TimeSpan.FromSeconds(30));
cursor = await harness.WaitForTickAsync("probe", after: cursor, TimeSpan.FromSeconds(5), cancellationToken);
```

`WaitForTickAsync(name, ...)` without `after:` returns immediately whenever a matching tick is anywhere in the history. That is the right shape for the deterministic-ready pattern (the worker signals once at startup, the test waits once). For periodic workers — where the same tick name is emitted every iteration — always thread the returned index back into the next call as `after:`, or capture `harness.LastTickIndexOf(name)` for a baseline cursor. Avoid `TickCountOf(name) - 1` as a cursor: it is a per-name count, not a global signal index, so with interleaved tick names it can fall below the global index of an already-recorded matching tick and produce a wait that completes immediately for that old tick.

## See also

- [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md#use-this-file-when) — `WebApplicationFactory<TEntryPoint>` helpers, fake time, and `.http` file replay for HTTP integration tests.
- [trellis-api-testing-reference.md](trellis-api-testing-reference.md#use-this-file-when) — core Trellis testing assertions, fake repositories, and `TestActorProvider`.
- [trellis-api-authorization.md](trellis-api-authorization.md#types) — `Actor` and `IActorProvider`.
- [trellis-api-asp.md](trellis-api-asp.md#namespace-trellisaspauthorization) — `AddTrellisWorkerActor` composition helper for the worker's production host.
- [trellis-api-mediator.md](trellis-api-mediator.md#domaineventdispatchservicecollectionextensions) — `AddDomainEventDispatch`, `IDomainEventHandler<T>`, and the mediator pipeline the harness captures from.
