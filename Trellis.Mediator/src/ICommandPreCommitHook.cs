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
/// Exceptions and cancellation propagate through the enclosing pipeline.
/// </para>
/// <para>
/// Hooks run for each command, including nested commands whose commit is deferred to the owner.
/// Stage changes through repositories sharing the command's unit of work; do not commit, open an
/// owning scope, publish events, or perform external side effects here. A rejected hook does not
/// roll back already staged changes. Propagate nested failures and discard the failed unit of work.
/// </para>
/// <para>
/// Implementations are scoped and may be reused by sequential or nested dispatches. Keep pending
/// state dispatch-specific rather than consuming an undifferentiated scoped buffer.
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
