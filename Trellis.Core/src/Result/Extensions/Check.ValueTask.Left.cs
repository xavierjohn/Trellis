namespace Trellis;

/// <summary>
/// Async Check extensions where only the LEFT (input) is async (ValueTask), check function is sync.
/// </summary>
public static partial class CheckExtensionsAsync
{
    /// <summary>
    /// Asynchronously runs a sync validation function on the success value, discarding the check result's value
    /// on success and preserving the original value. If the check fails, its failure is returned.
    /// Only the input is async (ValueTask); the check function is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <typeparam name="TK">Type of the check function's result value (discarded on success).</typeparam>
    /// <param name="resultTask">The async result to check.</param>
    /// <param name="func">The sync validation function that returns a Result.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckAsync<T, TK>(this ValueTask<Result<T>> resultTask, Func<T, Result<TK>> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.Check(func);
    }

    /// <summary>
    /// Asynchronously runs a sync validation function that returns <see cref="Result{Unit}"/> on the success value,
    /// preserving the original value on success. Only the input is async (ValueTask); the check function is sync.
    /// </summary>
    /// <typeparam name="T">Type of the original result value.</typeparam>
    /// <param name="resultTask">The async result to check.</param>
    /// <param name="func">The sync validation function that returns a Result of Unit.</param>
    /// <returns>The original result if the check passes; otherwise the check's failure.</returns>
    public static async ValueTask<Result<T>> CheckAsync<T>(this ValueTask<Result<T>> resultTask, Func<T, Result> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        Result<T> result = await resultTask.ConfigureAwait(false);
        return result.Check(func);
    }
}