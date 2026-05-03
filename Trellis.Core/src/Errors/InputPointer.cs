namespace Trellis;

/// <summary>
/// A pointer into a structured input document, expressed as an RFC 6901 JSON Pointer.
/// Used by validation errors to identify the location of an offending value.
/// </summary>
/// <example>
/// <code>
/// new InputPointer("/email")
/// new InputPointer("/items/0/quantity")
/// new InputPointer("")            // root
/// </code>
/// </example>
public readonly record struct InputPointer
{
    private readonly string? _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="InputPointer"/> struct.
    /// </summary>
    /// <param name="Path">
    /// The JSON Pointer path (e.g. <c>"/email"</c>, <c>"/items/0/quantity"</c>). The root
    /// of the input is <c>""</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="Path"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="Path"/> is not an RFC 6901 JSON Pointer.</exception>
    public InputPointer(string Path)
    {
        ArgumentNullException.ThrowIfNull(Path);
        Validate(Path);
        _path = Path;
    }

    /// <summary>
    /// Gets the JSON Pointer path. A default <see cref="InputPointer"/> is observed as the root pointer (<c>""</c>).
    /// </summary>
    public string Path
    {
        get => _path ?? string.Empty;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            Validate(value);
            _path = value;
        }
    }

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

    /// <summary>
    /// Deconstructs the pointer into its JSON Pointer path.
    /// </summary>
    /// <param name="Path">The JSON Pointer path.</param>
    public void Deconstruct(out string Path) => Path = this.Path;

    /// <inheritdoc />
    public bool Equals(InputPointer other) => string.Equals(Path, other.Path, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path);

    private static void Validate(string Path)
    {
        if (Path.Length > 0 && Path[0] != '/')
            throw new ArgumentException("JSON Pointer paths must be empty or start with '/'.", nameof(Path));

        for (var i = 0; i < Path.Length; i++)
        {
            if (Path[i] != '~') continue;
            if (i == Path.Length - 1 || Path[i + 1] is not ('0' or '1'))
                throw new ArgumentException("JSON Pointer escape sequences must be '~0' or '~1'.", nameof(Path));
        }
    }
}
