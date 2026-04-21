namespace Trellis.StateMachine;

using global::Stateless;
using Trellis;

/// <summary>
/// A lazy wrapper around <see cref="StateMachine{TState, TTrigger}"/> that defers machine
/// construction until first use, solving the EF Core materialization problem.
/// </summary>
/// <typeparam name="TState">The type representing the states of the state machine.</typeparam>
/// <typeparam name="TTrigger">The type representing the triggers/events of the state machine.</typeparam>
/// <remarks>
/// <para>
/// EF Core invokes the parameterless constructor before populating entity properties.
/// If a state machine is configured eagerly in the constructor using a <c>stateAccessor</c>
/// lambda (e.g., <c>() =&gt; Status</c>), the accessor reads a default or uninitialized
/// value — reference-type and value-object states throw, while enum states silently
/// start the machine in the wrong state. This forces aggregates to use a manual
/// null-coalescing pattern: <c>_machine ??= ConfigureStateMachine()</c>.
/// </para>
/// <para>
/// <see cref="LazyStateMachine{TState, TTrigger}"/> eliminates that boilerplate by deferring
/// both the <c>stateAccessor</c>/<c>stateMutator</c> invocation and the machine configuration
/// until the first call to <see cref="FireResult"/> or <see cref="Machine"/>.
/// </para>
/// <para>
/// This type is not thread-safe, consistent with the underlying
/// <see cref="StateMachine{TState, TTrigger}"/>. DDD aggregates are single-threaded
/// consistency boundaries; concurrent access to the same instance is a design error.
/// </para>
/// <para>
/// Usage:
/// <code>
/// private readonly LazyStateMachine&lt;OrderStatus, string&gt; _machine;
///
/// public Order()
/// {
///     _machine = new LazyStateMachine&lt;OrderStatus, string&gt;(
///         () =&gt; Status,
///         s =&gt; Status = s,
///         ConfigureStateMachine);
/// }
///
/// private static void ConfigureStateMachine(StateMachine&lt;OrderStatus, string&gt; machine)
/// {
///     machine.Configure(OrderStatus.Draft)
///         .Permit("submit", OrderStatus.Submitted);
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class LazyStateMachine<TState, TTrigger>
    where TState : notnull
    where TTrigger : notnull
{
    private readonly Func<TState> _stateAccessor;
    private readonly Action<TState> _stateMutator;
    private readonly Action<StateMachine<TState, TTrigger>> _configure;
    private StateMachine<TState, TTrigger>? _machine;

    /// <summary>
    /// Initializes a new instance of the <see cref="LazyStateMachine{TState, TTrigger}"/> class.
    /// </summary>
    /// <param name="stateAccessor">A function that returns the current state. Not invoked until first use.</param>
    /// <param name="stateMutator">An action that sets the current state. Not invoked until first use.</param>
    /// <param name="configure">
    /// A callback that configures the state machine's transitions.
    /// Invoked once, when the machine is first accessed.
    /// </param>
    public LazyStateMachine(
        Func<TState> stateAccessor,
        Action<TState> stateMutator,
        Action<StateMachine<TState, TTrigger>> configure)
    {
        ArgumentNullException.ThrowIfNull(stateAccessor);
        ArgumentNullException.ThrowIfNull(stateMutator);
        ArgumentNullException.ThrowIfNull(configure);

        _stateAccessor = stateAccessor;
        _stateMutator = stateMutator;
        _configure = configure;
    }

    /// <summary>
    /// Gets the underlying <see cref="StateMachine{TState, TTrigger}"/>, creating and configuring it on first access.
    /// </summary>
    /// <remarks>
    /// The <c>stateAccessor</c> and <c>stateMutator</c> lambdas are first invoked when this property is accessed,
    /// ensuring entity properties are fully populated by EF Core before the machine reads state.
    /// </remarks>
    public StateMachine<TState, TTrigger> Machine =>
        _machine ??= CreateMachine();

    /// <summary>
    /// Fires the specified trigger and returns the new state as a <see cref="Result{TState}"/>.
    /// </summary>
    /// <param name="trigger">The trigger to fire.</param>
    /// <returns>
    /// A <see cref="Result{TState}"/> containing the new state if the transition is valid,
    /// or an <see cref="Error.Conflict"/> if the trigger cannot be fired from the current state.
    /// </returns>
    /// <remarks>
    /// Delegates to <see cref="StateMachineExtensions.FireResult{TState, TTrigger}"/>.
    /// On first call, the underlying machine is lazily created and configured.
    /// </remarks>
    public Result<TState> FireResult(TTrigger trigger) =>
        Machine.FireResult(trigger);

    private StateMachine<TState, TTrigger> CreateMachine()
    {
        var machine = new StateMachine<TState, TTrigger>(_stateAccessor, _stateMutator);
        _configure(machine);
        return machine;
    }
}