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
    /// Initializes a new instance of the <see cref="InputPointer"/> struct with an explicit
    /// input location.
    /// </summary>
    /// <param name="Path">
    /// The JSON Pointer path. For <see cref="InputLocation.Query"/>, <see cref="InputLocation.Path"/>
    /// and <see cref="InputLocation.Header"/> this is a single escaped token naming the parameter;
    /// prefer <see cref="ForQuery"/>, <see cref="ForPath"/> and <see cref="ForHeader"/>, which do
    /// that escaping.
    /// </param>
    /// <param name="In">Which part of the input the value came from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Path"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="Path"/> is not an RFC 6901 JSON Pointer.</exception>
    public InputPointer(string Path, InputLocation In)
        : this(Path) => this.In = In;

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
    /// Gets which part of the input the value came from. Defaults to
    /// <see cref="InputLocation.Unspecified"/>, which projects as <c>"unknown"</c>.
    /// </summary>
    public InputLocation In { get; init; }

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
    /// Builds a body pointer, stamping <see cref="InputLocation.Body"/>.
    /// </summary>
    /// <param name="jsonPointer">A simple property name or full JSON Pointer.</param>
    /// <returns>An <see cref="InputPointer"/> located in the request body.</returns>
    /// <remarks>
    /// This takes a <em>pointer</em>, not a name, with escaping semantics identical to
    /// <see cref="ForProperty"/>: a leading <c>'/'</c> is passed through as an already-formed
    /// pointer, so <c>ForBody("/items/0/quantity")</c> addresses a nested value rather than a
    /// field literally named <c>/items/0/quantity</c>. It is therefore exempt from the
    /// single-token escaping applied by <see cref="ForQuery"/>, <see cref="ForPath"/> and
    /// <see cref="ForHeader"/>.
    /// <para>
    /// Empty input yields the root path stamped <see cref="InputLocation.Body"/>, which is
    /// deliberately <em>not</em> <see cref="Root"/>: the root pointer is the documented idiom
    /// for a whole-body violation, and stamping it is what lets such a violation project as
    /// <c>body</c> rather than <c>unknown</c>.
    /// </para>
    /// </remarks>
    public static InputPointer ForBody(string jsonPointer) =>
        ForProperty(jsonPointer) with { In = InputLocation.Body };

    /// <summary>
    /// Builds a pointer to a query-string parameter, stamping <see cref="InputLocation.Query"/>.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>An <see cref="InputPointer"/> located in the query string.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c> or empty.</exception>
    /// <remarks>See <see cref="ForName"/> for the escaping rules.</remarks>
    public static InputPointer ForQuery(string name) => ForName(name, InputLocation.Query);

    /// <summary>
    /// Builds a pointer to a route (path) parameter, stamping <see cref="InputLocation.Path"/>.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <returns>An <see cref="InputPointer"/> located in the route.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c> or empty.</exception>
    /// <remarks>See <see cref="ForName"/> for the escaping rules.</remarks>
    public static InputPointer ForPath(string name) => ForName(name, InputLocation.Path);

    /// <summary>
    /// Builds a pointer to a request header, stamping <see cref="InputLocation.Header"/>.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <returns>An <see cref="InputPointer"/> located in the request headers.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c> or empty.</exception>
    /// <remarks>See <see cref="ForName"/> for the escaping rules.</remarks>
    public static InputPointer ForHeader(string name) => ForName(name, InputLocation.Header);

    /// <summary>
    /// Builds a pointer that addresses a named parameter rather than a document location.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="location">The location to stamp.</param>
    /// <returns>An <see cref="InputPointer"/> whose path is a single escaped token.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c> or empty.</exception>
    /// <remarks>
    /// The name is stored as a <em>single</em> RFC 6901-escaped token, so a parameter named
    /// <c>a/b</c> becomes <c>/a~1b</c> — one parameter, not two path segments. Unlike
    /// <see cref="ForProperty"/> and <see cref="ForBody"/>, a leading <c>'/'</c> is escaped
    /// rather than treated as an already-formed pointer, because a parameter named <c>/id</c>
    /// is a name and the name factories never reinterpret it as a location. Projection
    /// recovers the name by unescaping that single token.
    /// <para>
    /// An empty name is rejected rather than collapsing to root: the root pointer is
    /// meaningful for a body location and meaningless for a named parameter.
    /// </para>
    /// </remarks>
    private static InputPointer ForName(string name, InputLocation location)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("A parameter name must not be null or empty.", nameof(name));

        var escaped = name.Replace("~", "~0", StringComparison.Ordinal)
                          .Replace("/", "~1", StringComparison.Ordinal);
        return new("/" + escaped, location);
    }

    /// <summary>
    /// Deconstructs the pointer into its JSON Pointer path.
    /// </summary>
    /// <param name="Path">The JSON Pointer path.</param>
    public void Deconstruct(out string Path) => Path = this.Path;

    /// <summary>
    /// Deconstructs the pointer into its JSON Pointer path and input location.
    /// </summary>
    /// <param name="Path">The JSON Pointer path.</param>
    /// <param name="In">Which part of the input the value came from.</param>
    public void Deconstruct(out string Path, out InputLocation In)
    {
        Path = this.Path;
        In = this.In;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Identity is <see cref="Path"/> <em>and</em> <see cref="In"/>. Path alone would make
    /// pointers that project to different wire locations compare equal, which would let
    /// de-duplication collapse distinct failures into one.
    /// </remarks>
    public bool Equals(InputPointer other) =>
        In == other.In && string.Equals(Path, other.Path, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Path), In);

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