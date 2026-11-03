namespace Trellis.Testing;

using FluentAssertions.Execution;

/// <summary>
/// Assertions for an operation that must be genuinely pending until a test releases its gate.
/// </summary>
public static class PendingOperationAssertionsExtensions
{
    /// <summary>
    /// Asserts the operation is pending, runs optional pending-state checks, always releases the gate,
    /// then waits for the operation with an explicit bound.
    /// </summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The operation under test.</param>
    /// <param name="release">Releases whatever the operation is waiting on; runs even when a check fails.</param>
    /// <param name="timeout">The maximum wait after release; <see cref="TimeoutException"/> propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this wait, not the operation.</param>
    /// <param name="whilePending">Optional checks that must hold while the operation is still pending.</param>
    /// <returns>The operation result.</returns>
    public static async Task<TResult> ShouldBePendingThenReleaseAsync<TResult>(
        this Task<TResult> operation, Action release, TimeSpan timeout, CancellationToken cancellationToken,
        Action? whilePending = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(release);
        CheckPendingThenRelease(operation.IsCompleted, release, whilePending);
        return await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ShouldBePendingThenReleaseAsync{TResult}(Task{TResult}, Action, TimeSpan, CancellationToken, Action?)"/>
    public static async Task ShouldBePendingThenReleaseAsync(
        this Task operation, Action release, TimeSpan timeout, CancellationToken cancellationToken,
        Action? whilePending = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(release);
        CheckPendingThenRelease(operation.IsCompleted, release, whilePending);
        await operation.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ShouldBePendingThenReleaseAsync{TResult}(Task{TResult}, Action, TimeSpan, CancellationToken, Action?)"/>
    /// <remarks>The ValueTask is consumed exactly once.</remarks>
    public static async ValueTask<TResult> ShouldBePendingThenReleaseAsync<TResult>(
        this ValueTask<TResult> operation, Action release, TimeSpan timeout, CancellationToken cancellationToken,
        Action? whilePending = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        CheckPendingThenRelease(operation.IsCompleted, release, whilePending);
        return await operation.AsTask().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private static void CheckPendingThenRelease(bool isCompleted, Action release, Action? whilePending)
    {
        try
        {
            Execute.Assertion
                .ForCondition(!isCompleted)
                .FailWith("Expected the operation to be pending before release, but it had already completed.");
            whilePending?.Invoke();
        }
        finally
        {
            release();
        }
    }
}