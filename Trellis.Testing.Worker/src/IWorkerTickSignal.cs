namespace Trellis.Testing.Worker;

/// <summary>
/// Cooperative signal that a worker tick has completed. The harness registers a singleton
/// implementation; the worker's <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// resolves the interface from DI and calls <see cref="SignalAsync(System.Threading.CancellationToken)"/>
/// at the end of each tick so the test can block on
/// <see cref="WorkerHarness{TWorker}.WaitForTickAsync(System.TimeSpan?, System.Threading.CancellationToken)"/>
/// instead of <see cref="System.Threading.Tasks.Task.Delay(System.TimeSpan)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use this primitive when a tick has no domain-visible outcome (no <see cref="IDomainEvent"/>
/// emitted) — for example, an infrastructure-only tick that returns "nothing to do" or a
/// no-op probe. When a tick already emits a domain event,
/// <see cref="WorkerHarness{TWorker}.WaitForEventAsync{TEvent}(System.TimeSpan?, System.Threading.CancellationToken)"/>
/// is preferred because it asserts a specific business outcome rather than a generic boundary.
/// </para>
/// <para>
/// Production builds typically do not register an implementation; the worker can resolve it as
/// optional (<see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService{T}(System.IServiceProvider)"/>)
/// and no-op when absent. The harness registers an implementation so the test side can observe
/// the signal.
/// </para>
/// </remarks>
public interface IWorkerTickSignal
{
    /// <summary>
    /// Signals completion of a worker tick with no name (the harness records it under the empty string).
    /// </summary>
    /// <param name="cancellationToken">A token to observe while signaling.</param>
    /// <returns>A <see cref="System.Threading.Tasks.ValueTask"/> that completes once the signal is recorded and any pending waiters are released.</returns>
    System.Threading.Tasks.ValueTask SignalAsync(System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals completion of a named worker tick. Tests can wait for a specific name via
    /// <see cref="WorkerHarness{TWorker}.WaitForTickAsync(string, System.TimeSpan?, System.Threading.CancellationToken)"/>.
    /// </summary>
    /// <param name="name">An identifier for the tick (for example, the job name or phase).</param>
    /// <param name="cancellationToken">A token to observe while signaling.</param>
    /// <returns>A <see cref="System.Threading.Tasks.ValueTask"/> that completes once the signal is recorded and any pending waiters are released.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="name"/> is <see langword="null"/>.</exception>
    System.Threading.Tasks.ValueTask SignalAsync(string name, System.Threading.CancellationToken cancellationToken = default);
}