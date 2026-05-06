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
/// into an <see cref="Error.UnprocessableContent"/> (HTTP 422) — the requested action is a
/// semantic rule violation against the aggregate's current state, not a concurrent-modification
/// conflict. Exceptions thrown by user-supplied entry/exit/transition actions are not swallowed.
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
    /// Fires the specified trigger on the state machine and returns the new state as a <see cref="Result{TState}"/>.
    /// </summary>
    /// <typeparam name="TState">The type representing the states of the state machine.</typeparam>
    /// <typeparam name="TTrigger">The type representing the triggers/events of the state machine.</typeparam>
    /// <param name="stateMachine">The state machine to fire the trigger on.</param>
    /// <param name="trigger">The trigger to fire.</param>
    /// <returns>
    /// A <see cref="Result{TState}"/> containing the new state if the transition is valid,
    /// or an <see cref="Error.UnprocessableContent"/> carrying a single
    /// <see cref="RuleViolation"/> with reason code <c>state.machine.invalid.transition</c>
    /// if the trigger cannot be fired from the current state (including when blocked by a guard).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Pre-checks with <see cref="StateMachine{TState, TTrigger}.CanFire(TTrigger)"/> — which
    /// honors <c>PermitIf</c>/<c>IgnoreIf</c> guards — and only invokes
    /// <see cref="StateMachine{TState, TTrigger}.Fire(TTrigger)"/> when the transition is permitted.
    /// This avoids any dependency on Stateless's exception message format and is therefore
    /// resilient to library upgrades.
    /// </para>
    /// <para>
    /// <b>HTTP semantics.</b> An invalid state-machine transition is a semantic rule violation
    /// (the aggregate cannot honor the requested action from its current state), not a
    /// concurrent-modification conflict — retry will not succeed. The returned error is therefore
    /// <see cref="Error.UnprocessableContent"/> (HTTP 422), not <see cref="Error.Conflict"/>
    /// (HTTP 409). Callers can still distinguish state-machine rejections from other 422s by
    /// matching on the <see cref="RuleViolation.ReasonCode"/> value <c>state.machine.invalid.transition</c>.
    /// </para>
    /// <para>
    /// Exceptions thrown by user entry, exit, or transition actions are not swallowed —
    /// they propagate to the caller as <see cref="InvalidOperationException"/> or whatever
    /// type the user code threw.
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
    /// // invalid.IsFailure == true; invalid.Error is Error.UnprocessableContent.
    /// </code>
    /// </example>
    public static Result<TState> FireResult<TState, TTrigger>(
        this StateMachine<TState, TTrigger> stateMachine,
        TTrigger trigger)
        where TState : notnull
        where TTrigger : notnull
    {
        if (stateMachine.CanFire(trigger))
        {
            stateMachine.Fire(trigger);
            return Result.Ok(stateMachine.State);
        }

        // Trigger is not permitted from the current state. Still invoke Fire so any
        // user-configured OnUnhandledTrigger callback runs and can apply its own policy
        // (silent suppression, custom exception, logging, etc.). If Stateless's default
        // unhandled-trigger handler is in effect it throws InvalidOperationException —
        // because we already know CanFire returned false, any InvalidOperationException
        // from this Fire call is by definition the unhandled-trigger path, so we translate
        // it to Error.UnprocessableContent (HTTP 422) — invalid state-machine transitions
        // are semantic rule violations, not concurrent-modification conflicts. Other
        // exception types (custom user handlers throwing typed exceptions) propagate untouched.
        try
        {
            stateMachine.Fire(trigger);
        }
        catch (InvalidOperationException)
        {
            var detail = $"Trigger '{trigger}' is not permitted from state '{stateMachine.State}'.";
            // Populate Detail on BOTH the outer Error.Detail AND the single RuleViolation.Detail
            // so HTTP-422 rendering surfaces the same message in both Problem Details.detail
            // (top-level, read from Error.Detail) and per-rule context (read from
            // RuleViolation.Detail). Error.UnprocessableContent.ForRule(reasonCode, detail)
            // sets RuleViolation.Detail only; the `with { Detail = detail }` lifts it to
            // Error.Detail. Trellis.Asp.ResponseFailureWriter consumes both surfaces.
            return Result.Fail<TState>(
                Error.UnprocessableContent.ForRule(
                    reasonCode: "state.machine.invalid.transition",
                    detail: detail) with
                { Detail = detail });
        }

        // Custom OnUnhandledTrigger swallowed the trigger — surface the current state as
        // success. The state is read AFTER the callback runs; it is normally unchanged but
        // a callback that mutates or reroutes state will surface the resulting state, not
        // a snapshot of the pre-call state.
        return Result.Ok(stateMachine.State);
    }
}