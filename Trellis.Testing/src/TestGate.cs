namespace Trellis.Testing;

/// <summary>
/// A rendezvous between code under test and a test: the code signals that it reached a point and waits there,
/// and the test waits (with a bound) for the arrival, makes assertions about the paused state, then releases it.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the hand-written <c>entered</c>/<c>release</c> <see cref="TaskCompletionSource"/> pair. Arrivals are
/// counted and never throw when repeated, waiting for an arrival always requires a timeout and token, and
/// <see cref="Dispose"/> releases the gate, so <c>using var gate = new TestGate();</c> never strands a background
/// flow when an assertion fails.
/// </para>
/// <para>
/// By default <see cref="Release"/> is permanent: later arrivals pass straight through, so a flow that re-enters
/// after release cannot hang. Construct with <c>permanentRelease: false</c> to make each <see cref="Release"/>
/// free only the callers currently waiting and close the gate again for later arrivals.
/// All members are thread-safe.
/// </para>
/// </remarks>
public sealed class TestGate : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _arrivalWaiters = [];
    private TaskCompletionSource _released = NewSignal();
    private int _arrivals;
    private bool _disposed;

    /// <summary>Initializes a new, closed gate.</summary>
    /// <param name="permanentRelease">
    /// True (the default) keeps the gate open after <see cref="Release"/>; false re-closes it for later arrivals.
    /// </param>
    public TestGate(bool permanentRelease = true) => ReleaseIsPermanent = permanentRelease;

    /// <summary>Gets a value indicating whether <see cref="Release"/> opens the gate for all later arrivals too.</summary>
    public bool ReleaseIsPermanent { get; }

    /// <summary>Gets how many times code under test has arrived, including arrivals that were canceled while waiting.</summary>
    public int ArrivalCount
    {
        get
        {
            lock (_lock)
                return _arrivals;
        }
    }

    /// <summary>Records an arrival without waiting. Safe to call repeatedly and from many threads.</summary>
    public void Signal() => RecordArrival();

    /// <summary>Waits until the gate is released, without recording an arrival.</summary>
    /// <param name="cancellationToken">Cancels only this wait; the gate stays closed.</param>
    /// <returns>A task that completes when the gate is released.</returns>
    public Task WaitUntilReleasedAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource released;
        lock (_lock)
            released = _released;

        return released.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Records an arrival, then waits until the gate is released. Call this from the code under test. The arrival and
    /// the release signal it waits on are captured together, so a <see cref="Release"/> that follows the arrival always
    /// frees this caller, even when the release is not permanent.
    /// </summary>
    /// <param name="cancellationToken">Cancels only this wait. The arrival is still counted.</param>
    /// <returns>A task that completes when the gate is released.</returns>
    public Task ArriveAsync(CancellationToken cancellationToken = default) =>
        RecordArrival().Task.WaitAsync(cancellationToken);

    /// <summary>Waits, with a bound, until the first arrival.</summary>
    /// <param name="timeout">The maximum wait; <see cref="TimeoutException"/> names how many arrivals were seen.</param>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>A task that completes when code under test has arrived.</returns>
    public Task WaitForArrivalAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        WaitForArrivalsAsync(1, timeout, cancellationToken);

    /// <summary>Waits, with a bound, until at least <paramref name="count"/> arrivals have been recorded.</summary>
    /// <param name="count">The number of arrivals to wait for; must be at least one.</param>
    /// <param name="timeout">The maximum wait; <see cref="TimeoutException"/> names how many arrivals were seen.</param>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>A task that completes when the arrival count has been reached.</returns>
    public async Task WaitForArrivalsAsync(int count, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var signal = NewSignal();
        lock (_lock)
        {
            if (_arrivals >= count)
                return;

            _arrivalWaiters.Add((count, signal));
        }

        try
        {
            await signal.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Expected {count} arrival(s) at the gate within {timeout}, but only {ArrivalCount} arrived.");
        }
        finally
        {
            lock (_lock)
                _arrivalWaiters.RemoveAll(waiter => ReferenceEquals(waiter.Signal, signal));
        }
    }

    /// <summary>
    /// Opens the gate for every caller currently waiting. When <see cref="ReleaseIsPermanent"/> is true the gate
    /// stays open for later arrivals; otherwise it closes again. Safe to call repeatedly.
    /// </summary>
    public void Release()
    {
        TaskCompletionSource released;
        lock (_lock)
        {
            released = _released;
            if (!ReleaseIsPermanent && !_disposed)
                _released = NewSignal();
        }

        released.TrySetResult();
    }

    /// <summary>Permanently opens the gate so no caller can remain stranded. Safe to call more than once.</summary>
    public void Dispose()
    {
        TaskCompletionSource released;
        lock (_lock)
        {
            _disposed = true;
            released = _released;
        }

        released.TrySetResult();
    }

    private TaskCompletionSource RecordArrival()
    {
        List<TaskCompletionSource>? ready = null;
        TaskCompletionSource released;
        lock (_lock)
        {
            _arrivals++;
            released = _released;
            if (_arrivalWaiters.Count > 0)
            {
                ready = [.. _arrivalWaiters.Where(waiter => waiter.Count <= _arrivals).Select(waiter => waiter.Signal)];
                _arrivalWaiters.RemoveAll(waiter => waiter.Count <= _arrivals);
            }
        }

        if (ready is not null)
        {
            foreach (var signal in ready)
                signal.TrySetResult();
        }

        return released;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}