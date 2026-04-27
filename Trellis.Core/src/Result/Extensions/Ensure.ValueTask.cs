namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async Ensure extensions where BOTH input and predicates are async (ValueTask).
/// </summary>
/// <remarks>
/// Users should capture CancellationToken in their lambda closures when cancellation support is needed.
/// </remarks>
/// <example>
/// <code>
/// var ct = cancellationToken;
/// await result.EnsureAsync(
///     value => ValidateAsync(value, ct),
///     Error.UnprocessableContent.ForRule("invalid", "Validation failed")
/// );
/// </code>
/// </example>
[DebuggerStepThrough]
public static partial class EnsureExtensionsAsync
{
    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The async result to validate.</param>
    /// <param name="predicate">The async predicate to test the value.</param>
    /// <param name="error">The error to return if the predicate is false.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async ValueTask<Result<TValue>> EnsureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<TValue, ValueTask<bool>> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureExtensions.Ensure));
        Result<TValue> result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return result;
        }

        if (!await predicate(value).ConfigureAwait(false))
            return Result.Fail<TValue>(error);

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The async result to validate.</param>
    /// <param name="predicate">The async predicate to test the value.</param>
    /// <param name="errorPredicate">The function that generates an error from the value.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async ValueTask<Result<TValue>> EnsureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<TValue, ValueTask<bool>> predicate, Func<TValue, Error> errorPredicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorPredicate);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureExtensions.Ensure));
        Result<TValue> result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return result;
        }

        if (!await predicate(value).ConfigureAwait(false))
            return Result.Fail<TValue>(errorPredicate(value));

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a new failure result if the predicate is false. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The async result to validate.</param>
    /// <param name="predicate">The async predicate to test the value.</param>
    /// <param name="errorPredicate">The async function that generates an error from the value.</param>
    /// <returns>The original result if success and predicate is true; otherwise a failure.</returns>
    public static async ValueTask<Result<TValue>> EnsureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<TValue, ValueTask<bool>> predicate, Func<TValue, ValueTask<Error>> errorPredicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(errorPredicate);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureExtensions.Ensure));
        Result<TValue> result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return result;
        }

        if (!await predicate(value).ConfigureAwait(false))
            return Result.Fail<TValue>(await errorPredicate(value).ConfigureAwait(false));

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a new failure result if the predicate result is a failure. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The async result to validate.</param>
    /// <param name="predicate">The async predicate function that returns a Result.</param>
    /// <returns>The original result if both succeed; otherwise a failure.</returns>
    public static async ValueTask<Result<TValue>> EnsureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<ValueTask<Result<TValue>>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureExtensions.Ensure));
        Result<TValue> result = await resultTask.ConfigureAwait(false);

        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        var predicateResult = await predicate().ConfigureAwait(false);

        if (predicateResult.IsFailure)
            return Result.Fail<TValue>(predicateResult.Error);

        result.LogActivityStatus();
        return result;
    }

    /// <summary>
    /// Returns a new failure result if the predicate result is a failure. Otherwise returns the starting result.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The async result to validate.</param>
    /// <param name="predicate">The async predicate function that receives the value and returns a Result.</param>
    /// <returns>The original result if both succeed; otherwise a failure.</returns>
    public static async ValueTask<Result<TValue>> EnsureAsync<TValue>(this ValueTask<Result<TValue>> resultTask, Func<TValue, ValueTask<Result<TValue>>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var activity = RopTrace.ActivitySource.StartActivity(nameof(EnsureExtensions.Ensure));
        Result<TValue> result = await resultTask.ConfigureAwait(false);

        if (!result.TryGetValue(out var value))
        {
            result.LogActivityStatus();
            return result;
        }

        var predicateResult = await predicate(value).ConfigureAwait(false);

        if (predicateResult.IsFailure)
            return Result.Fail<TValue>(predicateResult.Error);

        result.LogActivityStatus();
        return result;
    }
}