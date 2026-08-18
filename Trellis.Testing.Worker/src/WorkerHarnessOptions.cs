namespace Trellis.Testing.Worker;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis.Authorization;

/// <summary>
/// Mutable, fluent configuration for <see cref="WorkerHarness{TWorker}.CreateAsync(System.Action{WorkerHarnessOptions}?, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// All settings carry safe defaults. The harness applies them in this order during
/// <c>CreateAsync</c>:
/// </para>
/// <list type="number">
///   <item><description>Apply default <c>TimeProvider</c>, <c>IActorProvider</c>, domain-event capture, and tick-signal registrations.</description></item>
///   <item><description>Invoke each <see cref="ConfigureServices(System.Action{IServiceCollection})"/> delegate (in registration order).</description></item>
///   <item><description>Invoke each <see cref="ConfigureLogging(System.Action{ILoggingBuilder})"/> delegate (in registration order).</description></item>
///   <item><description>Register the worker as a hosted service (fails fast if an <see cref="Microsoft.Extensions.Hosting.IHostedService"/> with the same implementation type is already registered).</description></item>
///   <item><description>Build the host and resolve the harness-managed singletons.</description></item>
///   <item><description>Invoke each <see cref="SeedAsync(System.Func{IServiceProvider, System.Threading.CancellationToken, System.Threading.Tasks.Task})"/> delegate in a dedicated DI scope (in registration order).</description></item>
///   <item><description>If <see cref="AutoStart"/> is <see langword="true"/>, call <c>host.StartAsync()</c>; otherwise the caller must invoke <c>harness.StartAsync()</c> explicitly.</description></item>
/// </list>
/// </remarks>
public sealed class WorkerHarnessOptions
{
    private readonly List<Action<IServiceCollection>> _configureServices = [];
    private readonly List<Action<ILoggingBuilder>> _configureLogging = [];
    private readonly List<Func<IServiceProvider, CancellationToken, Task>> _seeds = [];

    /// <summary>
    /// The deterministic default starting instant — <c>2024-01-01T00:00:00Z</c>. Matches the
    /// <c>Trellis.Testing.AspNetCore.WebApplicationFactoryTimeExtensions.DefaultTestStartInstant</c>
    /// constant so worker tests and ASP.NET integration tests share the same baseline.
    /// </summary>
    public static readonly DateTimeOffset DefaultTestStartInstant =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The actor the harness's <see cref="TestActorProvider"/> returns by default. Worker
    /// tests typically use a system-actor identity carrying the permissions the worker's
    /// command handlers require. Defaults to <c>Actor.Create("system", new HashSet&lt;string&gt;())</c>
    /// — an empty-permission system actor; override before the harness is built when tests
    /// need specific permissions.
    /// </summary>
    public Actor SystemActor { get; set; } = Actor.Create("system", new HashSet<string>());

    /// <summary>
    /// The instant the harness-managed <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>
    /// reports at start. Defaults to <see cref="DefaultTestStartInstant"/>.
    /// </summary>
    public DateTimeOffset InitialTime { get; set; } = DefaultTestStartInstant;

    /// <summary>
    /// The fallback timeout used by <c>WaitForEventAsync</c> and <c>WaitForTickAsync</c> when
    /// the caller does not specify one. Measured in <b>real time</b> — advancing the harness's
    /// <c>FakeTimeProvider</c> does not consume this budget. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan DefaultWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When <see langword="true"/>, <see cref="WorkerHarness{TWorker}.CreateAsync(System.Action{WorkerHarnessOptions}?, System.Threading.CancellationToken)"/>
    /// also calls <c>host.StartAsync()</c> before returning. Defaults to <see langword="false"/> so the
    /// test can subscribe to events / configure waits before the worker starts producing them.
    /// </summary>
    public bool AutoStart { get; set; }

    internal IReadOnlyList<Action<IServiceCollection>> ConfigureServicesDelegates => _configureServices;
    internal IReadOnlyList<Action<ILoggingBuilder>> ConfigureLoggingDelegates => _configureLogging;
    internal IReadOnlyList<Func<IServiceProvider, CancellationToken, Task>> SeedDelegates => _seeds;

    /// <summary>
    /// Appends a delegate that contributes registrations to the worker host's
    /// <see cref="IServiceCollection"/>. Called after the harness's own defaults so user-supplied
    /// registrations can override them with <c>services.Replace(...)</c> or <c>services.RemoveAll(...)</c>.
    /// </summary>
    /// <param name="configure">A configuration delegate.</param>
    /// <returns>The same <see cref="WorkerHarnessOptions"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public WorkerHarnessOptions ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureServices.Add(configure);
        return this;
    }

    /// <summary>
    /// Appends a delegate that contributes logging configuration. Tests typically wire
    /// <c>FakeLogger</c> from <c>Microsoft.Extensions.Diagnostics.Testing</c> here so the test
    /// project can opt into log capture without forcing every worker-harness consumer to depend
    /// on the diagnostics-testing package.
    /// </summary>
    /// <param name="configure">A logging configuration delegate.</param>
    /// <returns>The same <see cref="WorkerHarnessOptions"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public WorkerHarnessOptions ConfigureLogging(Action<ILoggingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureLogging.Add(configure);
        return this;
    }

    /// <summary>
    /// Appends a seed delegate invoked once the host is built but before it is started. The
    /// harness creates a dedicated DI scope for each delegate so EF Core <c>DbContext</c> and
    /// other scoped services resolve correctly; the scope is disposed before the next delegate runs.
    /// </summary>
    /// <param name="seed">A seed delegate receiving the scope's <see cref="IServiceProvider"/>.</param>
    /// <returns>The same <see cref="WorkerHarnessOptions"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="seed"/> is <see langword="null"/>.</exception>
    public WorkerHarnessOptions SeedAsync(Func<IServiceProvider, CancellationToken, Task> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _seeds.Add(seed);
        return this;
    }
}