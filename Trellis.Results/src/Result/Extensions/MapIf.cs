namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Conditionally transforms the value inside a Result when a condition or predicate is met.
/// </summary>
[DebuggerStepThrough]
public static class MapIfExtensions
{
    /// <summary>
    /// Transforms the value if the result is successful and the condition is true.
    /// Returns the original result unchanged if the condition is false or the result is a failure.
    /// </summary>
    public static Result<T> MapIf<T>(this Result<T> result, bool condition, Func<T, T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsFailure || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        var mapped = Result.Success(func(result.Value));
        mapped.LogActivityStatus();
        return mapped;
    }

    /// <summary>
    /// Transforms the value if the result is successful and the predicate returns true for the value.
    /// Returns the original result unchanged if the predicate is false or the result is a failure.
    /// </summary>
    public static Result<T> MapIf<T>(this Result<T> result, Func<T, bool> predicate, Func<T, T> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsFailure || !predicate(result.Value))
        {
            result.LogActivityStatus();
            return result;
        }

        var mapped = Result.Success(func(result.Value));
        mapped.LogActivityStatus();
        return mapped;
    }
}