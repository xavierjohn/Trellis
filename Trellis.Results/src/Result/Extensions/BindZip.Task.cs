namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Async BindZip extensions where both input and function are async (Task).
/// </summary>
public static partial class BindZipExtensionsAsync
{
    /// <summary>
    /// Asynchronously binds an async function to the awaited Result value and zips both values into a tuple.
    /// </summary>
    /// <typeparam name="T1">Type of the input result value.</typeparam>
    /// <typeparam name="T2">Type of the new result value.</typeparam>
    /// <param name="resultTask">The task containing the result to bind and zip.</param>
    /// <param name="func">The async function to call if the result is successful.</param>
    /// <returns>A tuple result combining both values on success; otherwise the failure.</returns>
    public static async Task<Result<(T1, T2)>> BindZipAsync<T1, T2>(
        this Task<Result<T1>> resultTask,
        Func<T1, Task<Result<T2>>> func)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(BindZipExtensions.BindZip));

        var result = await resultTask.ConfigureAwait(false);
        if (result.IsFailure)
        {
            var failure = Result.Failure<(T1, T2)>(result.Error);
            failure.LogActivityStatus();
            return failure;
        }

        var nextResult = await func(result.Value).ConfigureAwait(false);
        if (nextResult.IsFailure)
        {
            var failure = Result.Failure<(T1, T2)>(nextResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        var success = Result.Success((result.Value, nextResult.Value));
        success.LogActivityStatus();
        return success;
    }
}