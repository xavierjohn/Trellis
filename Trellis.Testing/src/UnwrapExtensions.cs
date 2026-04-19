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
        result.TryGetValue(out var value)
            ? value
            : throw new UnwrapFailedException(
                BuildUnwrapErrorMessage<T>(result));

    /// <summary>
    /// Extracts the error from a failed result, or throws <see cref="UnwrapFailedException"/>
    /// if the result is a success.
    /// </summary>
    /// <typeparam name="T">Type of the result value.</typeparam>
    /// <param name="result">The result to unwrap the error from.</param>
    /// <returns>The error.</returns>
    /// <exception cref="UnwrapFailedException">Thrown when the result is a success.</exception>
    public static Error UnwrapError<T>(this Result<T> result) =>
        result.TryGetError(out var error)
            ? error
            : throw new UnwrapFailedException(
                $"Called UnwrapError() on a successful Result<{typeof(T).Name}>.");

    /// <summary>
    /// Extracts the error from a failed non-generic result, or throws if success.
    /// </summary>
    public static Error UnwrapError(this Result result) =>
        result.TryGetError(out var error)
            ? error
            : throw new UnwrapFailedException("Called UnwrapError() on a successful Result.");

    private static string BuildUnwrapErrorMessage<T>(Result<T> result)
    {
        // Caller (Unwrap) only invokes this when the result is known to be a failure.
        if (!result.TryGetError(out var error))
            return $"Called Unwrap() on a Result<{typeof(T).Name}> in an unexpected state.";
        return $"Called Unwrap() on a failed Result<{typeof(T).Name}>. " +
               $"Error: [{error.Code}] {error.Detail}";
    }

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
#pragma warning disable TRLS006 // Guarded by HasValue check above
            ? maybe.Value
#pragma warning restore TRLS006
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