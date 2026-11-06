namespace Trellis.Testing;

/// <summary>Records parameterless callback invocations without changing their outcomes.</summary>
/// <remarks>Use one recorder per test. Recording is thread-safe; callbacks are not serialized.</remarks>
public sealed class CallbackRecorder
{
    private int _callCount;

    /// <summary>Gets the number of callback entries, including calls that throw.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>Records one invocation.</summary>
    public void Record() => Interlocked.Increment(ref _callCount);

    /// <summary>Wraps a callback, recording each entry before invoking it.</summary>
    /// <param name="callback">The callback whose outcomes are preserved unchanged.</param>
    /// <returns>A recording wrapper.</returns>
    public Action Wrap(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return () =>
        {
            Record();
            callback();
        };
    }

    /// <summary>Wraps a function without awaiting, replacing, or inspecting its returned value.</summary>
    /// <typeparam name="TResult">The return type, which may itself be Task or ValueTask.</typeparam>
    /// <param name="callback">The function whose outcomes are preserved unchanged.</param>
    /// <returns>A recording wrapper.</returns>
    public Func<TResult> Wrap<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return () =>
        {
            Record();
            return callback();
        };
    }

    /// <summary>Creates a callback that records its entry and then throws, for code paths that must never invoke it.</summary>
    /// <param name="because">The reason the callback must not run; used as the exception message.</param>
    /// <returns>A recording callback that always throws <see cref="InvalidOperationException"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="because"/> is null, empty or whitespace.</exception>
    public Action Forbidden(string because)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);
        return () =>
        {
            Record();
            throw new InvalidOperationException(because);
        };
    }

    /// <summary>Creates a function that records its entry and then throws, for code paths that must never invoke it.</summary>
    /// <typeparam name="TResult">The return type the code under test expects.</typeparam>
    /// <param name="because">The reason the callback must not run; used as the exception message.</param>
    /// <returns>A recording function that always throws <see cref="InvalidOperationException"/>.</returns>
    /// <remarks>The function throws synchronously when invoked, even when <typeparamref name="TResult"/> is a Task or ValueTask; it never returns a faulted task.</remarks>
    /// <exception cref="ArgumentException"><paramref name="because"/> is null, empty or whitespace.</exception>
    public Func<TResult> Forbidden<TResult>(string because)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);
        return () =>
        {
            Record();
            throw new InvalidOperationException(because);
        };
    }
}

/// <summary>Records callback arguments in entry order without changing callback outcomes.</summary>
/// <typeparam name="TArgument">The argument type. Use a tuple for multiple arguments.</typeparam>
/// <remarks>
/// Recording is thread-safe; callbacks are not serialized. Arguments are captured without cloning.
/// Concurrent entry order is recording order, not callback completion order.
/// </remarks>
public sealed class CallbackRecorder<TArgument>
{
    private readonly Lock _lock = new();
    private readonly List<TArgument> _arguments = [];

    /// <summary>Gets the number of callback entries, including calls that throw.</summary>
    public int CallCount
    {
        get
        {
            lock (_lock)
                return _arguments.Count;
        }
    }

    /// <summary>Gets a snapshot of the arguments in recorded entry order.</summary>
    public IReadOnlyList<TArgument> Arguments
    {
        get
        {
            lock (_lock)
                return _arguments.ToArray();
        }
    }

    /// <summary>Records one argument before the callback executes.</summary>
    /// <param name="argument">The argument to capture without cloning.</param>
    public void Record(TArgument argument)
    {
        lock (_lock)
            _arguments.Add(argument);
    }

    /// <summary>Wraps an action, recording its argument before invoking it.</summary>
    /// <param name="callback">The callback whose outcomes are preserved unchanged.</param>
    /// <returns>A recording wrapper.</returns>
    public Action<TArgument> Wrap(Action<TArgument> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return argument =>
        {
            Record(argument);
            callback(argument);
        };
    }

    /// <summary>Wraps a function without awaiting, replacing, or inspecting its returned value.</summary>
    /// <typeparam name="TResult">The return type, which may itself be Task or ValueTask.</typeparam>
    /// <param name="callback">The function whose outcomes are preserved unchanged.</param>
    /// <returns>A recording wrapper.</returns>
    public Func<TArgument, TResult> Wrap<TResult>(Func<TArgument, TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return argument =>
        {
            Record(argument);
            return callback(argument);
        };
    }

    /// <summary>Creates a callback that records its argument and then throws, for code paths that must never invoke it.</summary>
    /// <param name="because">The reason the callback must not run; used as the exception message.</param>
    /// <returns>A recording callback that always throws <see cref="InvalidOperationException"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="because"/> is null, empty or whitespace.</exception>
    public Action<TArgument> Forbidden(string because)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);
        return argument =>
        {
            Record(argument);
            throw new InvalidOperationException(because);
        };
    }

    /// <summary>Creates a function that records its argument and then throws, for code paths that must never invoke it.</summary>
    /// <typeparam name="TResult">The return type the code under test expects.</typeparam>
    /// <param name="because">The reason the callback must not run; used as the exception message.</param>
    /// <returns>A recording function that always throws <see cref="InvalidOperationException"/>.</returns>
    /// <remarks>The function throws synchronously when invoked, even when <typeparamref name="TResult"/> is a Task or ValueTask; it never returns a faulted task.</remarks>
    /// <exception cref="ArgumentException"><paramref name="because"/> is null, empty or whitespace.</exception>
    public Func<TArgument, TResult> Forbidden<TResult>(string because)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);
        return argument =>
        {
            Record(argument);
            throw new InvalidOperationException(because);
        };
    }
}