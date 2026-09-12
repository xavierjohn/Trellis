namespace CookbookSnippets.Recipe26.NamedTicks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Testing.Worker;

public interface IHealthProbe
{
    Task ProbeAsync(CancellationToken cancellationToken);
}

public sealed class HealthProbeWorker(IServiceProvider services, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ticks = services.GetService<IWorkerTickSignal>();
        var probe = services.GetRequiredService<IHealthProbe>();
        var delay = Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken);
        if (ticks is not null)
            await ticks.SignalAsync("ready", stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            await delay.ConfigureAwait(false);
            await probe.ProbeAsync(stoppingToken).ConfigureAwait(false);
            delay = Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken);
            if (ticks is not null)
                await ticks.SignalAsync("probe", stoppingToken).ConfigureAwait(false);
        }
    }
}
