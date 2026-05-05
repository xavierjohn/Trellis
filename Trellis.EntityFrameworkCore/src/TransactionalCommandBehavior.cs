namespace Trellis.EntityFrameworkCore;

using global::Mediator;

/// <summary>
/// Pipeline behavior that automatically commits staged changes after a successful command handler.
/// <para>
/// The behavior wraps each command invocation in an <see cref="IUnitOfWork.BeginScope"/> scope,
/// runs the handler (which stages changes via repositories), then calls
/// <see cref="IUnitOfWork.CommitAsync"/> if the handler returned success. If the handler returned
/// a failure <see cref="Result{T}"/>, no commit occurs and the staged changes are discarded when
/// the <see cref="Microsoft.EntityFrameworkCore.DbContext"/> is disposed.
/// </para>
/// <para>
/// This behavior is constrained to <see cref="ICommand{TResponse}"/> messages — queries
/// are not wrapped and incur no overhead.
/// </para>
/// <para>
/// <b>Atomicity:</b> EF Core wraps each <c>SaveChanges</c> call in an implicit database
/// transaction, so all staged changes within a single handler are committed atomically.
/// Cross-aggregate operations that share the same <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// are automatically transactional.
/// </para>
/// <para>
/// <b>Nested-command semantics.</b> When a command handler dispatches another command via
/// <c>IMediator</c>, the inner command flows through the same pipeline and the inner
/// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> opens a nested
/// <see cref="IUnitOfWork.BeginScope"/>. The inner <see cref="IUnitOfWork.CommitAsync"/> defers
/// (returns success without touching the database) until the outermost scope unwinds. This
/// prevents a successful inner command from committing a partially-completed outer command's
/// staged changes. <b>Caveat:</b> if the inner command returns a failure but the outer handler
/// ignores it and returns success, the outer's commit will persist any changes the inner staged
/// before failing — handlers that need to discard inner failures' staged work must detach the
/// affected entities themselves. Custom <see cref="IUnitOfWork"/> implementations that don't
/// override <see cref="IUnitOfWork.BeginScope"/> reproduce the v1 commit-on-every-call behavior
/// and are vulnerable to the original nested-commit bug.
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
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        using var scope = _unitOfWork.BeginScope();

        var result = await next(message, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            var commitResult = await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (commitResult.TryGetError(out var error))
                return TResponse.CreateFailure(error);
        }

        return result;
    }
}