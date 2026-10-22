namespace Trellis.Testing;

/// <summary>
/// Test double for the provider-neutral unit-of-work commit boundary.
/// </summary>
/// <remarks>
/// Models serial nested scopes and records commit calls without persisting data.
/// It does not coordinate repositories, simulate rollback, or prove database atomicity.
/// Use a separate instance for each test or dependency-injection scope; concurrent
/// commands sharing an instance are not supported.
/// </remarks>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private int _scopeDepth;

    /// <summary>
    /// Gets or sets the result returned by non-deferred, uncanceled commit attempts.
    /// Defaults to success. Changing this property affects subsequent attempts.
    /// </summary>
    public Result<Unit> CommitResult { get; set; } = Result.Ok();

    /// <summary>
    /// Gets the total number of calls to <see cref="CommitAsync"/>,
    /// including deferred nested calls and canceled attempts.
    /// </summary>
    public int CommitCallCount { get; private set; }

    /// <summary>
    /// Gets the number of non-deferred commit attempts, including failed and canceled
    /// attempts. This count does not represent successful writes.
    /// </summary>
    public int CommitAttemptCount { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// At scope depth zero or one, counts an attempt and returns <see cref="CommitResult"/>,
    /// or a canceled task when the token is already canceled. At greater depth, returns
    /// success without attempting a commit, observing cancellation, or using the configured
    /// result. No data is written.
    /// </remarks>
    public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default)
    {
        CommitCallCount++;
        if (_scopeDepth > 1)
            return Result.Ok().AsTask();

        CommitAttemptCount++;
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<Result<Unit>>(cancellationToken)
            : CommitResult.AsTask();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ownership is fixed when the scope opens. Dispose scopes in reverse opening order.
    /// Disposing a scope more than once is harmless and never commits.
    /// </remarks>
    public IUnitOfWorkScope BeginScope()
    {
        _scopeDepth++;
        return new Scope(this, _scopeDepth == 1);
    }

    private sealed class Scope(FakeUnitOfWork owner, bool isOwner) : IUnitOfWorkScope
    {
        private bool _disposed;

        public bool IsOwner { get; } = isOwner;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner._scopeDepth--;
        }
    }
}