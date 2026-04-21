namespace Trellis;

/// <summary>
/// Enables construction of a failure result of the implementing type from an <see cref="Error"/>.
/// Implemented by <see cref="Result{TValue}"/> to support generic pipeline behaviors
/// that need to construct failure results without knowing the inner type parameter.
/// </summary>
/// <typeparam name="TSelf">The concrete result type (self-type pattern).</typeparam>
public interface IFailureFactory<TSelf> where TSelf : IFailureFactory<TSelf>
{
    /// <summary>
    /// Creates a failure result wrapping the given error.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failure result of type <typeparamref name="TSelf"/>.</returns>
#pragma warning disable CA1716 // Identifiers should not match keywords — Error is the domain type name
    static abstract TSelf CreateFailure(Error error);
#pragma warning restore CA1716
}