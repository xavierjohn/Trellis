namespace Trellis.Testing.Worker;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Singleton implementation of <see cref="IWorkerTickSignal"/> registered by
/// <see cref="WorkerHarness{TWorker}"/>. Records every signal and releases any pending
/// test-side waiters when a matching name arrives.
/// </summary>
/// <remarks>
/// Parallel to <see cref="DomainEventCapture"/>: the signal list is guarded by an internal
/// lock and waiters follow the snapshot-then-register-then-recheck pattern to avoid races
/// between worker-side <see cref="SignalAsync(string, CancellationToken)"/> calls and
/// test-side <see cref="WaitForAsync(string?, int, CancellationToken)"/> calls.
/// </remarks>
internal sealed class WorkerTickSignal : IWorkerTickSignal
{
    private readonly object _gate = new();
    private readonly List<string> _signals = [];
    private readonly List<Waiter> _waiters = [];

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return [.. _signals];
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _signals.Count;
        }
    }

    public int CountOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            var count = 0;
            foreach (var s in _signals)
                if (string.Equals(s, name, StringComparison.Ordinal))
                    count++;
            return count;
        }
    }

    public int LastIndexOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            for (var i = _signals.Count - 1; i >= 0; i--)
                if (string.Equals(_signals[i], name, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }

    public ValueTask SignalAsync(CancellationToken cancellationToken = default) =>
        SignalAsync(string.Empty, cancellationToken);

    public ValueTask SignalAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        List<Waiter>? released = null;
        int releasedIndex = 0;

        lock (_gate)
        {
            _signals.Add(name);
            releasedIndex = _signals.Count - 1;

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Matches(name, releasedIndex))
                {
                    released ??= [];
                    released.Add(_waiters[i]);
                    _waiters.RemoveAt(i);
                }
            }
        }

        if (released is not null)
            foreach (var waiter in released)
                waiter.Complete(releasedIndex);

        return ValueTask.CompletedTask;
    }

    public Task<int> WaitForAsync(string? name, int afterIndexExclusive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<int> tcs;
        Waiter waiter;

        lock (_gate)
        {
            for (var i = Math.Max(0, afterIndexExclusive + 1); i < _signals.Count; i++)
            {
                if (name is null || string.Equals(_signals[i], name, StringComparison.Ordinal))
                    return Task.FromResult(i);
            }

            tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiter = new Waiter(tcs, name, afterIndexExclusive);
            _waiters.Add(waiter);
        }

        return WaitWithCancellationAsync(tcs, waiter, cancellationToken);
    }

    private async Task<int> WaitWithCancellationAsync(
        TaskCompletionSource<int> tcs,
        Waiter waiter,
        CancellationToken cancellationToken)
    {
        await using var registration = cancellationToken.Register(static state =>
        {
            var (signal, w) = ((WorkerTickSignal, Waiter))state!;
            // Only cancel when this callback wins the removal race; if SignalAsync(...) already
            // pulled the waiter out of the list, its TrySetResult is in flight and must win.
            if (signal.TryRemoveWaiter(w))
                w.Cancel();
        }, (this, waiter));

        return await tcs.Task.ConfigureAwait(false);
    }

    private bool TryRemoveWaiter(Waiter waiter)
    {
        lock (_gate)
            return _waiters.Remove(waiter);
    }

    private sealed class Waiter(TaskCompletionSource<int> tcs, string? expectedName, int afterIndexExclusive)
    {
        public bool Matches(string name, int signalIndex) =>
            signalIndex > afterIndexExclusive &&
            (expectedName is null || string.Equals(name, expectedName, StringComparison.Ordinal));

        public void Complete(int index) => tcs.TrySetResult(index);

        public void Cancel() => tcs.TrySetCanceled();
    }
}