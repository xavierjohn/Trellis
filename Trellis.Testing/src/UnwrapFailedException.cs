namespace Trellis.Testing;

/// <summary>
/// Exception thrown when <see cref="UnwrapExtensions.Unwrap{T}(Result{T})"/> or
/// <see cref="UnwrapExtensions.Unwrap{T}(Maybe{T})"/> is called on a failure/none value.
/// Provides a descriptive message including the error details for test diagnostics.
/// </summary>
public sealed class UnwrapFailedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnwrapFailedException"/> class.
    /// </summary>
    public UnwrapFailedException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnwrapFailedException"/> class.
    /// </summary>
    /// <param name="message">The descriptive error message.</param>
    public UnwrapFailedException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnwrapFailedException"/> class.
    /// </summary>
    /// <param name="message">The descriptive error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public UnwrapFailedException(string message, Exception innerException) : base(message, innerException) { }
}