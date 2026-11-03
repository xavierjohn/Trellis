namespace Trellis.Testing;

using FluentAssertions.Execution;

/// <summary>
/// Assertions that an async operation surfaces a source's original exception or cancellation token unchanged.
/// </summary>
/// <remarks>
/// Every method takes an optional <c>timeout</c> and <c>cancellationToken</c> that bound only the assertion's wait.
/// With neither supplied the operation is awaited without a bound, so pass them whenever the operation could be
/// stranded on a pending source. An expired bound fails the assertion; a canceled token throws
/// <see cref="OperationCanceledException"/>. Neither completes or cancels the operation.
/// </remarks>
public static class PropagationAssertionsExtensions
{
    /// <summary>Asserts the operation faults with exactly the <paramref name="expected"/> instance (no wrapping).</summary>
    /// <param name="operation">The operation under test.</param>
    /// <param name="expected">The original exception that must surface by reference.</param>
    /// <param name="timeout">Optional maximum wait; expiry fails the assertion.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait, not the operation.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    public static async Task ShouldPropagateExceptionAsync(
        this Task operation, Exception expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expected);
        if (!await CompletedWithinBoundAsync(operation, timeout, cancellationToken).ConfigureAwait(false))
        {
            Execute.Assertion.FailWith(
                "Expected the operation to fault with the original {0}, but it did not complete within {1}.",
                expected.GetType().FullName, timeout);
            return;
        }

        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception actual)
        {
            Execute.Assertion
                .ForCondition(ReferenceEquals(actual, expected))
                .FailWith(
                    "Expected the original {0} instance to propagate, but the operation faulted with {1}: {2}",
                    expected.GetType().FullName,
                    actual.GetType().FullName,
                    actual.Message);
            return;
        }

        Execute.Assertion.FailWith(
            "Expected the operation to fault with the original {0}, but it completed successfully.",
            expected.GetType().FullName);
    }

    /// <inheritdoc cref="ShouldPropagateExceptionAsync(Task, Exception, TimeSpan?, CancellationToken)"/>
    public static Task ShouldPropagateExceptionAsync(
        this ValueTask operation, Exception expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        operation.AsTask().ShouldPropagateExceptionAsync(expected, timeout, cancellationToken);

    /// <inheritdoc cref="ShouldPropagateExceptionAsync(Task, Exception, TimeSpan?, CancellationToken)"/>
    public static Task ShouldPropagateExceptionAsync<T>(
        this ValueTask<T> operation, Exception expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        operation.AsTask().ShouldPropagateExceptionAsync(expected, timeout, cancellationToken);

    /// <summary>Asserts the operation is canceled carrying exactly the <paramref name="expected"/> token.</summary>
    /// <param name="operation">The operation under test.</param>
    /// <param name="expected">The token whose identity must be preserved.</param>
    /// <param name="timeout">Optional maximum wait; expiry fails the assertion.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait, not the operation.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The token under test is the subject of the assertion and precedes the optional timeout and the token that bounds only the wait.")]
    public static async Task ShouldPropagateCancellationAsync(
        this Task operation, CancellationToken expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!await CompletedWithinBoundAsync(operation, timeout, cancellationToken).ConfigureAwait(false))
        {
            Execute.Assertion.FailWith(
                "Expected the operation to be canceled, but it did not complete within {0}.", timeout);
            return;
        }

        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (OperationCanceledException actual)
        {
            Execute.Assertion
                .ForCondition(actual.CancellationToken == expected)
                .FailWith("Expected the operation to preserve the original cancellation token, but it carried a different one.");
            return;
        }
        catch (Exception actual)
        {
            Execute.Assertion.FailWith(
                "Expected the operation to be canceled, but it faulted with {0}: {1}",
                actual.GetType().FullName,
                actual.Message);
            return;
        }

        Execute.Assertion.FailWith("Expected the operation to be canceled, but it completed successfully.");
    }

    /// <inheritdoc cref="ShouldPropagateCancellationAsync(Task, CancellationToken, TimeSpan?, CancellationToken)"/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The token under test is the subject of the assertion and precedes the optional timeout and the token that bounds only the wait.")]
    public static Task ShouldPropagateCancellationAsync(
        this ValueTask operation, CancellationToken expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        operation.AsTask().ShouldPropagateCancellationAsync(expected, timeout, cancellationToken);

    /// <inheritdoc cref="ShouldPropagateCancellationAsync(Task, CancellationToken, TimeSpan?, CancellationToken)"/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "The token under test is the subject of the assertion and precedes the optional timeout and the token that bounds only the wait.")]
    public static Task ShouldPropagateCancellationAsync<T>(
        this ValueTask<T> operation, CancellationToken expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        operation.AsTask().ShouldPropagateCancellationAsync(expected, timeout, cancellationToken);

    private static async Task<bool> CompletedWithinBoundAsync(Task operation, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (timeout is null && !cancellationToken.CanBeCanceled)
            return true;

        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var expiry = Task.Delay(timeout ?? Timeout.InfiniteTimeSpan, stopWaiting.Token);
        if (await Task.WhenAny(operation, expiry).ConfigureAwait(false) == operation)
        {
            await stopWaiting.CancelAsync().ConfigureAwait(false);
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }
}