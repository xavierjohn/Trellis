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
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{TValue}"/> with <see cref="Unit"/> representing success or failure.</returns>
    Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a unit-of-work scope so that <see cref="CommitAsync"/> calls inside nested scopes
    /// defer until the outermost scope unwinds. The Trellis pipeline's
    /// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> wraps every command in a
    /// scope so that a successful inner command does not commit a partially-completed outer
    /// command's staged changes.
    /// </summary>
    /// <returns>A disposable handle. Dispose ends the scope; the outermost scope's exit re-enables
    /// commit on the next <see cref="CommitAsync"/> call.</returns>
    /// <remarks>
    /// <para>
    /// <b>Nested-command semantics.</b> Within a nested command's pipeline, a successful inner
    /// handler's <see cref="CommitAsync"/> is deferred (returns <see cref="Result.Ok()"/> without
    /// touching the database) so the outer command can still abort. Only when the outermost scope
    /// is disposed and the outer handler returns success does the deferred commit actually run —
    /// at that point both the outer and inner staged changes are persisted atomically through the
    /// shared <c>DbContext</c>'s implicit transaction.
    /// </para>
    /// <para>
    /// <b>Caveat.</b> If the inner command returns a failure but the outer handler chooses to
    /// ignore it and returns success anyway, the outer's commit will persist any changes the
    /// inner staged before failing. The unit-of-work is shared with the outer's
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>; per-scope rollback of staged
    /// changes is not supported. Handlers that need to discard inner failures' staged work must
    /// detach the affected entities themselves.
    /// </para>
    /// <para>
    /// Custom <see cref="IUnitOfWork"/> implementations may rely on the default no-op scope
    /// (commit-on-every-call) by not overriding this method; doing so reproduces the v1
    /// behavior and is appropriate when the implementation does not need nested-scope safety.
    /// </para>
    /// </remarks>
    IDisposable BeginScope() => NoOpUnitOfWorkScope.Instance;
}

/// <summary>
/// Default no-op scope used by custom <see cref="IUnitOfWork"/> implementations that do not
/// implement nested-scope tracking. <see cref="EfUnitOfWork{TContext}"/> overrides
/// <see cref="IUnitOfWork.BeginScope"/> with a depth-counting scope.
/// </summary>
internal sealed class NoOpUnitOfWorkScope : IDisposable
{
    public static readonly NoOpUnitOfWorkScope Instance = new();

    private NoOpUnitOfWorkScope()
    {
    }

    public void Dispose()
    {
    }
}