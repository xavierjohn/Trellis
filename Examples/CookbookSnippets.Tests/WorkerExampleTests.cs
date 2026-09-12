namespace CookbookSnippets.Tests;

using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Trellis.Testing.Worker;
using CookbookSnippets.Recipe26.NamedTicks;

public class WorkerExampleTests
{
    [Fact]
    public async Task NamedTick_EachSignal_HasArmedNextDelayBeforeTimeAdvances()
    {
        var time = new ObservedTimeProvider();
        var signal = new AdvancingSignal(time);
        await using var services = new ServiceCollection()
            .AddSingleton<IHealthProbe, Probe>()
            .AddSingleton<IWorkerTickSignal>(signal)
            .BuildServiceProvider();
        using var worker = new HealthProbeWorker(services, time);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            for (var expected = 1; expected <= 3; expected++)
            {
                var observation = await signal.Observations.Reader.ReadAsync(TestContext.Current.CancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                observation.TimerCount.Should().Be(expected, "the next delay must exist before publishing readiness or a completed tick");
                observation.Name.Should().Be(expected == 1 ? "ready" : "probe");
            }
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class Probe : IHealthProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ObservedTimeProvider : TimeProvider
    {
        public FakeTimeProvider Inner { get; } = new();
        public int TimerCount { get; private set; }
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerCount++;
            return Inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class AdvancingSignal(ObservedTimeProvider time) : IWorkerTickSignal
    {
        public Channel<(string Name, int TimerCount)> Observations { get; } =
            Channel.CreateUnbounded<(string, int)>();

        public ValueTask SignalAsync(CancellationToken cancellationToken = default) =>
            SignalAsync("", cancellationToken);

        public ValueTask SignalAsync(string name, CancellationToken cancellationToken = default)
        {
            Observations.Writer.TryWrite((name, time.TimerCount));
            if (time.TimerCount < 3)
                time.Inner.Advance(TimeSpan.FromSeconds(30));
            return ValueTask.CompletedTask;
        }
    }
}
