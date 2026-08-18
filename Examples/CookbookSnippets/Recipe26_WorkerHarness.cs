// Cookbook Recipe 26 — Test a BackgroundService with WorkerHarness<TWorker>.
namespace CookbookSnippets.Recipe26;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis;
using Trellis.Mediator;
using Trellis.Testing.Worker;
using Xunit;

public sealed partial class ProbeId : RequiredGuid<ProbeId>;

public sealed class HealthProbe(ProbeId id, string route)
{
    public ProbeId Id { get; } = id;

    public string Route { get; } = route;
}

public sealed record ProbeCompletedDomainEvent(ProbeId ProbeId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RunProbeCommand(ProbeId ProbeId) : ICommand<Result<Trellis.Unit>>;

public sealed class RunProbeHandler : ICommandHandler<RunProbeCommand, Result<Trellis.Unit>>
{
    public ValueTask<Result<Trellis.Unit>> Handle(RunProbeCommand command, CancellationToken cancellationToken)
        => new(Result.Ok());
}

public interface IHealthProbeRepository
{
    Task AddAsync(HealthProbe probe, CancellationToken cancellationToken);

    Task<IReadOnlyList<HealthProbe>> GetDuePendingAsync(CancellationToken cancellationToken);
}

public sealed class FakeHealthProbeRepository : IHealthProbeRepository
{
    private readonly List<HealthProbe> _probes = [];

    public Task AddAsync(HealthProbe probe, CancellationToken cancellationToken)
    {
        _probes.Add(probe);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HealthProbe>> GetDuePendingAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HealthProbe>>([.. _probes]);
}

// The worker under test. BackgroundService is registered as a singleton hosted service, so it
// resolves scoped services through an IServiceScopeFactory rather than capturing them in the
// constructor. IWorkerTickSignal is the harness-only observation primitive; production hosts do
// not register an implementation, so the worker injects it as an optional dependency and no-ops
// when absent.
public sealed class HealthProbeWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    IWorkerTickSignal? tick = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Register the first Task.Delay BEFORE signaling readiness — the signal must prove the
        // FakeTimeProvider callback exists. Task.Delay(TimeSpan, TimeProvider, CancellationToken)
        // eagerly registers the timer with the TimeProvider when called, so by the time
        // SignalAsync("ready") completes the callback is already observable to Time.Advance.
        // Signaling FIRST would leave a gap during which the test can resume from
        // WaitForTickAsync("ready"), call Time.Advance, and have the worker subsequently register
        // a deadline of (advanced-now + period) — losing the Advance.
        var nextDelay = Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken);
        if (tick is not null)
            await tick.SignalAsync("ready", stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await nextDelay.ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            var repo = scope.ServiceProvider.GetRequiredService<IHealthProbeRepository>();

            foreach (var probe in await repo.GetDuePendingAsync(stoppingToken).ConfigureAwait(false))
            {
                var outcome = await mediator.Send(new RunProbeCommand(probe.Id), stoppingToken).ConfigureAwait(false);
                if (outcome.IsSuccess)
                    await publisher.PublishAsync(
                        new ProbeCompletedDomainEvent(probe.Id, time.GetUtcNow()),
                        stoppingToken).ConfigureAwait(false);
            }

            // Register the next iteration's delay BEFORE signaling "probe", for the same reason as
            // above. Signaling AFTER PublishAsync + the next Task.Delay registration makes
            // WaitForTickAsync a true completion barrier — every captured event for this iteration
            // is recorded AND the next callback exists for the next Advance.
            nextDelay = Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken);
            if (tick is not null)
                await tick.SignalAsync("probe", stoppingToken).ConfigureAwait(false);
        }
    }
}

// The integration test.
public class HealthProbeWorkerTests
{
#pragma warning disable CA1707 // Cookbook test recipe intentionally shows readable xUnit-style test names.
    [Fact]
    public async Task Worker_dispatches_due_probes_and_publishes_a_completion_event_per_run()
    {
        await using var harness = await WorkerHarness<HealthProbeWorker>.CreateAsync(opts =>
        {
            opts.ConfigureServices(s =>
            {
                // WorkerHarness deliberately does not call AddMediator(...) or
                // AddDomainEventDispatch(); a worker test re-uses the production composition root
                // so the test exercises the same wiring the production host uses. Forgetting
                // either registration would surface as GetRequiredService throwing at scope
                // resolution.
                //
                // The cookbook shows the AddMediator(...) call here:
                //
                //     s.AddMediator(options =>
                //     {
                //         options.Assemblies = [typeof(RunProbeCommand).Assembly];
                //         options.ServiceLifetime = ServiceLifetime.Scoped;
                //     });
                //
                // It is elided in this compile-check project because AddMediator is emitted by
                // the Mediator.SourceGenerator package, which this project deliberately does not
                // reference: several cookbook recipes show commands without a paired handler, and
                // the generator fails the build (MSG0005) on those. Your real test project does
                // reference it, so keep the call.
                s.AddDomainEventDispatch();
                s.AddSingleton<IHealthProbeRepository, FakeHealthProbeRepository>();
            });
            opts.SeedAsync(async (sp, ct) =>
            {
                var repo = sp.GetRequiredService<IHealthProbeRepository>();
                await repo.AddAsync(new HealthProbe(ProbeId.NewUniqueV7(), "/api/orders"), ct);
            });
        });

        await harness.StartAsync(CancellationToken.None);

        // StartAsync returns as soon as ExecuteAsync is scheduled, NOT after the worker has
        // registered its first Task.Delay callback with FakeTimeProvider. Block on the worker's
        // "ready" signal — which the worker emits AFTER its first Task.Delay(...) call — so the
        // subsequent Time.Advance always lands on an existing callback.
        await harness.WaitForTickAsync("ready", TimeSpan.FromSeconds(5));

        // Snapshot the most recent "probe" tick BEFORE advancing time. The cursor is the global
        // signal index (LastTickIndexOf returns -1 when nothing has signaled yet);
        // WaitForTickAsync(after: cursor, ...) blocks until a tick with a STRICTLY greater global
        // index fires. Do not use TickCountOf as a cursor — it is a per-name count, not the global
        // signal index, and will race when other tick names interleave.
        var cursor = harness.LastTickIndexOf("probe");

        // Advance the FakeTimeProvider past the worker's 5-minute Task.Delay. Advance is
        // deterministic; the worker's Task.Delay(interval, time, ct) resumes and runs the
        // iteration. WaitForTickAsync's timeout measures real time and is NOT consumed by Advance.
        harness.Time.Advance(TimeSpan.FromMinutes(5));
        await harness.WaitForTickAsync("probe", after: cursor, TimeSpan.FromSeconds(2));

        // Once WaitForTickAsync returns, PublishAsync for this iteration has already returned (the
        // worker signals AFTER the publish loop), so the captured-event list is fully populated and
        // the synchronous read is race-free.
        harness.Events<ProbeCompletedDomainEvent>().Should().HaveCount(1);
    }
#pragma warning restore CA1707
}
