namespace Trellis.Testing;

using FluentAssertions;

/// <summary>
/// Extension methods to enable FluentAssertions on async Result types (Task and ValueTask).
/// </summary>
public static class ResultAssertionsAsyncExtensions
{
    /// <summary>Asserts success within a bounded wait, without canceling the source operation.</summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <param name="resultTask">The source task.</param>
    /// <param name="timeout">The maximum wait; TimeoutException propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original success value constraint.</returns>
    public static async Task<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this Task<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
        string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return await resultTask.WaitAsync(timeout, cancellationToken).BeSuccessAsync(because, becauseArgs).ConfigureAwait(false);
    }

    /// <summary>Asserts failure within a bounded wait, without canceling the source operation.</summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <param name="resultTask">The source task.</param>
    /// <param name="timeout">The maximum wait; TimeoutException propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original error constraint.</returns>
    public static async Task<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this Task<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
        string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return await resultTask.WaitAsync(timeout, cancellationToken).BeFailureAsync(because, becauseArgs).ConfigureAwait(false);
    }

    /// <summary>Asserts the failure type within a bounded wait, without canceling the source operation.</summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <typeparam name="TError">The expected error type.</typeparam>
    /// <param name="resultTask">The source task.</param>
    /// <param name="timeout">The maximum wait; TimeoutException propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original typed error constraint.</returns>
    public static async Task<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this Task<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
        string because = "", params object[] becauseArgs) where TError : Error
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return await resultTask.WaitAsync(timeout, cancellationToken).BeFailureOfTypeAsync<TValue, TError>(because, becauseArgs).ConfigureAwait(false);
    }

    /// <summary>Asserts success within a bounded wait, consuming the ValueTask once.</summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <param name="resultTask">The source ValueTask.</param>
    /// <param name="timeout">The maximum wait; TimeoutException propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait, not the source operation.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original success value constraint.</returns>
    public static async ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
        string because = "", params object[] becauseArgs) =>
        await resultTask.AsTask().BeSuccessAsync(timeout, cancellationToken, because, becauseArgs).ConfigureAwait(false);

    /// <summary>Asserts failure within a bounded wait, consuming the ValueTask once.</summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <param name="resultTask">The source ValueTask.</param>
    /// <param name="timeout">The maximum wait; TimeoutException propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait, not the source operation.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original error constraint.</returns>
    public static async ValueTask<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
        string because = "", params object[] becauseArgs) =>
        await resultTask.AsTask().BeFailureAsync(timeout, cancellationToken, because, becauseArgs).ConfigureAwait(false);

    /// <summary>Asserts the failure type within a bounded wait, consuming the ValueTask once.</summary>
    /// <typeparam name="TValue">The result value type.</typeparam>
    /// <typeparam name="TError">The expected error type.</typeparam>
    /// <param name="resultTask">The source ValueTask.</param>
    /// <param name="timeout">The maximum wait; TimeoutException propagates on expiry.</param>
    /// <param name="cancellationToken">Cancels only this assertion's wait, not the source operation.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>The original typed error constraint.</returns>
    public static async ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this ValueTask<Result<TValue>> resultTask, TimeSpan timeout, CancellationToken cancellationToken,
        string because = "", params object[] becauseArgs) where TError : Error =>
        await resultTask.AsTask().BeFailureOfTypeAsync<TValue, TError>(timeout, cancellationToken, because, becauseArgs).ConfigureAwait(false);

    /// <summary>
    /// Asserts that the async result is a success.
    /// </summary>
    /// <typeparam name="TValue">The type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders.</param>
    /// <returns>An AndWhich constraint for method chaining with the success value.</returns>
    public static async Task<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return new ResultAssertions<TValue>(result).BeSuccess(because, becauseArgs);
    }

    /// <summary>
    /// Asserts that the async result is a failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders.</param>
    /// <returns>An AndWhich constraint for method chaining with the error.</returns>
    public static async Task<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return new ResultAssertions<TValue>(result).BeFailure(because, becauseArgs);
    }

    /// <summary>
    /// Asserts that the async result is a failure of a specific error type.
    /// </summary>
    /// <typeparam name="TValue">The type of the result value.</typeparam>
    /// <typeparam name="TError">The expected error type.</typeparam>
    /// <param name="resultTask">The task containing the result.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders.</param>
    /// <returns>An AndWhich constraint for method chaining with the typed error.</returns>
    public static async Task<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
        where TError : Error
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return new ResultAssertions<TValue>(result).BeFailureOfType<TError>(because, becauseArgs);
    }

    /// <summary>
    /// Asserts that the async ValueTask result is a success.
    /// </summary>
    /// <typeparam name="TValue">The type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders.</param>
    /// <returns>An AndWhich constraint for method chaining with the success value.</returns>
    public static async ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
    {
        var result = await resultTask.ConfigureAwait(false);
        return new ResultAssertions<TValue>(result).BeSuccess(because, becauseArgs);
    }

    /// <summary>
    /// Asserts that the async ValueTask result is a failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the result value.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders.</param>
    /// <returns>An AndWhich constraint for method chaining with the error.</returns>
    public static async ValueTask<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
    {
        var result = await resultTask.ConfigureAwait(false);
        return new ResultAssertions<TValue>(result).BeFailure(because, becauseArgs);
    }

    /// <summary>
    /// Asserts that the async ValueTask result is a failure of a specific error type.
    /// </summary>
    /// <typeparam name="TValue">The type of the result value.</typeparam>
    /// <typeparam name="TError">The expected error type.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result.</param>
    /// <param name="because">A formatted phrase explaining why the assertion is needed.</param>
    /// <param name="becauseArgs">Zero or more objects to format using the placeholders.</param>
    /// <returns>An AndWhich constraint for method chaining with the typed error.</returns>
    public static async ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
        where TError : Error
    {
        var result = await resultTask.ConfigureAwait(false);
        return new ResultAssertions<TValue>(result).BeFailureOfType<TError>(because, becauseArgs);
    }
}