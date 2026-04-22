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
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result whose value may be transformed.</param>
    /// <param name="condition">The boolean condition that must be true for the transformation to apply.</param>
    /// <param name="func">The transformation function to apply to the value.</param>
    /// <returns>A result with the transformed value if successful and condition is true; otherwise the original result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="func"/> is null.</exception>
    public static Result<T> MapIf<T>(this Result<T> result, bool condition, Func<T, T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsFailure || !condition)
        {
            result.LogActivityStatus();
            return result;
        }

        result.TryGetValue(out var value);
        var mapped = Result.Ok(func(value!));
        mapped.LogActivityStatus();
        return mapped;
    }

    /// <summary>
    /// Transforms the value if the result is successful and the predicate returns true for the value.
    /// Returns the original result unchanged if the predicate is false or the result is a failure.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result whose value may be transformed.</param>
    /// <param name="predicate">The predicate function to test the value against.</param>
    /// <param name="func">The transformation function to apply to the value.</param>
    /// <returns>A result with the transformed value if successful and predicate is true; otherwise the original result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> or <paramref name="func"/> is null.</exception>
    public static Result<T> MapIf<T>(this Result<T> result, Func<T, bool> predicate, Func<T, T> func)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(func);
        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsFailure)
        {
            result.LogActivityStatus();
            return result;
        }

        result.TryGetValue(out var value);
        if (!predicate(value!))
        {
            result.LogActivityStatus();
            return result;
        }

        var mapped = Result.Ok(func(value!));
        mapped.LogActivityStatus();
        return mapped;
    }
}