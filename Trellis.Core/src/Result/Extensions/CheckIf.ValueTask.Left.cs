namespace Trellis;

/// <summary>
/// Async CheckIf extensions where only the LEFT (input) is async (ValueTask), check function is sync.
/// </summary>
public static partial class CheckIfExtensionsAsync
{
    /// <summary>
    /// Conditionally runs a sync validation function when the boolean condition is true.
    /// Only the input is async (ValueTask); the check function is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The async result to check.</param>
    /// <param name="condition">The condition that must be true for the check to run.</param>
    /// <param name="func">The sync validation function that returns a Result.</param>
    /// <returns>The original result if the condition is false or the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckIfAsync<T, TK>(this ValueTask<Result<T>> resultTask, bool condition, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(condition, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(ValueTask{Result{T}}, bool, Func{T, Result{TK}})"/>
    public static async ValueTask<Result<T>> CheckIfAsync<T>(this ValueTask<Result<T>> resultTask, bool condition, Func<T, Result<Unit>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(condition, func);
    }

    /// <summary>
    /// Conditionally runs a sync validation function when the predicate returns true.
    /// Only the input is async (ValueTask); the check function is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The async result to check.</param>
    /// <param name="predicate">The predicate to evaluate against the success value.</param>
    /// <param name="func">The sync validation function that returns a Result.</param>
    /// <returns>The original result if the predicate returns false or the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckIfAsync<T, TK>(this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(predicate, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(ValueTask{Result{T}}, Func{T, bool}, Func{T, Result{TK}})"/>
    public static async ValueTask<Result<T>> CheckIfAsync<T>(this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<Unit>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(predicate, func);
    }
}