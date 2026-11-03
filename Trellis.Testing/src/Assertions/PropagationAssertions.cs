namespace Trellis.Testing;

using FluentAssertions.Execution;

/// <summary>
/// Assertions that an async operation surfaces a source's original exception or cancellation token unchanged.
/// </summary>
public static class PropagationAssertionsExtensions
{
    /// <summary>Asserts the operation faults with exactly the <paramref name="expected"/> instance (no wrapping).</summary>
    /// <param name="operation">The operation under test.</param>
    /// <param name="expected">The original exception that must surface by reference.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    public static async Task ShouldPropagateExceptionAsync(this Task operation, Exception expected)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expected);
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

    /// <inheritdoc cref="ShouldPropagateExceptionAsync(Task, Exception)"/>
    public static Task ShouldPropagateExceptionAsync(this ValueTask operation, Exception expected) =>
        operation.AsTask().ShouldPropagateExceptionAsync(expected);

    /// <inheritdoc cref="ShouldPropagateExceptionAsync(Task, Exception)"/>
    public static Task ShouldPropagateExceptionAsync<T>(this ValueTask<T> operation, Exception expected) =>
        operation.AsTask().ShouldPropagateExceptionAsync(expected);

    /// <summary>Asserts the operation is canceled carrying exactly the <paramref name="expected"/> token.</summary>
    /// <param name="operation">The operation under test.</param>
    /// <param name="expected">The token whose identity must be preserved.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    public static async Task ShouldPropagateCancellationAsync(this Task operation, CancellationToken expected)
    {
        ArgumentNullException.ThrowIfNull(operation);
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

    /// <inheritdoc cref="ShouldPropagateCancellationAsync(Task, CancellationToken)"/>
    public static Task ShouldPropagateCancellationAsync(this ValueTask operation, CancellationToken expected) =>
        operation.AsTask().ShouldPropagateCancellationAsync(expected);

    /// <inheritdoc cref="ShouldPropagateCancellationAsync(Task, CancellationToken)"/>
    public static Task ShouldPropagateCancellationAsync<T>(this ValueTask<T> operation, CancellationToken expected) =>
        operation.AsTask().ShouldPropagateCancellationAsync(expected);
}