namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Abstraction over the commit boundary for staged changes.
/// Repositories stage changes; calling <see cref="CommitAsync"/> persists them.
/// <para>
/// In the standard Trellis pipeline, commit is handled automatically by
/// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> after a successful handler.
/// Inject <see cref="IUnitOfWork"/> directly only in non-pipeline scenarios
/// (background jobs, integration tests, etc.).
/// </para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all staged changes to the database.
    /// Returns <see cref="Result{Unit}"/> to surface concurrency, duplicate-key,
    /// and foreign-key errors as <see cref="Error"/> instead of exceptions.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{Unit}"/> representing success or failure.</returns>
    Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default);
}
