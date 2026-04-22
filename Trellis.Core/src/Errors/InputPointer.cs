namespace Trellis;

/// <summary>
/// A pointer into a structured input document, expressed as an RFC 6901 JSON Pointer.
/// Used by validation errors to identify the location of an offending value.
/// </summary>
/// <param name="Path">
/// The JSON Pointer path (e.g. <c>"/email"</c>, <c>"/items/0/quantity"</c>). The root
/// of the input is <c>""</c>.
/// </param>
/// <example>
/// <code>
/// new InputPointer("/email")
/// new InputPointer("/items/0/quantity")
/// new InputPointer("")            // root
/// </code>
/// </example>
public readonly record struct InputPointer(string Path)
{
    /// <summary>
    /// A pointer to the root of the input document.
    /// </summary>
    public static InputPointer Root => new("");

    /// <summary>
    /// Builds an <see cref="InputPointer"/> from a property name, prepending <c>"/"</c>
    /// if the value is not already a JSON Pointer.
    /// </summary>
    /// <param name="propertyName">A simple property name or full JSON Pointer.</param>
    /// <returns>An <see cref="InputPointer"/>.</returns>
    /// <remarks>
    /// When the input is a simple property name (does not start with <c>'/'</c>), the special
    /// characters defined by RFC 6901 §3 are escaped: <c>'~'</c> becomes <c>"~0"</c> and
    /// <c>'/'</c> becomes <c>"~1"</c>. The order is significant — <c>'~'</c> is escaped first
    /// so that an already-introduced <c>"~1"</c> from a slash escape is not re-escaped as
    /// <c>"~01"</c>. Inputs that already start with <c>'/'</c> are assumed to be a fully-formed
    /// JSON Pointer (e.g. produced by <c>JsonPointerNormalizer</c>) and are passed through unchanged.
    /// </remarks>
    public static InputPointer ForProperty(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return Root;
        if (propertyName.StartsWith('/'))
            return new(propertyName);

        var escaped = propertyName.Replace("~", "~0", StringComparison.Ordinal)
                                  .Replace("/", "~1", StringComparison.Ordinal);
        return new("/" + escaped);
    }

    /// <inheritdoc />
    public override string ToString() => Path;
}
