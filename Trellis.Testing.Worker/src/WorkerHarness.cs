namespace Trellis.Testing.Worker;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Trellis.Authorization;
using Trellis.Mediator;

/// <summary>
/// Integration-test harness for a <see cref="BackgroundService"/> worker. Builds an
/// <see cref="IHost"/> with a deterministic <see cref="FakeTimeProvider"/>, a configurable
/// <see cref="TestActorProvider"/>, and a domain-event capture wired through the Trellis
/// mediator pipeline so tests can advance time, wait for specific events or named tick
/// signals, and assert what the worker emitted.
/// </summary>
/// <typeparam name="TWorker">The <see cref="BackgroundService"/> implementation under test.</typeparam>
/// <remarks>
/// <para>
/// The harness owns hosted-service registration for <typeparamref name="TWorker"/>. A test
/// that also registers <typeparamref name="TWorker"/> as an <see cref="IHostedService"/>
/// in its <see cref="WorkerHarnessOptions.ConfigureServices(System.Action{IServiceCollection})"/>
/// callback will fail fast at <see cref="CreateAsync(System.Action{WorkerHarnessOptions}?, System.Threading.CancellationToken)"/>
/// time so the test does not silently run two copies of the worker.
/// </para>
/// <para>
/// The harness does <b>not</b> register <c>AddDomainEventDispatch()</c>. Worker tests are
/// integration tests of the production composition root — the same registration the worker's
/// production host uses should appear in the test's
/// <see cref="WorkerHarnessOptions.ConfigureServices(System.Action{IServiceCollection})"/>
/// callback. Without it, the open-generic capture handler is registered but the mediator
/// pipeline never publishes events, so <see cref="Events{TEvent}()"/> would remain empty
/// even when the worker raises events on its aggregates.
/// </para>
/// <para>
/// Wait timeouts measure <b>real time</b>, not the harness's fake clock — calling
/// <see cref="Time"/>.<c>Advance(...)</c> does not consume the timeout budget. The fake clock
/// only drives the worker's <c>Task.Delay</c> / <c>PeriodicTimer</c> continuations.
/// </para>
/// </remarks>
public sealed class WorkerHarness<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TWorker> : IAsyncDisposable
    where TWorker : BackgroundService
{
    private readonly IHost _host;
    private readonly DomainEventCapture _capture;
    private readonly WorkerTickSignal _tickSignal;
    private readonly TimeSpan _defaultWaitTimeout;
    private int _started;
    private int _disposed;

    private WorkerHarness(
        IHost host,
        FakeTimeProvider time,
        TestActorProvider actor,
        DomainEventCapture capture,
        WorkerTickSignal tickSignal,
        TimeSpan defaultWaitTimeout)
    {
        _host = host;
        Time = time;
        Actor = actor;
        _capture = capture;
        _tickSignal = tickSignal;
        _defaultWaitTimeout = defaultWaitTimeout;
    }

    /// <summary>The underlying <see cref="IHost"/>; exposed for advanced scenarios that need direct lifecycle access.</summary>
    public IHost Host => _host;

    /// <summary>The root <see cref="IServiceProvider"/> of the worker host. Tests typically create their own scope from this when resolving scoped services after the host starts.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>The harness-managed <see cref="FakeTimeProvider"/> registered as the singleton <see cref="TimeProvider"/>. Tests advance time via <see cref="FakeTimeProvider.Advance(TimeSpan)"/> to drive the worker's <c>Task.Delay</c> / <c>PeriodicTimer</c> continuations.</summary>
    public FakeTimeProvider Time { get; }

    /// <summary>The harness-managed <see cref="TestActorProvider"/>. Tests can call <see cref="TestActorProvider.WithActor(Actor)"/> inside a scope to override the default system actor for a specific command flow.</summary>
    public TestActorProvider Actor { get; }

    /// <summary>The <see cref="IWorkerTickSignal"/> instance registered with the host. Tests typically observe it via <see cref="WaitForTickAsync(TimeSpan?, CancellationToken)"/> rather than calling it directly.</summary>
    public IWorkerTickSignal TickSignal => _tickSignal;

    /// <summary>
    /// Builds the harness: applies defaults, runs the
    /// <see cref="WorkerHarnessOptions.ConfigureServices(System.Action{IServiceCollection})"/>
    /// callbacks, registers <typeparamref name="TWorker"/> as a hosted service, builds the host,
    /// runs the <see cref="WorkerHarnessOptions.SeedAsync(System.Func{IServiceProvider, CancellationToken, Task})"/>
    /// callbacks, and starts the host when <see cref="WorkerHarnessOptions.AutoStart"/> is set.
    /// </summary>
    /// <param name="configure">Optional configuration delegate.</param>
    /// <param name="cancellationToken">A token to observe during host build, seeding, and (when applicable) start.</param>
    /// <returns>An initialized harness ready for time advances, event waits, and tick waits.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an <see cref="IHostedService"/> for <typeparamref name="TWorker"/> is already registered by a user <see cref="WorkerHarnessOptions.ConfigureServices(System.Action{IServiceCollection})"/> callback.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1000:Do not declare static members on generic types",
        Justification = "WorkerHarness<TWorker>.CreateAsync is the discoverable factory entry point; collocating it with the generic type keeps the call-site shape WorkerHarness<MyWorker>.CreateAsync(...) symmetric with WebApplicationFactory<TEntryPoint>.")]
    public static async Task<WorkerHarness<TWorker>> CreateAsync(
        Action<WorkerHarnessOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var options = new WorkerHarnessOptions();
        configure?.Invoke(options);

        var time = new FakeTimeProvider(options.InitialTime);
        var capture = new DomainEventCapture();
        var tickSignal = new WorkerTickSignal();
        var actor = new TestActorProvider(options.SystemActor);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(capture);
        builder.Services.AddSingleton<IWorkerTickSignal>(tickSignal);
        // Scoped to match production IActorProvider registrations; the AsyncLocal flow in
        // TestActorProvider means a single shared instance still gives each scope its own
        // ambient actor when tests use WithActor(...).
        builder.Services.Replace(ServiceDescriptor.Scoped<IActorProvider>(_ => actor));

        // Open-generic capture handler: DI closes IDomainEventHandler<X> against this
        // implementation for every concrete X the publisher resolves, alongside any
        // production handler the user registered for the same event.
        builder.Services.Add(ServiceDescriptor.Scoped(
            typeof(IDomainEventHandler<>),
            typeof(DomainEventCaptureHandler<>)));

        foreach (var configureServices in options.ConfigureServicesDelegates)
            configureServices(builder.Services);

        foreach (var configureLogging in options.ConfigureLoggingDelegates)
            configureLogging(builder.Logging);

        ThrowIfWorkerAlreadyRegistered(builder.Services);
        builder.Services.AddHostedService<TWorker>();

        var host = builder.Build();

        var harness = new WorkerHarness<TWorker>(
            host,
            time,
            actor,
            capture,
            tickSignal,
            options.DefaultWaitTimeout);

        try
        {
            foreach (var seed in options.SeedDelegates)
            {
                await using var scope = host.Services.CreateAsyncScope();
                await seed(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }

            if (options.AutoStart)
                await harness.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await harness.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return harness;
    }

    /// <summary>
    /// Starts the worker host. On the success path, subsequent calls are no-ops and safe to
    /// repeat. If the underlying <see cref="IHost.StartAsync(CancellationToken)"/> throws, the
    /// harness drives a best-effort <see cref="IHost.StopAsync(CancellationToken)"/> so any
    /// hosted services that started before the failure observe their stop hook, and rethrows
    /// the original exception. After a failed start the harness should be discarded — the
    /// host's hosted services have already observed a stop transition and re-starting them
    /// from the same harness is not a supported scenario.
    /// </summary>
    /// <param name="cancellationToken">A token to observe during start.</param>
    /// <remarks>
    /// <para>
    /// <see cref="IHost.StartAsync(CancellationToken)"/> returns as soon as the
    /// <see cref="BackgroundService"/>'s <see cref="BackgroundService.ExecuteAsync(CancellationToken)"/>
    /// has been scheduled onto the thread pool, NOT after the worker has registered its first
    /// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> /
    /// <see cref="System.Threading.PeriodicTimer"/> callback with the harness's
    /// <see cref="FakeTimeProvider"/>. A test that calls
    /// <c>await harness.StartAsync(); harness.Time.Advance(period);</c> back-to-back can race
    /// the worker: the <c>Advance</c> fires before any callback is registered, the worker
    /// then registers <c>Task.Delay(period, FakeTime, ct)</c> with a deadline of
    /// <c>(now + period)</c>, and the test times out waiting for a tick that needs another
    /// <c>Advance(period)</c> to fire.
    /// </para>
    /// <para>
    /// Two ways to avoid the race:
    /// <list type="number">
    ///   <item><description>(Deterministic) Have the worker register its first
    ///     <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> BEFORE calling
    ///     <see cref="IWorkerTickSignal.SignalAsync(string, CancellationToken)"/> with a known
    ///     name (e.g. <c>"ready"</c>), then <c>await</c> the saved delay. The
    ///     <c>Task.Delay(TimeSpan, TimeProvider, CancellationToken)</c> overload eagerly
    ///     calls <see cref="TimeProvider.CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)"/>
    ///     when invoked, so the <see cref="FakeTimeProvider"/> callback is registered before
    ///     the signal fires — by the time the test resumes from
    ///     <see cref="WaitForTickAsync(string, TimeSpan?, CancellationToken)"/> the callback
    ///     exists and the subsequent <c>Time.Advance(...)</c> is guaranteed to land on it.
    ///     Signaling FIRST and registering the delay second would still leave a gap during
    ///     which the test can advance the clock before any callback is registered.</description></item>
    ///   <item><description>(Lazy) Call <see cref="SettleAsync(TimeSpan?, CancellationToken)"/>
    ///     after <c>StartAsync</c>. This is a real-time yield so the worker's first scheduling
    ///     turn lands before the test advances the clock. Convenient for one-off tests but
    ///     adds wall-clock latency.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Reserve the "started" slot before delegating to the host so a concurrent caller
        // sees the state transition.
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;
        try
        {
            await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception startEx)
        {
            // Host.StartAsync iterates registered IHostedService instances in registration
            // order and propagates the first StartAsync exception WITHOUT rolling back
            // already-started services — so an earlier service that holds external resources
            // would leak them until the DI scope is disposed. Drive a best-effort
            // _host.StopAsync so those services observe their stop hook. Cap with the same
            // 10-second real-time budget DisposeAsync uses so a misbehaving service does not
            // hang the rethrow; capture cleanup exceptions in Data so the original start
            // failure stays the surfaced exception but a stop-time bug stays diagnosable.
            // Reset the slot so DisposeAsync does not attempt a second StopAsync on a host
            // that is already stopped — the harness is not intended to be re-started after a
            // failed start (see the XML doc on this method).
            try
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _host.StopAsync(stopCts.Token).ConfigureAwait(false);
            }
            catch (Exception stopEx)
            {
                startEx.Data["WorkerHarness.StartAsync.CleanupStopException"] = stopEx;
            }

            Volatile.Write(ref _started, 0);
            throw;
        }
    }

    /// <summary>
    /// Yields real time so the worker's <see cref="BackgroundService.ExecuteAsync(CancellationToken)"/>
    /// can take its first scheduling turn — typically enough for it to register its initial
    /// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> /
    /// <see cref="System.Threading.PeriodicTimer"/> callback with the harness's
    /// <see cref="FakeTimeProvider"/>. Call after <see cref="StartAsync(CancellationToken)"/>
    /// when the worker has no other readiness signal.
    /// </summary>
    /// <param name="duration">Optional real-time settle duration. Defaults to 200ms.</param>
    /// <param name="cancellationToken">A token to observe during the yield.</param>
    /// <remarks>
    /// For a deterministic alternative, have the worker register its first
    /// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> BEFORE calling
    /// <see cref="IWorkerTickSignal.SignalAsync(string, CancellationToken)"/> with a known
    /// name (then <c>await</c> the saved delay) and use
    /// <see cref="WaitForTickAsync(string, TimeSpan?, CancellationToken)"/> in the test. The
    /// signal must follow the <c>Task.Delay(...)</c> call so the <see cref="FakeTimeProvider"/>
    /// callback is provably registered before the test resumes and advances time.
    /// </remarks>
    public Task SettleAsync(TimeSpan? duration = null, CancellationToken cancellationToken = default) =>
        Task.Delay(duration ?? TimeSpan.FromMilliseconds(200), cancellationToken);

    /// <summary>
    /// Stops the worker host. Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    /// <param name="cancellationToken">A token to observe during stop.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Transition started→stopped atomically so a concurrent caller observes the change
        // and DisposeAsync does not double-stop the host. Restore the flag if the host's
        // own StopAsync throws so the caller can retry.
        if (Interlocked.CompareExchange(ref _started, 0, 1) != 1)
            return;
        try
        {
            await _host.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Volatile.Write(ref _started, 1);
            throw;
        }
    }

    /// <summary>
    /// Returns every captured event of type <typeparamref name="TEvent"/> in the order they were dispatched.
    /// </summary>
    /// <typeparam name="TEvent">The event type to filter by; uses runtime <c>is</c> matching so derived events appear in queries for their base type.</typeparam>
    public IReadOnlyList<TEvent> Events<TEvent>() where TEvent : IDomainEvent =>
        _capture.SnapshotOf<TEvent>();

    /// <summary>
    /// Returns every captured domain event regardless of type, in dispatch order.
    /// </summary>
    public IReadOnlyList<IDomainEvent> AllEvents => _capture.Snapshot();

    /// <summary>
    /// Waits until a domain event of type <typeparamref name="TEvent"/> is captured (or returns
    /// immediately if one was captured before the call). Returns the matching event so the test
    /// does not have to re-query <see cref="Events{TEvent}()"/>.
    /// </summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <param name="timeout">Optional override of <see cref="WorkerHarnessOptions.DefaultWaitTimeout"/>. Measured in real time.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The first matching event.</returns>
    /// <exception cref="WorkerHarnessTimeoutException">Thrown when no matching event arrives before the timeout elapses.</exception>
    public Task<TEvent> WaitForEventAsync<TEvent>(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
        => WaitForEventCoreAsync<TEvent>(predicate: null, timeout, cancellationToken);

    /// <summary>
    /// Waits until a domain event of type <typeparamref name="TEvent"/> satisfying
    /// <paramref name="predicate"/> is captured.
    /// </summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <param name="predicate">Filter applied to candidate events. The first event that returns <see langword="true"/> completes the wait.</param>
    /// <param name="timeout">Optional override of <see cref="WorkerHarnessOptions.DefaultWaitTimeout"/>. Measured in real time.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The first matching event.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
    /// <exception cref="WorkerHarnessTimeoutException">Thrown when no matching event arrives before the timeout elapses.</exception>
    public async Task<TEvent> WaitForEventAsync<TEvent>(
        Func<TEvent, bool> predicate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return await WaitForEventCoreAsync<TEvent>(predicate, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TEvent> WaitForEventCoreAsync<TEvent>(
        Func<TEvent, bool>? predicate,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        var effectiveTimeout = timeout ?? _defaultWaitTimeout;
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await _capture.WaitForAsync(predicate, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw BuildEventTimeoutException<TEvent>(effectiveTimeout, predicate is not null);
        }
    }

    /// <summary>
    /// Returns the total number of tick signals captured so far (across all names). The value
    /// minus one is the global index of the most recent signal — pass that as <c>after</c> to
    /// a cursor-less <see cref="WaitForTickAsync(int, TimeSpan?, CancellationToken)"/> call to
    /// wait for the next tick. For successive named-tick waits, prefer threading the index
    /// returned by the previous <see cref="WaitForTickAsync(string, TimeSpan?, CancellationToken)"/>
    /// call (or capturing <see cref="LastTickIndexOf(string)"/>) over computing a cursor from
    /// <see cref="TickCountOf(string)"/>, which is a per-name count and not a global index.
    /// </summary>
    public int TickCount => _tickSignal.Count;

    /// <summary>
    /// Returns the number of tick signals captured so far with the given <paramref name="name"/>.
    /// Useful for assertions such as <c>harness.TickCountOf("probe").Should().Be(3)</c>. Do
    /// <strong>not</strong> use this as a cursor for <see cref="WaitForTickAsync(string, int, TimeSpan?, CancellationToken)"/>:
    /// the <c>after</c> parameter is a global signal index, and with interleaved tick names
    /// a per-name count can fall below the global index of an already-recorded matching
    /// signal — producing a wait that returns immediately for an old tick. Use
    /// <see cref="LastTickIndexOf(string)"/> for a baseline named cursor instead.
    /// </summary>
    /// <param name="name">The tick name to count.</param>
    public int TickCountOf(string name) => _tickSignal.CountOf(name);

    /// <summary>
    /// Returns the global signal index of the most recently captured tick with the given
    /// <paramref name="name"/>, or <c>-1</c> if no such tick has been recorded. Pass the
    /// returned value as <c>after</c> to <see cref="WaitForTickAsync(string, int, TimeSpan?, CancellationToken)"/>
    /// to wait for the NEXT tick with that name, even when other tick names are interleaved
    /// in the history.
    /// </summary>
    /// <param name="name">The tick name to look up.</param>
    public int LastTickIndexOf(string name) => _tickSignal.LastIndexOf(name);

    /// <summary>
    /// Waits until <see cref="IWorkerTickSignal.SignalAsync(CancellationToken)"/> or
    /// <see cref="IWorkerTickSignal.SignalAsync(string, CancellationToken)"/> has been called at least once.
    /// Returns immediately if a signal arrived before the call. Use the
    /// <see cref="WaitForTickAsync(int, TimeSpan?, CancellationToken)"/> overload to wait for a tick
    /// that arrives AFTER an explicit cursor when running multi-tick scenarios.
    /// </summary>
    /// <param name="timeout">Optional override of <see cref="WorkerHarnessOptions.DefaultWaitTimeout"/>. Measured in real time.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The index of the tick that satisfied the wait. Pass that value to a subsequent <see cref="WaitForTickAsync(int, TimeSpan?, CancellationToken)"/> call as <c>after</c> to wait for the next tick.</returns>
    /// <exception cref="WorkerHarnessTimeoutException">Thrown when no signal arrives before the timeout elapses.</exception>
    public Task<int> WaitForTickAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        WaitForTickCoreAsync(name: null, afterIndexExclusive: -1, timeout, cancellationToken);

    /// <summary>
    /// Waits for a tick signal of any name that arrives strictly AFTER the given <paramref name="after"/>
    /// index. The typical pattern is to capture <see cref="TickCount"/> minus one before advancing
    /// time, then pass it here so the wait only matches a tick produced by the new <c>Advance</c>.
    /// </summary>
    /// <param name="after">The exclusive lower bound on the tick index — only ticks with a higher index satisfy the wait. Pass <c>-1</c> to wait for any tick (equivalent to the zero-argument overload).</param>
    /// <param name="timeout">Optional override of <see cref="WorkerHarnessOptions.DefaultWaitTimeout"/>. Measured in real time.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The index of the tick that satisfied the wait.</returns>
    /// <exception cref="WorkerHarnessTimeoutException">Thrown when no qualifying tick arrives before the timeout elapses.</exception>
    public Task<int> WaitForTickAsync(int after, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        WaitForTickCoreAsync(name: null, afterIndexExclusive: after, timeout, cancellationToken);

    /// <summary>
    /// Waits until a tick signal with the specified <paramref name="name"/> arrives. Returns
    /// immediately if a matching signal is already in the history. Use the
    /// <see cref="WaitForTickAsync(string, int, TimeSpan?, CancellationToken)"/> overload when the
    /// worker emits the same tick name on every iteration so successive waits do not match older
    /// recorded ticks.
    /// </summary>
    /// <param name="name">The expected tick name; the harness compares with ordinal equality.</param>
    /// <param name="timeout">Optional override of <see cref="WorkerHarnessOptions.DefaultWaitTimeout"/>. Measured in real time.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The index of the tick that satisfied the wait.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="WorkerHarnessTimeoutException">Thrown when no matching tick arrives before the timeout elapses.</exception>
    public Task<int> WaitForTickAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return WaitForTickCoreAsync(name, afterIndexExclusive: -1, timeout, cancellationToken);
    }

    /// <summary>
    /// Waits for a tick signal of the given <paramref name="name"/> that arrives strictly AFTER
    /// the given <paramref name="after"/> index. Use this for workers that emit the same tick
    /// name every iteration: capture <see cref="TickCount"/> minus one before advancing time
    /// (or use the index returned by the previous <c>WaitForTickAsync</c> call), then pass it
    /// here so successive waits do not match an older recorded tick.
    /// </summary>
    /// <param name="name">The expected tick name; the harness compares with ordinal equality.</param>
    /// <param name="after">The exclusive lower bound on the tick index. Pass <c>-1</c> to wait for any matching tick (equivalent to the cursor-less overload).</param>
    /// <param name="timeout">Optional override of <see cref="WorkerHarnessOptions.DefaultWaitTimeout"/>. Measured in real time.</param>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>The index of the tick that satisfied the wait.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="WorkerHarnessTimeoutException">Thrown when no qualifying tick arrives before the timeout elapses.</exception>
    public Task<int> WaitForTickAsync(string name, int after, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return WaitForTickCoreAsync(name, afterIndexExclusive: after, timeout, cancellationToken);
    }

    private async Task<int> WaitForTickCoreAsync(string? name, int afterIndexExclusive, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var effectiveTimeout = timeout ?? _defaultWaitTimeout;
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await _tickSignal.WaitForAsync(name, afterIndexExclusive, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw BuildTickTimeoutException(name, effectiveTimeout);
        }
    }

    /// <summary>
    /// Stops the host (if started) and disposes the underlying <see cref="IHost"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            if (Interlocked.CompareExchange(ref _started, 0, 1) == 1)
            {
                // The host may have been stopped already; StopAsync is idempotent at the
                // host level too. Cap stop with a short real-time budget so a misbehaving
                // worker does not hang test teardown forever.
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await _host.StopAsync(stopCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* tests should still tear down */ }
            }
        }
        finally
        {
            if (_host is IAsyncDisposable hostAsync)
                await hostAsync.DisposeAsync().ConfigureAwait(false);
            else
                _host.Dispose();
        }
    }

    private WorkerHarnessTimeoutException BuildEventTimeoutException<TEvent>(TimeSpan timeout, bool hadPredicate)
        where TEvent : IDomainEvent
    {
        var capturedSameType = _capture.SnapshotOf<TEvent>().Count;
        var capturedTotal = _capture.Snapshot().Count;

        var detail = hadPredicate
            ? $"No event of type '{typeof(TEvent).Name}' matched the predicate within {timeout}."
            : $"No event of type '{typeof(TEvent).Name}' was captured within {timeout}.";

        return new WorkerHarnessTimeoutException(
            $"{detail} Captured {capturedSameType} event(s) of this type and {capturedTotal} domain event(s) in total. " +
            "If the worker is expected to emit this event after Time.Advance(...), confirm that the production " +
            "composition root registers AddDomainEventDispatch() (or one of the assembly-scanning overloads) so the " +
            "mediator publishes events. Worker-harness tests inherit the production registration; the harness does " +
            "not register domain-event dispatch on its own.");
    }

    private WorkerHarnessTimeoutException BuildTickTimeoutException(string? name, TimeSpan timeout)
    {
        var signals = _tickSignal.Snapshot();
        var signalSummary = signals.Count == 0
            ? "no tick signals recorded"
            : $"recorded tick names: [{string.Join(", ", signals.Select(s => string.IsNullOrEmpty(s) ? "<empty>" : s))}]";

        var detail = name is null
            ? $"No worker tick signal arrived within {timeout}."
            : $"No worker tick signal named '{name}' arrived within {timeout}.";

        return new WorkerHarnessTimeoutException(
            $"{detail} {signalSummary}. The worker must resolve IWorkerTickSignal from DI and call " +
            "SignalAsync(...) at the end of each tick for this primitive to release. Prefer " +
            "WaitForEventAsync<TEvent> when the tick emits a domain event.");
    }

    private static void ThrowIfWorkerAlreadyRegistered(IServiceCollection services)
    {
        var workerType = typeof(TWorker);
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(IHostedService))
                continue;

            if (DescriptorRegistersWorker(descriptor, workerType))
            {
                throw new InvalidOperationException(
                    $"WorkerHarness<{workerType.Name}> owns the IHostedService registration for the worker under test, " +
                    $"but '{workerType.FullName}' was already registered as an IHostedService inside ConfigureServices(...). " +
                    "Remove the duplicate AddHostedService call from the test's ConfigureServices callback — the harness " +
                    "registers the worker for you.");
            }
        }
    }

    private static bool DescriptorRegistersWorker(ServiceDescriptor descriptor, Type workerType)
    {
        if (descriptor.ImplementationType == workerType)
            return true;

        if (descriptor.ImplementationInstance?.GetType() == workerType)
            return true;

        var factory = descriptor.ImplementationFactory;
        if (factory is null)
            return false;

        // AddHostedService<TWorker>(sp => ...) stores the factory as
        // Func<IServiceProvider, TWorker> (implicitly cast to Func<IServiceProvider, object>);
        // the runtime delegate type preserves the closed generic argument.
        var factoryType = factory.GetType();
        if (factoryType.IsGenericType)
        {
            var args = factoryType.GetGenericArguments();
            if (args.Length == 2 && args[1] == workerType)
                return true;
        }

        // Hand-rolled Func<IServiceProvider, object> lambdas lose the worker type in the
        // delegate signature; the method's declared return type is the next-best signal.
        return factory.Method.ReturnType == workerType;
    }
}