namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async BindZip extensions where the input is async (Task) and the function is synchronous.
/// </summary>
public static partial class BindZipExtensionsAsync
{
    /// <summary>
    /// Asynchronously binds a function to the awaited Result value and zips both values into a tuple.
    /// </summary>
    /// <typeparam name="T1">Type of the input result value.</typeparam>
    /// <typeparam name="T2">Type of the new result value.</typeparam>
    /// <param name="resultTask">The task containing the result to bind and zip.</param>
    /// <param name="func">The synchronous function to call if the result is successful.</param>
    /// <returns>A tuple result combining both values on success; otherwise the failure.</returns>
    public static async Task<Result<(T1, T2)>> BindZipAsync<T1, T2>(
        this Task<Result<T1>> resultTask,
        Func<T1, Result<T2>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        var result = await resultTask.ConfigureAwait(false);
        return result.BindZip(func);
    }
}