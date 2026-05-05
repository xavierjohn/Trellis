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
    /// Returns <see cref="Result{TValue}"/> with <see cref="Unit"/> to surface concurrency, duplicate-key,
    /// and foreign-key errors as <see cref="Error"/> instead of exceptions.
    /// </summary>
    /// <remarks>
    /// When called inside a nested <see cref="BeginScope"/> scope (i.e. depth > 1),
    /// implementations should defer the actual database write and return success without
    /// touching the database; only the outermost scope's <see cref="CommitAsync"/> call
    /// should persist staged changes. This prevents a successful inner command from
    /// committing a partially-completed outer command's staged changes.
    /// </remarks>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{TValue}"/> with <see cref="Unit"/> representing success or failure.</returns>
    Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a unit-of-work scope so that <see cref="CommitAsync"/> calls inside nested scopes
    /// defer until only the outermost scope remains. The Trellis pipeline's
    /// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> wraps every command in a
    /// scope so that a successful inner command does not commit a partially-completed outer
    /// command's staged changes.
    /// </summary>
    /// <returns>A disposable handle. Dispose ends the scope by decrementing the depth counter;
    /// disposal itself does not trigger a commit.</returns>
    /// <remarks>
    /// <para>
    /// <b>Nested-command semantics.</b> Within a nested command's pipeline, a successful inner
    /// handler's <see cref="CommitAsync"/> call is a no-op (returns <see cref="Result.Ok()"/>
    /// without touching the database) because depth &gt; 1. The actual database write happens
    /// when the **outer** <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> calls
    /// <see cref="CommitAsync"/> at depth == 1 — that call is still inside the outermost
    /// <c>using</c> scope. Disposing the outermost scope afterwards just decrements the counter
    /// to 0; it does **not** itself invoke <see cref="CommitAsync"/>. At commit time, both the
    /// outer and inner staged changes are persisted atomically through the shared
    /// <c>DbContext</c>'s implicit transaction.
    /// </para>
    /// <para>
    /// <b>Caveat.</b> If the inner command returns a failure but the outer handler chooses to
    /// ignore it and returns success anyway, the outer's commit will persist any changes the
    /// inner staged before failing. The unit-of-work is shared with the outer's
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>; per-scope rollback of staged
    /// changes is not supported. Handlers that need to discard inner failures' staged work must
    /// detach the affected entities themselves.
    /// </para>
    /// </remarks>
    IDisposable BeginScope();
}