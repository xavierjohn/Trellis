namespace Trellis;

/// <summary>
/// Async CheckIf extensions where only the LEFT (input) is async (Task), check function is sync.
/// </summary>
public static partial class CheckIfExtensionsAsync
{
    /// <summary>
    /// Conditionally runs a sync validation function when the boolean condition is true.
    /// Only the input is async; the check function is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The task containing the result to check.</param>
    /// <param name="condition">The condition that must be true for the check to run.</param>
    /// <param name="func">The sync validation function that returns a Result.</param>
    /// <returns>The original result if the condition is false or the check passes; otherwise the check's failure.</returns>
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, bool condition, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(condition, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Task{Result{T}}, bool, Func{T, Result{TK}})"/>
    public static async Task<Result<T>> CheckIfAsync<T>(this Task<Result<T>> resultTask, bool condition, Func<T, Result<Unit>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(condition, func);
    }

    /// <summary>
    /// Conditionally runs a sync validation function when the predicate returns true.
    /// Only the input is async; the check function is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The task containing the result to check.</param>
    /// <param name="predicate">The predicate to evaluate against the success value.</param>
    /// <param name="func">The sync validation function that returns a Result.</param>
    /// <returns>The original result if the predicate returns false or the check passes; otherwise the check's failure.</returns>
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(predicate, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Task{Result{T}}, Func{T, bool}, Func{T, Result{TK}})"/>
    public static async Task<Result<T>> CheckIfAsync<T>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<Unit>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(predicate, func);
    }
}