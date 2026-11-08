---
name: recipe-26-worker-tests
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows integration tests for a BackgroundService with WorkerHarness. Use when testing a hosted worker."
---
# Recipe 26 — Test a `BackgroundService` with `WorkerHarness<TWorker>`

## Contents

- Problem
- What it shows

**Problem.** A periodic worker reads pending work, dispatches a side effect, and emits a domain event per item. Integration tests must drive the worker forward deterministically (no `Task.Delay`), capture the domain events the worker raised, and stop the host cleanly — without re-implementing the `IHost` + `FakeTimeProvider` + `IActorProvider` + `IDomainEventHandler<T>` plumbing in every test fixture.

```csharp
// The worker under test. BackgroundService is registered as a singleton hosted service,
// so it resolves scoped services through an IServiceScopeFactory rather than capturing
// them in the constructor. IWorkerTickSignal is the harness-only observation primitive;
// production hosts do not register an implementation, so the worker injects it as an
// optional dependency and no-ops when absent.
public sealed class HealthProbeWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    IWorkerTickSignal? tick = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register the first Task.Delay BEFORE signaling readiness — the signal must
        // prove the FakeTimeProvider callback exists. Task.Delay(TimeSpan, TimeProvider,
        // CancellationToken) eagerly registers the timer with the TimeProvider when
        // called, so by the time SignalAsync("ready") completes the callback is already
        // observable to Time.Advance. Signaling FIRST (before Task.Delay) would leave a
        // gap during which the test can resume from WaitForTickAsync("ready"), call
        // Time.Advance, and have the worker subsequently register a deadline of
        // (advanced-now + period) — losing the Advance. The signal is a no-op in
        // production hosts that do not register IWorkerTickSignal.
        var nextDelay = Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken);
        if (tick is not null)
            await tick.SignalAsync("ready", stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await nextDelay.ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator  = scope.ServiceProvider.GetRequiredService<IMediator>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            var repo      = scope.ServiceProvider.GetRequiredService<IHealthProbeRepository>();

            foreach (var probe in await repo.GetDuePendingAsync(stoppingToken).ConfigureAwait(false))
            {
                var outcome = await mediator.Send(new RunProbeCommand(probe.Id), stoppingToken).ConfigureAwait(false);
                if (outcome.IsSuccess)
                    await publisher.PublishAsync(
                        new ProbeCompletedDomainEvent(probe.Id, time.GetUtcNow()),
                        stoppingToken).ConfigureAwait(false);
            }

            // Register the next iteration's delay BEFORE signaling "probe", for the same
            // reason as above: a test that snapshots a probe cursor and advances time
            // after the wait expects the next callback to already be registered.
            // Signaling AFTER PublishAsync + the next Task.Delay registration makes
            // WaitForTickAsync a true completion barrier — every captured event for this
            // iteration is recorded AND the next callback exists for the next Advance.
            nextDelay = Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken);
            if (tick is not null)
                await tick.SignalAsync("probe", stoppingToken).ConfigureAwait(false);
        }
    }
}

// The integration test.
public class HealthProbeWorkerTests
{
    [Fact]
    public async Task Worker_dispatches_due_probes_and_publishes_a_completion_event_per_run()
    {
        await using var harness = await WorkerHarness<HealthProbeWorker>.CreateAsync(opts =>
        {
            opts.ConfigureServices(s =>
            {
                // WorkerHarness deliberately does not call AddMediator(...) or
                // AddDomainEventDispatch(); a worker test re-uses the production
                // composition root so the test exercises the same wiring the production
                // host uses. Forgetting either registration would surface as
                // GetRequiredService throwing at scope resolution.
                s.AddMediator(options =>
                {
                    options.Assemblies = [typeof(RunProbeCommand).Assembly];
                    options.ServiceLifetime = ServiceLifetime.Scoped;
                });
                s.AddDomainEventDispatch();
                s.AddSingleton<IHealthProbeRepository, FakeHealthProbeRepository>();
            });
            opts.SeedAsync(async (sp, ct) =>
            {
                var repo = sp.GetRequiredService<IHealthProbeRepository>();
                await repo.AddAsync(new HealthProbe(ProbeId.NewUniqueV7(), "/api/orders"), ct);
            });
        });

        await harness.StartAsync(TestContext.Current.CancellationToken);

        // StartAsync returns as soon as ExecuteAsync is scheduled, NOT after the
        // worker has registered its first Task.Delay callback with FakeTimeProvider.
        // Block on the worker's "ready" signal — which the worker emits AFTER its
        // first Task.Delay(...) call (so the callback is provably registered before
        // the signal fires) — so the subsequent Time.Advance always lands on an
        // existing callback. Without this barrier the Advance races the worker.
        await harness.WaitForTickAsync("ready", TimeSpan.FromSeconds(5));

        // Snapshot the most recent "probe" tick BEFORE advancing time. The cursor is the
        // global signal index (LastTickIndexOf returns -1 when nothing has signaled yet);
        // WaitForTickAsync(after: cursor, ...) will block until a tick with a STRICTLY
        // greater global index fires, so the wait can never observe a tick already in
        // the recorded history. Do not use TickCountOf as a cursor — it is a per-name
        // count, not the global signal index, and will race when other tick names
        // interleave.
        var cursor = harness.LastTickIndexOf("probe");

        // Advance the FakeTimeProvider past the worker's 5-minute Task.Delay. Advance is
        // deterministic; the worker's Task.Delay(interval, time, ct) resumes and runs the
        // iteration. WaitForTickAsync's timeout measures real time and is NOT consumed
        // by the call to Advance.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.WaitForTickAsync("probe", after: cursor, TimeSpan.FromSeconds(2));

        // Once WaitForTickAsync returns, PublishAsync for this iteration has already
        // returned (the worker signals AFTER the publish loop), so the captured-event
        // list is fully populated and the synchronous read is race-free.
        harness.Events<ProbeCompletedDomainEvent>().Should().HaveCount(1);
    }
}
```

**What it shows.** `WorkerHarness<TWorker>.CreateAsync(...)` builds an `IHost` with `TWorker` registered as the sole hosted service, wires `TimeProvider` to `FakeTimeProvider`, registers a `TestActorProvider` returning `WorkerHarnessOptions.SystemActor`, and installs an open-generic `IDomainEventHandler<>` that captures every published event for `harness.Events<TEvent>()` and `harness.WaitForEventAsync<TEvent>()`. The harness does **not** register `AddMediator(...)` or `AddDomainEventDispatch()` — those are part of the production composition root and belong in the test's `ConfigureServices` callback so the worker exercises the same wiring it uses in production. `harness.StartAsync(...)` returns as soon as `ExecuteAsync` is scheduled on the thread pool, NOT after the worker has registered its first `Task.Delay` with `FakeTimeProvider`; the recipe makes the readiness barrier deterministic by REGISTERING the first `Task.Delay(...)` before signaling `"ready"` (because `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` eagerly calls `timeProvider.CreateTimer(...)`, the callback exists by the time `SignalAsync("ready")` completes), then `await harness.WaitForTickAsync("ready", ...)` in the test releases when the test can safely call `Time.Advance`. Signaling first and registering the delay second would leave a gap during which the test could `Advance` before any callback was registered — losing the `Advance`. The lazier alternative when you cannot change the worker is `await harness.SettleAsync()`, at the cost of a real-time yield. `harness.Time.Advance(...)` is the deterministic equivalent of `Task.Delay` — the worker's `Task.Delay(interval, time, ct)` resumes synchronously and runs the next iteration. Tests should pair `Advance` with `WaitForTickAsync(name, after: cursor, ...)` so the wait observes the *next* tick rather than racing with one already in the history; `LastTickIndexOf(name)` is the right baseline-cursor source. The harness's `DisposeAsync` stops the host with a real-time cap; if `StartAsync` itself throws, the harness drives a best-effort `StopAsync` so any earlier-registered `IHostedService` that succeeded still observes its stop hook.

`Trellis.Testing.Worker` references `Trellis.Testing` transitively, so handler-test assertions like `Should().BeSuccess()` and `FakeRepository<,>` are available from the same test project without an extra package.
