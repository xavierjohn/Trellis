namespace Trellis;

/// <summary>
/// Generic interface for result types, providing typed access to the success value.
/// </summary>
/// <typeparam name="TValue">The type of the success value.</typeparam>
/// <remarks>
/// This interface extends <see cref="IResult"/> to add strongly-typed access to the success value.
/// </remarks>
public interface IResult<TValue> : IResult
{
    /// <summary>
    /// Attempts to get the success value without throwing.
    /// </summary>
    /// <param name="value">When this method returns true, contains the success value; otherwise, the default value.</param>
    /// <returns>True if the result is successful; otherwise false.</returns>
    bool TryGetValue(out TValue value);
}