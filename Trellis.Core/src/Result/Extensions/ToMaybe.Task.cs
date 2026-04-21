namespace Trellis;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Provides asynchronous extension methods for converting Result{T} to Maybe{T}.
/// </summary>
[DebuggerStepThrough]
public static class ToMaybeExtensionsAsync
{
    /// <summary>
    /// Asynchronously converts a <see cref="Result{TValue}"/> wrapped in a Task to a <see cref="Maybe{TValue}"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The task containing the result to convert.</param>
    /// <returns>A Maybe containing the value if success; otherwise None.</returns>
    public static async Task<Maybe<TValue>> ToMaybeAsync<TValue>(this Task<Result<TValue>> resultTask) where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.ToMaybe();
    }

    /// <summary>
    /// Asynchronously converts a <see cref="Result{TValue}"/> wrapped in a ValueTask to a <see cref="Maybe{TValue}"/>.
    /// </summary>
    /// <typeparam name="TValue">Type of the result value.</typeparam>
    /// <param name="resultTask">The value task containing the result to convert.</param>
    /// <returns>A Maybe containing the value if success; otherwise None.</returns>
    public static async ValueTask<Maybe<TValue>> ToMaybeAsync<TValue>(this ValueTask<Result<TValue>> resultTask) where TValue : notnull
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToMaybe();
    }
}