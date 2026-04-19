namespace Trellis;

/// <summary>
/// Async Ensure extensions where only the LEFT (input) is async (Task), predicates are sync.
/// </summary>
public static partial class EnsureExtensionsAsync
{
    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to validate.</param>
    /// <param name="predicate">The predicate to test.</param>
    /// <param name="error">The error to return if the predicate is false.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to validate.</param>
    /// <param name="predicate">The predicate function to test the value.</param>
    /// <param name="error">The error to return if the predicate is false.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to validate.</param>
    /// <param name="predicate">The predicate function to test the value.</param>
    /// <param name="errorPredicate">The function that generates an error from the value.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, bool> predicate, Func<TValue, Error> errorPredicate)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorPredicate);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate, errorPredicate);
    }

    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// The error is generated asynchronously.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to validate.</param>
    /// <param name="predicate">The predicate function to test the value.</param>
    /// <param name="errorPredicate">The async function that generates an error from the value.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, bool> predicate, Func<TValue, Task<Error>> errorPredicate)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorPredicate);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureExtensions.Ensure));
        Result<TValue> result = await resultTask.ConfigureAwait(false);

        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        if (predicate(result.Value))
        {
            result.LogActivityStatus();
            return result;
        }

        return Result.Fail<TValue>(await errorPredicate(result.Value).ConfigureAwait(false));
    }

    /// <summary>
    /// Returns a new failure result if the predicate result is a failure. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to validate.</param>
    /// <param name="predicate">The predicate function that returns a Result.</param>
    /// <returns>The original result if both succeed; otherwise a failure.</returns>
    public static async Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<Result<TValue>> predicate)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate);
    }

    /// <summary>
    /// Returns a new failure result if the predicate result is a failure. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to validate.</param>
    /// <param name="predicate">The predicate function that receives the value and returns a Result.</param>
    /// <returns>The original result if both succeed; otherwise a failure.</returns>
    public static async Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Result<TValue>> predicate)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);

        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.Ensure(predicate);
    }
}