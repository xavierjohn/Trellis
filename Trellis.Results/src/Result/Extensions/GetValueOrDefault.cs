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
    public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, TValue defaultValue) =>
        result.IsSuccess ? result.Value : defaultValue;

    /// <summary>
    /// Returns the success value, or evaluates the factory to produce a default if the result is a failure.
    /// The factory is only invoked on the failure track.
    /// This is a terminal operator that exits the Result railway.
    /// </summary>
    public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, Func<TValue> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return result.IsSuccess ? result.Value : defaultFactory();
    }

    /// <summary>
    /// Returns the success value, or evaluates the factory (which receives the error) to produce a default.
    /// The factory is only invoked on the failure track.
    /// This is a terminal operator that exits the Result railway.
    /// </summary>
    public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, Func<Error, TValue> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return result.IsSuccess ? result.Value : defaultFactory(result.Error);
    }
}