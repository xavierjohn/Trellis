namespace Trellis;

using System.Diagnostics;

/// <summary>
/// Terminal extraction methods for Result values.
/// </summary>
[DebuggerStepThrough]
public static class GetValueOrDefaultExtensions
{
    /// <summary>
    /// Returns the success value, or the specified default if the result is a failure.
    /// This is a terminal operator that exits the Result railway.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The result to extract a value from.</param>
    /// <param name="defaultValue">The value to return if the result is a failure.</param>
    /// <returns>The success value, or <paramref name="defaultValue"/> on failure.</returns>
    public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, TValue defaultValue) =>
        result.TryGetValue(out var value) ? value : defaultValue;

    /// <summary>
    /// Returns the success value, or evaluates the factory to produce a default if the result is a failure.
    /// The factory is only invoked on the failure track.
    /// This is a terminal operator that exits the Result railway.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The result to extract a value from.</param>
    /// <param name="defaultFactory">A factory function invoked only when the result is a failure.</param>
    /// <returns>The success value, or the factory result on failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="defaultFactory"/> is null.</exception>
    public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, Func<TValue> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return result.TryGetValue(out var value) ? value : defaultFactory();
    }

    /// <summary>
    /// Returns the success value, or evaluates the factory (which receives the error) to produce a default.
    /// The factory is only invoked on the failure track.
    /// This is a terminal operator that exits the Result railway.
    /// </summary>
    /// <typeparam name="TValue">The type of the value in the Result.</typeparam>
    /// <param name="result">The result to extract a value from.</param>
    /// <param name="defaultFactory">A factory function that receives the error and produces a default value.</param>
    /// <returns>The success value, or the factory result on failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="defaultFactory"/> is null.</exception>
    public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, Func<Error, TValue> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return result.TryGetValue(out var value) ? value : defaultFactory(result.Error);
    }
}