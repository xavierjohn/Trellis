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
    public static async ValueTask<Result<T>> CheckIfAsync<T, TK>(this ValueTask<Result<T>> resultTask, bool condition, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(condition, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(ValueTask{Result{T}}, bool, Func{T, Result{TK}})"/>
    public static ValueTask<Result<T>> CheckIfAsync<T>(this ValueTask<Result<T>> resultTask, bool condition, Func<T, Result<Unit>> func)
        => CheckIfAsync<T, Unit>(resultTask, condition, func);

    /// <summary>
    /// Conditionally runs a sync validation function when the predicate returns true.
    /// Only the input is async (ValueTask); the check function is sync.
    /// </summary>
    public static async ValueTask<Result<T>> CheckIfAsync<T, TK>(this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.CheckIf(predicate, func);
    }

    /// <inheritdoc cref="CheckIfAsync{T,TK}(ValueTask{Result{T}}, Func{T, bool}, Func{T, Result{TK}})"/>
    public static ValueTask<Result<T>> CheckIfAsync<T>(this ValueTask<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Result<Unit>> func)
        => CheckIfAsync<T, Unit>(resultTask, predicate, func);
}
