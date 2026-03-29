namespace Trellis;

/// <summary>
/// Async BindZip extension where the input is synchronous and the function returns a Task.
/// </summary>
public static partial class BindZipExtensionsAsync
{
    /// <summary>
    /// Binds an async function to the Result value and zips both values into a tuple.
    /// </summary>
    /// <typeparam name="T1">Type of the input result value.</typeparam>
    /// <typeparam name="T2">Type of the new result value.</typeparam>
    /// <param name="result">The result to bind and zip.</param>
    /// <param name="func">The async function to call if the result is successful.</param>
    /// <returns>A tuple result combining both values on success; otherwise the failure.</returns>
    public static async Task<Result<(T1, T2)>> BindZipAsync<T1, T2>(
        this Result<T1> result,
        Func<T1, Task<Result<T2>>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity();

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
