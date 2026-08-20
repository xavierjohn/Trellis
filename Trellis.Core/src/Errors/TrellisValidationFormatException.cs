namespace Trellis;

/// <summary>
/// A <see cref="FormatException"/> that also carries the structured validation failure that
/// caused it.
/// </summary>
/// <remarks>
/// <para>
/// A parse failure has to satisfy two contracts at once, and no single exception type satisfies
/// both. <see cref="IParsable{TSelf}"/> requires malformed input to be signalled with a
/// <see cref="FormatException"/>; the ASP boundary recognizes a structured validation failure only
/// through <see cref="TrellisJsonValidationException"/>, which is a
/// <see cref="System.Text.Json.JsonException"/>. So the failure crosses the boundary in two hops:
/// <c>Parse</c> throws this type, and the JSON converter that called <c>Parse</c> rethrows it as a
/// <see cref="TrellisJsonValidationException"/> carrying the same <see cref="InvalidInput"/>.
/// </para>
/// <para>
/// Deriving from <see cref="FormatException"/> rather than introducing a sibling type is what keeps
/// existing <c>catch (FormatException)</c> sites matching, and the base message is the unchanged
/// flattened parse message, so a caller that catches and logs sees exactly what it saw before.
/// The structure is strictly additional.
/// </para>
/// <para>
/// It lives in <c>Trellis.Core</c>, and is public, because it is thrown from
/// <c>Trellis.Primitives</c> and caught in <c>Trellis.Core</c>. Making it internal would put it out
/// of reach of the throwing sites, and moving it to <c>Trellis.Primitives</c> would force Core's
/// converter to reference Primitives, inverting the dependency.
/// </para>
/// </remarks>
public sealed class TrellisValidationFormatException : FormatException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisValidationFormatException"/> class.
    /// </summary>
    public TrellisValidationFormatException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisValidationFormatException"/> class
    /// with a message.
    /// </summary>
    /// <param name="message">The flattened parse message, unchanged from what callers saw before.</param>
    public TrellisValidationFormatException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisValidationFormatException"/> class
    /// with a message and an inner exception.
    /// </summary>
    /// <param name="message">The flattened parse message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public TrellisValidationFormatException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TrellisValidationFormatException"/> class
    /// carrying the structured failure.
    /// </summary>
    /// <param name="message">The flattened parse message, unchanged from what callers saw before.</param>
    /// <param name="invalidInput">The structured failure the parse produced.</param>
    public TrellisValidationFormatException(string? message, Error.InvalidInput? invalidInput)
        : base(message) => InvalidInput = invalidInput;

    /// <summary>
    /// Gets the structured validation failure, when the parse produced one.
    /// </summary>
    public Error.InvalidInput? InvalidInput { get; init; }
}
