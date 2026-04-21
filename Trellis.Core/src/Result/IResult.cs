namespace Trellis;

/// <summary>
/// Non-generic base interface for result types, exposing success/failure state and error information.
/// </summary>
/// <remarks>
/// This interface allows polymorphic handling of results without knowing the success value type.
/// Use <see cref="IResult{TValue}"/> for typed access to the success value.
/// </remarks>
public interface IResult
{
    /// <summary>
    /// Gets a value indicating whether this result represents a successful outcome.
    /// </summary>
    /// <value><c>true</c> if the result is successful; otherwise, <c>false</c>.</value>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Error))]
    bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether this result represents a failure.
    /// </summary>
    /// <value><c>true</c> if the result is a failure; otherwise, <c>false</c>.</value>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Error))]
    bool IsFailure { get; }

    /// <summary>
    /// Gets the error when this result is a failure, or <see langword="null"/> when it is a success.
    /// </summary>
    /// <remarks>
    /// Reading this property never throws. Pattern-match on the value to handle individual error cases.
    /// Use <see cref="TryGetError"/> for bool-gated imperative branches.
    /// </remarks>
#pragma warning disable CA1716 // Identifiers should not match keywords
    Error? Error { get; }
#pragma warning restore CA1716 // Identifiers should not match keywords

    /// <summary>
    /// Attempts to get the error without throwing. Companion to <see cref="Error"/> for callers
    /// that prefer <c>TryParse</c>-style imperative usage.
    /// </summary>
    /// <param name="error">When this method returns <see langword="true"/>, contains the error; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the result is a failure; otherwise <see langword="false"/>.</returns>
#pragma warning disable CA1716 // Identifiers should not match keywords — "error" mirrors the Error property name.
    bool TryGetError([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Error? error);
#pragma warning restore CA1716
}