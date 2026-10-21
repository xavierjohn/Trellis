namespace Trellis.Mediator;

using global::Mediator;

/// <summary>
/// Validates or stages additional changes after a command handler and before its unit-of-work commit.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> awaits hooks sequentially in
/// registration order for successful responses and persist-on-failure responses. The first hook
/// failure replaces the response and prevents the commit. Ordinary handler failures skip all hooks.
/// Every hook veto, exception, or cancellation permanently marks the unit of work uncommittable.
/// Exceptions and cancellation still propagate through the enclosing pipeline.
/// </para>
/// <para>
/// Hooks run for each command, including nested commands whose commit is deferred to the owner.
/// Stage changes through repositories sharing the command's unit of work; do not commit, open an
/// owning scope, publish events, or perform external side effects here. A rejected hook does not
/// roll back already staged changes, but blocks all later commits even when an outer handler ignores
/// its failure. Discard the failed unit of work and its backing store/context.
/// </para>
/// <para>
/// Implementations are scoped and may be reused by sequential or nested dispatches. Keep pending
/// state in <see cref="CommandDispatchState"/> rather than an undifferentiated scoped buffer.
/// Each hook reads and removes its own typed entry; frame cleanup also runs when handlers fail.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The command type.</typeparam>
/// <typeparam name="TResponse">The command's result type.</typeparam>
public interface ICommandPreCommitHook<in TMessage, in TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult
{
    /// <summary>
    /// Validates the command outcome and stages any additional writes before persistence.
    /// </summary>
    /// <param name="message">The command whose handler completed.</param>
    /// <param name="response">The original successful or persist-on-failure handler response.</param>
    /// <param name="cancellationToken">A token to observe before the commit.</param>
    /// <returns>
    /// <c>Result.Ok()</c> to allow persistence, or a failure that vetoes persistence, including
    /// when the handler or the hook's failure carries persist-on-failure intent.
    /// </returns>
    ValueTask<Result<Trellis.Unit>> BeforeCommitAsync(
        TMessage message,
        TResponse response,
        CancellationToken cancellationToken);
}
