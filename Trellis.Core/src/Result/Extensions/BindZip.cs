namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Extension methods that bind a function to a Result and zip the original value with the new value into a tuple.
/// Enables sequential accumulation of values through a pipeline.
/// </summary>
[DebuggerStepThrough]
public static partial class BindZipExtensions
{
    /// <summary>
    /// Binds a function to the Result value and zips both values into a tuple on success.
    /// If the source or the function result is a failure, returns that failure.
    /// </summary>
    /// <typeparam name="T1">Type of the input result value.</typeparam>
    /// <typeparam name="T2">Type of the new result value.</typeparam>
    /// <param name="result">The result to bind and zip.</param>
    /// <param name="func">The function to call if the result is successful.</param>
    /// <returns>A tuple result combining both values on success; otherwise the failure.</returns>
    public static Result<(T1, T2)> BindZip<T1, T2>(
        this Result<T1> result,
        Func<T1, Result<T2>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity();

        if (!result.TryGetValue(out var value))
        {
            var failure = Result.Fail<(T1, T2)>(result.Error);
            failure.LogActivityStatus();
            return failure;
        }

        var nextResult = func(value);
        if (!nextResult.TryGetValue(out var inner))
        {
            var failure = Result.Fail<(T1, T2)>(nextResult.Error);
            failure.LogActivityStatus();
            return failure;
        }

        var success = Result.Ok((value, inner));
        success.LogActivityStatus();
        return success;
    }

}