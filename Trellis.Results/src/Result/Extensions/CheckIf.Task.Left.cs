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
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, bool condition, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(condition, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Task{Result{T}}, bool, Func{T, Result{TK}})"/>
    public static Task<Result<T>> CheckIfAsync<T>(this Task<Result<T>> resultTask, bool condition, Func<T, Result<Unit>> func)
        => CheckIfAsync<T, Unit>(resultTask, condition, func);

    /// <summary>
    /// Conditionally runs a sync validation function when the predicate returns true.
    /// Only the input is async; the check function is sync.
    /// </summary>
    public static async Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(predicate, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(Task{Result{T}}, Func{T, bool}, Func{T, Result{TK}})"/>
    public static Task<Result<T>> CheckIfAsync<T>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<Unit>> func)
        => CheckIfAsync<T, Unit>(resultTask, predicate, func);
}
