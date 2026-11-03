namespace Trellis.Testing;

using System.Threading.Tasks.Sources;

/// <summary>A pending async source that a test explicitly completes, faults, or cancels.</summary>
/// <typeparam name="T">The value produced on completion.</typeparam>
/// <remarks>
/// <para>
/// Two modes share one vocabulary. The default (<c>new ControlledAsync&lt;T&gt;()</c>) is Task-backed: every
/// receiver shares one task and may be awaited repeatedly. <see cref="ControlledAsync.SingleUse{T}"/> returns an
/// <see cref="IValueTaskSource{TResult}"/>-backed source whose <see cref="ValueTask"/> may be consumed only once;
/// a second consumption throws and is counted in <see cref="ResultReadCount"/>, so a test can prove an
/// implementation honors the ValueTask single-consumption contract.
/// </para>
/// <para>
/// Continuations run asynchronously. Always release pending sources in test cleanup.
/// There is no implicit timeout, disposal, or cancellation of the operation under test.
/// </para>
/// </remarks>
public sealed class ControlledAsync<T>
{
    private readonly TaskCompletionSource<T>? _shared;
    private readonly SingleUseCore? _singleUse;

    /// <summary>Initializes a Task-backed source whose receivers share one task.</summary>
    public ControlledAsync()
        : this(singleUse: false)
    {
    }

    internal ControlledAsync(bool singleUse)
    {
        if (singleUse)
            _singleUse = new SingleUseCore();
        else
            _shared = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Gets a value indicating whether this source enforces ValueTask single consumption.</summary>
    public bool IsSingleUse => _singleUse is not null;

    /// <summary>Gets the shared Task receiver.</summary>
    /// <exception cref="InvalidOperationException">The source is single-use and has no shared task.</exception>
    public Task<T> Task => _shared?.Task
        ?? throw new InvalidOperationException("A single-use source has no shared Task; use ValueTask.");

    /// <summary>Gets the ValueTask receiver: Task-backed by default, or a single-consumption source.</summary>
    public ValueTask<T> ValueTask => _singleUse is { } core ? core.CreateValueTask() : new(_shared!.Task);

    /// <summary>Gets how many times a single-use ValueTask result was read; always zero for a Task-backed source.</summary>
    public int ResultReadCount => _singleUse?.ResultReadCount ?? 0;

    /// <summary>Completes the source if no terminal outcome has been set.</summary>
    /// <param name="value">The value to preserve unchanged.</param>
    /// <returns>True if this call completed the source; otherwise false.</returns>
    public bool TryComplete(T value) => _singleUse is { } core ? core.TryComplete(value) : _shared!.TrySetResult(value);

    /// <summary>Faults the source with the original exception if it is still pending.</summary>
    /// <param name="exception">The exception to propagate without wrapping.</param>
    /// <returns>True if this call faulted the source; otherwise false.</returns>
    public bool TryFail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return _singleUse is { } core ? core.TryFail(exception) : _shared!.TrySetException(exception);
    }

    /// <summary>Cancels the source, preserving the supplied token, if it is still pending.</summary>
    /// <param name="cancellationToken">The token to associate with the canceled source; it is not canceled by this method.</param>
    /// <returns>True if this call canceled the source; otherwise false.</returns>
    public bool TryCancel(CancellationToken cancellationToken = default) =>
        _singleUse is { } core ? core.TryCancel(cancellationToken) : _shared!.TrySetCanceled(cancellationToken);

    private sealed class SingleUseCore : IValueTaskSource<T>
    {
        private readonly Lock _gate = new();
        private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
        private bool _terminal;
        private int _resultReadCount;

        public int ResultReadCount => Volatile.Read(ref _resultReadCount);

        public ValueTask<T> CreateValueTask() => new(this, _core.Version);

        public bool TryComplete(T value) => Transition(() => _core.SetResult(value));

        public bool TryFail(Exception exception) => Transition(() => _core.SetException(exception));

        public bool TryCancel(CancellationToken cancellationToken) =>
            Transition(() => _core.SetException(new OperationCanceledException(cancellationToken)));

        T IValueTaskSource<T>.GetResult(short token)
        {
            if (_core.GetStatus(token) == ValueTaskSourceStatus.Pending)
                throw new InvalidOperationException("The ValueTask result was read before the source completed.");

            if (Interlocked.Increment(ref _resultReadCount) > 1)
                throw new InvalidOperationException("The ValueTask was consumed more than once.");

            return _core.GetResult(token);
        }

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource<T>.OnCompleted(
            Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);

        private bool Transition(Action apply)
        {
            lock (_gate)
            {
                if (_terminal)
                    return false;

                _terminal = true;
                apply();
                return true;
            }
        }
    }
}

/// <summary>Factory methods for <see cref="ControlledAsync{T}"/> modes that need an explicit choice.</summary>
public static class ControlledAsync
{
    /// <summary>Creates a source whose <see cref="ControlledAsync{T}.ValueTask"/> enforces single consumption.</summary>
    /// <typeparam name="T">The value produced on completion.</typeparam>
    /// <returns>A pending single-use source.</returns>
    public static ControlledAsync<T> SingleUse<T>() => new(singleUse: true);
}