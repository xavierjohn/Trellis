namespace Trellis;

/// <summary>
/// Abstraction over the commit boundary for staged changes.
/// Repositories stage changes; calling <see cref="CommitAsync"/> persists them.
/// <para>
/// In the standard Trellis pipeline, commit is handled automatically by
/// <c>TransactionalCommandBehavior</c> after a successful handler.
/// Inject <see cref="IUnitOfWork"/> directly only in non-pipeline scenarios
/// (background jobs, integration tests, etc.).
/// </para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all staged changes to the backing store.
    /// Returns <see cref="Result{TValue}"/> with <see cref="Unit"/> to surface concurrency, duplicate-key,
    /// and foreign-key errors as <see cref="Error"/> instead of exceptions.
    /// </summary>
    /// <remarks>
    /// When called inside a nested <see cref="BeginScope"/> scope (i.e. depth > 1),
    /// implementations should defer the actual write and return success without
    /// touching the store; only the outermost scope's <see cref="CommitAsync"/> call
    /// should persist staged changes. This prevents a successful inner command from
    /// committing a partially-completed outer command's staged changes.
    /// </remarks>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{TValue}"/> with <see cref="Unit"/> representing success or failure.</returns>
    Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a unit-of-work scope so that <see cref="CommitAsync"/> calls inside nested scopes
    /// defer until only the outermost scope remains. The Trellis pipeline's
    /// <c>TransactionalCommandBehavior</c> wraps every command in a scope so that a successful
    /// inner command does not commit a partially-completed outer command's staged changes.
    /// </summary>
    /// <returns>A disposable handle. Dispose ends the scope by decrementing the depth counter;
    /// disposal itself does not trigger a commit.</returns>
    /// <remarks>
    /// <para>
    /// <b>Nested-command semantics.</b> Within a nested command's pipeline, a successful inner
    /// handler's <see cref="CommitAsync"/> call is a no-op (returns <see cref="Result.Ok()"/>
    /// without touching the store) because depth &gt; 1. The actual write happens when the
    /// **outer** <c>TransactionalCommandBehavior</c> calls <see cref="CommitAsync"/> at depth == 1
    /// — that call is still inside the outermost <c>using</c> scope. Disposing the outermost scope
    /// afterwards just decrements the counter to 0; it does **not** itself invoke
    /// <see cref="CommitAsync"/>. At commit time, both the outer and inner staged changes are
    /// persisted atomically by the implementation.
    /// </para>
    /// <para>
    /// <b>Caveat.</b> If the inner command returns a failure but the outer handler chooses to
    /// ignore it and returns success anyway, the outer's commit will persist any changes the
    /// inner staged before failing. The unit-of-work is shared across the scope; per-scope
    /// rollback of staged changes is not supported. Handlers that need to discard inner failures'
    /// staged work must detach the affected entities themselves.
    /// </para>
    /// <para>
    /// <b>Concurrency.</b> The depth counter is per-<see cref="IUnitOfWork"/>-instance — i.e.
    /// per DI scope. Concurrent commands sent on the **same** scoped <see cref="IUnitOfWork"/>
    /// (e.g. via <c>Task.WhenAll(mediator.Send(a), mediator.Send(b))</c> from inside a handler)
    /// are **not supported**: their scopes share the counter and one command's commit can be
    /// suppressed by the other's open scope, or vice versa. Many stores (an EF Core
    /// <c>DbContext</c>, for example) are not thread-safe regardless, so concurrent dispatch on a
    /// single request scope is unsafe. To run commands in parallel, give each one its own DI scope
    /// (e.g. <c>IServiceScopeFactory</c>) so each resolves its own <see cref="IUnitOfWork"/>.
    /// </para>
    /// </remarks>
    IDisposable BeginScope();
}