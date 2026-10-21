namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Accesses typed application state belonging to the active transactional command dispatch.
/// </summary>
/// <remarks>
/// The transactional behavior owns each frame. Nested commands have separate entries and restore
/// the parent's entries on exit. Every exit clears the frame, including failure and cancellation.
/// Use a distinct state type for each owner; do not retain state across dispatches.
/// </remarks>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "The injectable accessor exposes ambient state rather than owning a dispatch frame.")]
public sealed class CommandDispatchState
{
    private static readonly AsyncLocal<Frame?> s_current = new();

    internal static IDisposable Begin()
    {
        var frame = new Frame(s_current.Value);
        s_current.Value = frame;
        return frame;
    }

    /// <summary>
    /// Gets the entry for <typeparamref name="TState"/>, creating it only when absent.
    /// </summary>
    /// <typeparam name="TState">The application-owned state type.</typeparam>
    /// <param name="factory">Creates a non-null entry when this dispatch has none.</param>
    /// <returns>The entry belonging to the current dispatch.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown outside an active command frame or when the factory returns null.
    /// </exception>
    public TState GetOrAdd<TState>(Func<TState> factory) where TState : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        return GetRequiredFrame().GetOrAdd(factory);
    }

    /// <summary>
    /// Looks up this dispatch's entry without creating one.
    /// </summary>
    /// <typeparam name="TState">The application-owned state type.</typeparam>
    /// <param name="state">The entry when present, otherwise null.</param>
    /// <returns>Whether this dispatch contains the entry.</returns>
    /// <exception cref="InvalidOperationException">Thrown outside an active command frame.</exception>
    public bool TryGet<TState>([NotNullWhen(true)] out TState? state) where TState : class =>
        GetRequiredFrame().TryGet(out state);

    /// <summary>
    /// Removes only the entry for <typeparamref name="TState"/> from this dispatch.
    /// </summary>
    /// <typeparam name="TState">The application-owned state type.</typeparam>
    /// <returns>Whether an entry was removed.</returns>
    /// <exception cref="InvalidOperationException">Thrown outside an active command frame.</exception>
    public bool Remove<TState>() where TState : class => GetRequiredFrame().Remove<TState>();

    private static Frame GetRequiredFrame() =>
        s_current.Value ?? throw new InvalidOperationException(
            "Command dispatch state requires an active transactional command. " +
            "Invoke the handler through TransactionalCommandBehavior.");

    private sealed class Frame(Frame? previous) : IDisposable
    {
        private readonly object _stateLock = new();
        private Dictionary<Type, object>? _entries;
        private bool _active = true;

        internal TState GetOrAdd<TState>(Func<TState> factory) where TState : class
        {
            lock (_stateLock)
            {
                EnsureActive();
                if (_entries is not null && _entries.TryGetValue(typeof(TState), out var existing))
                    return (TState)existing;

                var state = factory() ?? throw new InvalidOperationException(
                    "A command dispatch state factory must not return null.");
                (_entries ??= []).Add(typeof(TState), state);
                return state;
            }
        }

        internal bool TryGet<TState>([NotNullWhen(true)] out TState? state) where TState : class
        {
            lock (_stateLock)
            {
                EnsureActive();
                if (_entries is not null && _entries.TryGetValue(typeof(TState), out var existing))
                {
                    state = (TState)existing;
                    return true;
                }

                state = null;
                return false;
            }
        }

        internal bool Remove<TState>() where TState : class
        {
            lock (_stateLock)
            {
                EnsureActive();
                return _entries?.Remove(typeof(TState)) ?? false;
            }
        }

        private void EnsureActive()
        {
            if (!_active)
                throw new InvalidOperationException(
                    "The command dispatch frame has expired. Do not access dispatch state from orphan tasks.");
        }

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (!_active)
                    return;
                if (!ReferenceEquals(s_current.Value, this))
                    throw new InvalidOperationException("Command dispatch frames must close in reverse order.");
                _active = false;
                _entries = null;
            }

            s_current.Value = previous;
        }
    }
}
