namespace Trellis.Testing;

using FluentAssertions;

/// <summary>
/// Extension methods to enable FluentAssertions on async Result types (Task and ValueTask).
/// </summary>
public static class ResultAssertionsAsyncExtensions
{
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