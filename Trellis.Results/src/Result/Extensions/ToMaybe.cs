namespace Trellis;

using System;
using System.Diagnostics;

/// <summary>
/// Provides extension methods for converting Result{T} to Maybe{T}.
/// Success results become Some(value), failure results become None.
/// </summary>
[DebuggerStepThrough]
public static class ToMaybeExtensions
{
    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to a <see cref="Maybe{TValue}"/>.
    /// If the result is a success, returns Some(value). If the result is a failure, returns None.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <returns>A Maybe containing the value if success; otherwise None.</returns>
    public static Maybe<TValue> ToMaybe<TValue>(this Result<TValue> result) where TValue : notnull
    {
        using var activity = RopTrace.ActivitySource.StartActivity(nameof(ToMaybe));

        if (result.IsSuccess)
            return Maybe.From(result.Value);

        return Maybe.None<TValue>();
    }
}