namespace Trellis.Mediator;

using global::Mediator;

/// <summary>
/// Pipeline behavior that automatically commits staged changes after a successful command handler.
/// <para>
/// The behavior wraps each command invocation in an <see cref="IUnitOfWork.BeginScope"/> scope,
/// runs the handler (which stages changes via repositories), then calls
/// <see cref="IUnitOfWork.CommitAsync"/> if the handler returned success. If the handler returned
/// a failure <see cref="Result{T}"/>, no commit occurs and the staged changes are discarded when
/// the underlying store/context is disposed.
/// </para>
/// <para>
/// <b>Persist-on-failure outcomes.</b> If the response implements <see cref="IPersistOnFailure"/>
/// and the per-instance flag is <see langword="true"/> (the canonical producer is
/// <see cref="Result.FailAfterCommit{T}(Error)"/>), the commit step also runs even though the
/// result is a failure. This enables the worker-handler pattern of persisting a
/// <c>permanently_failed</c> state row alongside the failure outcome. On commit failure for a
/// persist-on-failure outcome, the commit error replaces the handler error in the returned result.
/// </para>
/// <para>
/// This behavior is constrained to <see cref="ICommand{TResponse}"/> messages — queries
/// are not wrapped and incur no overhead.
/// </para>
/// <para>
/// <b>Atomicity:</b> the unit of work persists all staged changes within a single handler atomically
/// (the shipped EF Core adapter does so through the <c>DbContext</c>'s implicit transaction). Cross-aggregate
/// operations that share the same unit of work are automatically transactional.
/// </para>
/// <para>
/// <b>Nested-command semantics.</b> When a command handler dispatches another command via
/// <c>IMediator</c>, the inner command flows through the same pipeline and the inner
/// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> opens a nested
/// <see cref="IUnitOfWork.BeginScope"/>. The inner <see cref="IUnitOfWork.CommitAsync"/> defers
/// (returns success without touching the store) until the outermost scope unwinds. This
/// prevents a successful inner command from committing a partially-completed outer command's
/// staged changes. <b>Caveat:</b> if the inner command returns a failure but the outer handler
/// ignores it and returns success, the outer's commit will persist any changes the inner staged
/// before failing — handlers that need to discard inner failures' staged work must detach the
/// affected entities themselves. Every <see cref="IUnitOfWork"/> implementation must implement
/// <see cref="IUnitOfWork.BeginScope"/> with depth-aware semantics; the shipped EF Core unit of
/// work does this with an internal depth counter.
/// </para>
/// </summary>
/// <typeparam name="TMessage">The command type.</typeparam>
/// <typeparam name="TResponse">The result type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
public sealed class TransactionalCommandBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult, IFailureFactory<TResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="TransactionalCommandBehavior{TMessage, TResponse}"/>.
    /// </summary>
    /// <param name="unitOfWork">The scoped unit of work used to commit staged changes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="unitOfWork"/> is null.</exception>
    public TransactionalCommandBehavior(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The handler is committed when the result is successful, or when the result opts in to
    /// post-handler persistence via <see cref="IPersistOnFailure.PersistOnFailure"/> = <see langword="true"/>
    /// (typically a <see cref="Result.FailAfterCommit{T}(Error)"/> outcome from a worker handler that
    /// stages a "permanently_failed" row). On commit failure for a persist-on-failure outcome, the
    /// commit error replaces the handler error — the caller must learn that the persist-on-failure
    /// guarantee was not honored.
    /// </para>
    /// </remarks>
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        using var scope = _unitOfWork.BeginScope();

        var result = await next(message, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess || result is IPersistOnFailure { PersistOnFailure: true })
        {
            var commitResult = await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (commitResult.TryGetError(out var error))
                return TResponse.CreateFailure(error);
        }

        return result;
    }
}