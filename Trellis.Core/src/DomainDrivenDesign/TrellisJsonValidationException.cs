namespace Trellis;

using System.Text.Json;

/// <summary>
/// Thrown by Trellis JSON converters when a structured value object's invariants
/// are violated during deserialization (e.g., <c>Money</c>'s amount/currency rules).
/// </summary>
/// <remarks>
/// <para>
/// This is a marker subclass of <see cref="JsonException"/>. <c>Trellis.Asp</c>'s
/// <c>ScalarValueValidationMiddleware</c> recognizes it and surfaces the message
/// (and <see cref="JsonException.Path"/>, when present) in the resulting Problem
/// Details payload — restoring DX parity with MVC's model-binder error reporting,
/// which already includes per-field <see cref="JsonException"/> messages.
/// </para>
/// <para>
/// Plain <see cref="JsonException"/>s (e.g., those thrown by <c>System.Text.Json</c>
/// when a JSON value can't be coerced to a CLR type) are deliberately not surfaced
/// because their messages can include internal type names. Trellis converters opt
/// in to message surfacing by throwing this subclass with a curated message — for
/// example <c>error.GetDisplayMessage()</c> from a <see cref="Result"/> failure.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Roslynator",
    "RCS1194:Implement exception constructors",
    Justification = "JsonException's protected serialization ctor and 4/5-arg ctors are intentionally not re-exposed; the three standard ctors cover all supported usage.")]
public sealed class TrellisJsonValidationException : JsonException
{
    /// <summary>Default constructor (RCS1194).</summary>
    public TrellisJsonValidationException()
    {
    }

    /// <summary>Creates an instance with the supplied curated, user-safe message.</summary>
    public TrellisJsonValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Wraps an inner exception with the supplied message.</summary>
    public TrellisJsonValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}