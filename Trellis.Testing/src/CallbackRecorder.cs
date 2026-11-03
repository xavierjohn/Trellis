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
}