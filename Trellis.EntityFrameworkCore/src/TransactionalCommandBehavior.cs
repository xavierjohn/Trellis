namespace Trellis.EntityFrameworkCore;

using global::Mediator;

/// <summary>
/// Pipeline behavior that automatically commits staged changes after a successful command handler.
/// <para>
/// The behavior calls <c>next</c> to run the handler (which stages changes via repositories),
/// then calls <see cref="IUnitOfWork.CommitAsync"/> if the handler returned success.
/// If the handler returned a failure <see cref="Result{T}"/>, no commit occurs and the
/// staged changes are discarded when the <see cref="Microsoft.EntityFrameworkCore.DbContext"/> is disposed.
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
/// </summary>
/// <typeparam name="TMessage">The command type.</typeparam>
/// <typeparam name="TResponse">The result type, constrained to <see cref="IResult"/> and <see cref="IFailureFactory{TSelf}"/>.</typeparam>
public sealed class TransactionalCommandBehavior<TMessage, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : ICommand<TResponse>
    where TResponse : IResult, IFailureFactory<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var result = await next(message, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            var commitResult = await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (commitResult.TryGetError(out var error))
                return TResponse.CreateFailure(error);
        }

        return result;
    }
}