namespace Trellis.StateMachine;

using System;
using global::Stateless;
using Trellis;

/// <summary>
/// Provides extension methods for <see cref="StateMachine{TState, TTrigger}"/> that return
/// <see cref="Result{TValue}"/> instead of throwing on invalid transitions.
/// </summary>
/// <remarks>
/// <para>
/// These extensions pre-check the trigger with <see cref="StateMachine{TState, TTrigger}.CanFire(TTrigger)"/>
/// (which honors <c>PermitIf</c>/<c>IgnoreIf</c> guards) and translate disallowed transitions
/// into an <see cref="Error.InvariantViolation"/> (HTTP 422) — a rejected transition is a
/// domain-invariant breach against the aggregate's current state, not inbound-input validation
/// or a concurrent-modification conflict. Exceptions thrown by user-supplied entry/exit/transition
/// actions are not swallowed.
/// </para>
/// <para>
/// These extensions do not change the concurrency model of <see cref="StateMachine{TState, TTrigger}"/>.
/// Stateless state machines are not thread-safe, so concurrent calls to <see cref="FireResult{TState, TTrigger}(StateMachine{TState, TTrigger}, TTrigger)"/>
/// on the same machine instance must still be externally synchronized. Because Stateless is
/// single-threaded by contract, the <c>CanFire</c>+<c>Fire</c> pre-check pattern is race-free
/// when used as documented.
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
    /// Fires the trigger and returns the outcome as a <see cref="Result{TState}"/>. Short-circuits
    /// with <see cref="Error.InvariantViolation"/> when the transition is not permitted in the current
    /// state (i.e., <c>CanFire</c> returns <see langword="false"/>), without invoking any
    /// consumer-registered <c>OnUnhandledTrigger</c> callback. Consumers who want their
    /// <c>OnUnhandledTrigger</c> callback to run must call <c>Fire</c> directly — <c>FireResult</c>
    /// is the guarded entry point that prefers a typed <see cref="Error.InvariantViolation"/> over
    /// running side-effect code.
    /// </summary>
    /// <typeparam name="TState">The type representing the states of the state machine.</typeparam>
    /// <typeparam name="TTrigger">The type representing the triggers/events of the state machine.</typeparam>
    /// <param name="stateMachine">The state machine to fire the trigger on.</param>
    /// <param name="trigger">The trigger to fire.</param>
    /// <returns>
    /// A <see cref="Result{TState}"/> containing the new state if the transition is valid,
    /// or an <see cref="Error.InvariantViolation"/> with reason code
    /// <c>state.machine.invalid.transition</c> if the trigger cannot be fired from the current
    /// state or a guard throws <see cref="InvalidOperationException"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Pre-checks with <see cref="StateMachine{TState, TTrigger}.CanFire(TTrigger)"/> — which
    /// honors <c>PermitIf</c>/<c>IgnoreIf</c> guards — and only invokes
    /// <see cref="StateMachine{TState, TTrigger}.Fire(TTrigger)"/> when the transition is permitted.
    /// This avoids both Stateless exception-message parsing and invoking consumer
    /// <c>OnUnhandledTrigger</c> callbacks from the typed-result path.
    /// </para>
    /// <para>
    /// <b>HTTP semantics.</b> An invalid state-machine transition is a domain-invariant breach
    /// (the aggregate cannot honor the requested action from its current state), not inbound-input
    /// validation or a concurrent-modification conflict — retry will not succeed. The returned error
    /// is therefore <see cref="Error.InvariantViolation"/> (HTTP 422), not <see cref="Error.InvalidInput"/>
    /// or <see cref="Error.Conflict"/> (HTTP 409). Callers can distinguish state-machine rejections from
    /// other 422s by matching on the <c>ReasonCode</c> value <c>state.machine.invalid.transition</c>.
    /// </para>
    /// <para>
    /// <see cref="InvalidOperationException"/> thrown while evaluating a guard is converted to
    /// <see cref="Error.InvariantViolation"/>. Exceptions thrown by user entry, exit, or transition
    /// actions are not swallowed — they propagate to the caller.
    /// </para>
    /// <para>
    /// The underlying <see cref="StateMachine{TState, TTrigger}"/> remains not thread-safe,
    /// so callers must not invoke this method concurrently on the same machine instance
    /// without synchronization.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var machine = new StateMachine&lt;State, Trigger&gt;(State.Idle);
    /// machine.Configure(State.Idle).Permit(Trigger.Start, State.Running);
    ///
    /// // Valid transition
    /// Result&lt;State&gt; result = machine.FireResult(Trigger.Start);
    /// // result.IsSuccess == true; result holds State.Running.
    ///
    /// // Invalid transition — Idle has no Trigger.Start defined here.
    /// Result&lt;State&gt; invalid = machine.FireResult(Trigger.Pause);
    /// // invalid.IsFailure == true; invalid.Error is Error.InvariantViolation.
    /// </code>
    /// </example>
    public static Result<TState> FireResult<TState, TTrigger>(
        this StateMachine<TState, TTrigger> stateMachine,
        TTrigger trigger)
        where TState : notnull
        where TTrigger : notnull
    {
        ArgumentNullException.ThrowIfNull(stateMachine);

        try
        {
            if (!stateMachine.CanFire(trigger))
            {
                var detail = $"Trigger '{trigger}' is not permitted from state '{stateMachine.State}'.";
                // Do not invoke Fire here: that would run consumer OnUnhandledTrigger callbacks
                // inside the typed-result path and make their exceptions indistinguishable from
                // Stateless's default unhandled-trigger exception.
                return InvalidTransition<TState>(detail);
            }
        }
        // CanFire evaluates guards. Convert only exceptions that unwind through Stateless's
        // guard-evaluation frame; accessor and Stateless configuration failures propagate.
        catch (InvalidOperationException ex) when (WasThrownByGuardEvaluation(ex))
        {
            return InvalidTransition<TState>(ex.Message);
        }

        stateMachine.Fire(trigger);
        return Result.Ok(stateMachine.State);
    }

    private static bool WasThrownByGuardEvaluation(InvalidOperationException exception) =>
        exception.Source != typeof(StateMachine<,>).Assembly.GetName().Name
        && exception.StackTrace?.Contains("Stateless.StateMachine", StringComparison.Ordinal) == true
        && exception.StackTrace.Contains("GuardCondition", StringComparison.Ordinal);

    private static Result<TState> InvalidTransition<TState>(string detail) =>
        Result.Fail<TState>(
            Error.InvariantViolation.ForReason(
                reasonCode: "state.machine.invalid.transition",
                detail: detail));
}