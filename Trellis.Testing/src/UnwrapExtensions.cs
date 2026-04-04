#pragma warning disable TRLS003 // Unsafe access to Result.Value — Unwrap performs its own guard check

namespace Trellis.Testing;

/// <summary>
/// Provides <c>Unwrap()</c> extension methods for extracting values from <see cref="Result{T}"/>
/// and <see cref="Maybe{T}"/> in test code. These methods throw a descriptive exception on failure/none
/// rather than returning the raw <c>.Value</c>, which avoids TRLS003 warnings in test projects.
/// </summary>
/// <remarks>
/// <para>
/// Intended for test code only. Do not use in production code — use pattern matching,
/// <c>Match</c>, <c>GetValueOrDefault</c>, or other ROP operations instead.
/// </para>
/// <para>
/// Typical usage after a FluentAssertions guard:
/// <code>
/// result.Should().BeSuccess();
/// var value = result.Unwrap(); // Safe — we know it's a success
/// </code>
/// </para>
/// </remarks>
public static class UnwrapExtensions
{
    /// <summary>
    /// Extracts the value from a successful result, or throws <see cref="UnwrapFailedException"/>
    /// with the error details if the result is a failure.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to unwrap.</param>
    /// <returns>The success value.</returns>
    /// <exception cref="UnwrapFailedException">Thrown when the result is a failure.</exception>
    public static T Unwrap<T>(this Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new UnwrapFailedException(
                $"Called Unwrap() on a failed Result<{typeof(T).Name}>. " +
                $"Error: [{result.Error.Code}] {result.Error.Detail}");

    /// <summary>
    /// Extracts the value from a Maybe that has a value, or throws <see cref="UnwrapFailedException"/>
    /// if the Maybe is None.
    /// </summary>
    /// <typeparam name="T">Type of the Maybe value.</typeparam>
    /// <param name="maybe">The Maybe to unwrap.</param>
    /// <returns>The contained value.</returns>
    /// <exception cref="UnwrapFailedException">Thrown when the Maybe is None.</exception>
    public static T Unwrap<T>(this Maybe<T> maybe) where T : notnull =>
        maybe.HasValue
            ? maybe.Value
            : throw new UnwrapFailedException(
                $"Called Unwrap() on a None Maybe<{typeof(T).Name}>.");

    /// <summary>
    /// Awaits the task and extracts the value from a successful result, or throws
    /// <see cref="UnwrapFailedException"/> with the error details if the result is a failure.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">The task producing the result to unwrap.</param>
    /// <returns>The success value.</returns>
    /// <exception cref="UnwrapFailedException">Thrown when the result is a failure.</exception>
    public static async Task<T> UnwrapAsync<T>(this Task<Result<T>> resultTask)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.Unwrap();
    }

    /// <summary>
    /// Awaits the value task and extracts the value from a successful result, or throws
    /// <see cref="UnwrapFailedException"/> with the error details if the result is a failure.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="resultTask">The value task producing the result to unwrap.</param>
    /// <returns>The success value.</returns>
    /// <exception cref="UnwrapFailedException">Thrown when the result is a failure.</exception>
    public static async ValueTask<T> UnwrapAsync<T>(this ValueTask<Result<T>> resultTask)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.Unwrap();
    }
}
