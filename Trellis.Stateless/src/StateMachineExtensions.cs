namespace Trellis.Stateless;

using System;
using global::Stateless;
using Trellis;

/// <summary>
/// Provides extension methods for <see cref="StateMachine{TState, TTrigger}"/> that return
/// <see cref="Result{TValue}"/> instead of throwing on invalid transitions.
/// </summary>
/// <remarks>
/// <para>
/// These extensions fire the trigger once and translate Stateless invalid-transition exceptions
/// into an <see cref="Error.Conflict"/> (HTTP 409) for expected failure paths — the requested
/// transition conflicts with the machine's current state.
/// </para>
/// <para>
/// These extensions do not change the concurrency model of <see cref="StateMachine{TState, TTrigger}"/>.
/// Stateless state machines are not thread-safe, so concurrent calls to <see cref="FireResult{TState, TTrigger}(StateMachine{TState, TTrigger}, TTrigger)"/>
/// on the same machine instance must still be externally synchronized.
/// </para>
/// <para>
/// Usage with Railway Oriented Programming:
/// <code>
/// var machine = new StateMachine&lt;OrderState, OrderTrigger&gt;(OrderState.New);
/// machine.Configure(OrderState.New)
///     .Permit(OrderTrigger.Submit, OrderState.Submitted);
///
/// Result&lt;OrderState&gt; result = machine.FireResult(OrderTrigger.Submit);
/// </code>
/// </para>
/// </remarks>
public static class StateMachineExtensions
{
    /// <summary>
    /// Fires the specified trigger on the state machine and returns the new state as a <see cref="Result{TState}"/>.
    /// </summary>
    /// <typeparam name="TState">The type representing the states of the state machine.</typeparam>
    /// <typeparam name="TTrigger">The type representing the triggers/events of the state machine.</typeparam>
    /// <param name="stateMachine">The state machine to fire the trigger on.</param>
    /// <param name="trigger">The trigger to fire.</param>
    /// <returns>
    /// A <see cref="Result{TState}"/> containing the new state if the transition is valid,
    /// or an <see cref="Error.Conflict"/> if the trigger cannot be fired from the current state.
    /// </returns>
    /// <remarks>
    /// This method fires the trigger once and converts Stateless invalid-transition exceptions
    /// into a failure result. Exceptions thrown by user entry, exit, or transition actions are
    /// not swallowed.
    /// The underlying <see cref="StateMachine{TState, TTrigger}"/> remains not thread-safe,
    /// so callers must not invoke this method concurrently on the same machine instance without synchronization.
    /// </remarks>
    /// <example>
    /// <code>
    /// var machine = new StateMachine&lt;State, Trigger&gt;(State.Idle);
    /// machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);
    ///
    /// // Valid transition
    /// Result&lt;State&gt; result = machine.FireResult(Trigger.Start);
    /// // result.IsSuccess == true, result.Value == State.Running
    ///
    /// // Invalid transition
    /// Result&lt;State&gt; invalid = machine.FireResult(Trigger.Start);
    /// // invalid.IsFailure == true, invalid.Error is Error.Conflict
    /// </code>
    /// </example>
    public static Result<TState> FireResult<TState, TTrigger>(
        this StateMachine<TState, TTrigger> stateMachine,
        TTrigger trigger)
        where TState : notnull
        where TTrigger : notnull
    {
        try
        {
            stateMachine.Fire(trigger);
            return Result.Ok(stateMachine.State);
        }
        catch (InvalidOperationException exception) when (IsStatelessInvalidTransition(exception))
        {
            return Result.Fail<TState>(new Error.Conflict(
                Resource: null,
                ReasonCode: "state.machine.invalid.transition")
            { Detail = exception.Message });
        }
    }

    private static bool IsStatelessInvalidTransition(InvalidOperationException exception) =>
        string.Equals(exception.Source, typeof(StateMachine<,>).Assembly.GetName().Name, StringComparison.Ordinal)
        && IsInvalidTransitionMessage(exception.Message);

    private static bool IsInvalidTransitionMessage(string message) =>
        message.StartsWith("No valid leaving transitions are permitted from state '", StringComparison.Ordinal)
        || message.Contains(" is valid for transition from state '", StringComparison.Ordinal);
}