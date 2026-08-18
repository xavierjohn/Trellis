namespace Trellis.Testing.Worker.Tests.Helpers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Mediator;

/// <summary>
/// Minimal <see cref="BackgroundService"/> used by the harness tests. On each tick it
/// publishes a <see cref="TestTickCompletedEvent"/> through <see cref="IDomainEventPublisher"/>
/// and signals <see cref="IWorkerTickSignal"/>. The interval is parameterized so tests can
/// drive several iterations through
/// <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider.Advance(TimeSpan)"/>.
/// </summary>
/// <remarks>
/// The worker resolves <see cref="IDomainEventPublisher"/> from a per-tick scope to match how
/// production workers create scopes for unit-of-work isolation. The harness's open-generic
/// <c>DomainEventCaptureHandler&lt;TEvent&gt;</c> registration is closed by DI inside each
/// scope and records the event into the shared singleton capture.
/// </remarks>
public sealed class TestWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IWorkerTickSignal tickSignal,
    TestWorkerOptions options) : BackgroundService
{
    private int _iteration;

    public int Iterations => Volatile.Read(ref _iteration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var iteration = Interlocked.Increment(ref _iteration);

            await using var scope = scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
            await publisher.PublishAsync(
                new TestTickCompletedEvent(iteration, timeProvider.GetUtcNow()),
                stoppingToken).ConfigureAwait(false);

            await tickSignal.SignalAsync(options.TickName, stoppingToken).ConfigureAwait(false);
        }
    }
}

public sealed class TestWorkerOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    public string TickName { get; init; } = string.Empty;
}