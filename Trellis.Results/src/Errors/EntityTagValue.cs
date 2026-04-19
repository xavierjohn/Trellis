namespace Trellis;

/// <summary>
/// Represents an RFC 9110 §8.8.1 entity tag (ETag) value with explicit weak/strong semantics.
/// </summary>
/// <remarks>
/// <para>
/// An entity tag consists of an opaque quoted string, optionally prefixed with a weakness
/// indicator (<c>W/</c>). Entity tags are used for conditional requests and cache validation.
/// </para>
/// <para>
/// Create instances using <see cref="Strong"/> or <see cref="Weak"/> factory methods,
/// or parse from header values using <see cref="TryParse"/>.
/// Compare using <see cref="StrongEquals"/> or <see cref="WeakEquals"/> per RFC 9110 §8.8.3.2.
/// Format for HTTP headers using <see cref="ToHeaderValue"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var strong = EntityTagValue.Strong("abc123");
/// var weak = EntityTagValue.Weak("abc123");
///
/// // Parse from header
/// var parsed = EntityTagValue.TryParse("W/\"abc123\"");
///
/// // RFC 9110 §8.8.3.2 comparison
/// strong.StrongEquals(EntityTagValue.Strong("abc123")); // true
/// strong.WeakEquals(weak); // true
/// </code>
/// </example>
public sealed record EntityTagValue
{
    /// <summary>
    /// Gets the raw opaque tag string without quotes or <c>W/</c> prefix.
    /// </summary>
    public string OpaqueTag { get; }

    /// <summary>
    /// Gets whether this is a weak entity tag.
    /// </summary>
    /// <value><c>true</c> if weak; <c>false</c> if strong.</value>
    public bool IsWeak { get; }

    /// <summary>
    /// Gets whether this instance represents the RFC 9110 wildcard <c>*</c> token
    /// (as opposed to a literal ETag with opaque-tag <c>*</c>).
    /// </summary>
    public bool IsWildcard { get; }

    private EntityTagValue(string opaqueTag, bool isWeak, bool isWildcard = false)
    {
        ArgumentNullException.ThrowIfNull(opaqueTag);
        if (!isWildcard)
            ValidateOpaqueTag(opaqueTag);
        OpaqueTag = opaqueTag;
        IsWeak = isWeak;
        IsWildcard = isWildcard;
    }

    private static bool HasInvalidOpaqueTagChars(string opaqueTag)
    {
        foreach (var c in opaqueTag)
        {
            // RFC 9110 §8.8.1: etagc = %x21 / %x23-7E / obs-text (0x80-FF)
            if (c is < '\x21' or '"' or '\x7F' or > '\xFF')
                return true;
        }

        return false;
    }

    private static void ValidateOpaqueTag(string opaqueTag)
    {
        foreach (var c in opaqueTag)
        {
            // RFC 9110 §8.8.1: etagc = %x21 / %x23-7E / obs-text (0x80-FF)
            if (c is < '\x21' or '"' or '\x7F' or > '\xFF')
                throw new ArgumentException(
                    $"Invalid character in opaque tag: U+{(int)c:X4}. " +
                    "Opaque tags may only contain %x21, %x23-7E, and obs-text (0x80-FF) per RFC 9110 §8.8.1.",
                    nameof(opaqueTag));
        }
    }

    /// <summary>Creates a strong entity tag.</summary>
    /// <param name="opaqueTag">The opaque tag string.</param>
    /// <returns>A new strong <see cref="EntityTagValue"/>.</returns>
    public static EntityTagValue Strong(string opaqueTag) => new(opaqueTag, false);

    /// <summary>Creates a weak entity tag.</summary>
    /// <param name="opaqueTag">The opaque tag string.</param>
    /// <returns>A new weak <see cref="EntityTagValue"/>.</returns>
    public static EntityTagValue Weak(string opaqueTag) => new(opaqueTag, true);

    /// <summary>
    /// Creates the RFC 9110 wildcard entity tag (<c>*</c>), which matches any current entity.
    /// This is semantically distinct from <c>Strong("*")</c>, which is a literal ETag with opaque-tag <c>*</c>.
    /// </summary>
    /// <returns>A wildcard <see cref="EntityTagValue"/>.</returns>
    public static EntityTagValue Wildcard() => new("*", false, isWildcard: true);

    /// <summary>
    /// Parses an entity tag from its HTTP header representation.
    /// </summary>
    /// <param name="headerValue">
    /// The header value to parse. Expected formats: <c>"tag"</c> for strong or <c>W/"tag"</c> for weak.
    /// </param>
    /// <returns>
    /// A <see cref="Result{TValue}"/> containing the parsed <see cref="EntityTagValue"/> on success,
    /// or a <see cref="BadRequestError"/> on failure.
    /// </returns>
    /// <example>
    /// <code>
    /// var strong = EntityTagValue.TryParse("\"abc123\"");
    /// var weak = EntityTagValue.TryParse("W/\"abc123\"");
    /// </code>
    /// </example>
    public static Result<EntityTagValue> TryParse(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return Result.Fail<EntityTagValue>(Error.BadRequest("ETag header value cannot be null or empty.", "etag.parse.error", null));

        if (headerValue.StartsWith("W/\"", StringComparison.Ordinal) && headerValue.EndsWith('"') && headerValue.Length >= 4)
        {
            var tag = headerValue[3..^1];
            if (HasInvalidOpaqueTagChars(tag))
                return Result.Fail<EntityTagValue>(Error.BadRequest("Invalid ETag format.", "etag.parse.error", null));
            return Result.Ok(new EntityTagValue(tag, true));
        }

        if (headerValue.StartsWith('"') && headerValue.EndsWith('"') && headerValue.Length >= 2)
        {
            var tag = headerValue[1..^1];
            if (HasInvalidOpaqueTagChars(tag))
                return Result.Fail<EntityTagValue>(Error.BadRequest("Invalid ETag format.", "etag.parse.error", null));
            return Result.Ok(new EntityTagValue(tag, false));
        }

        return Result.Fail<EntityTagValue>(Error.BadRequest("Invalid ETag format.", "etag.parse.error", null));
    }

    /// <summary>
    /// Performs a strong comparison per RFC 9110 §8.8.3.2.
    /// Both tags must be strong and their opaque tags must match character-by-character.
    /// Wildcards never match in entity tag comparison — they are a precondition token, not a tag.
    /// </summary>
    public bool StrongEquals(EntityTagValue other) =>
        !IsWildcard && !other.IsWildcard && !IsWeak && !other.IsWeak && OpaqueTag == other.OpaqueTag;

    /// <summary>
    /// Performs a weak comparison per RFC 9110 §8.8.3.2.
    /// Only the opaque tags must match; the weakness indicator is ignored.
    /// Wildcards never match in entity tag comparison — they are a precondition token, not a tag.
    /// </summary>
    public bool WeakEquals(EntityTagValue other) =>
        !IsWildcard && !other.IsWildcard && OpaqueTag == other.OpaqueTag;

    /// <summary>
    /// Formats this entity tag for use in an HTTP header.
    /// Returns <c>*</c> for the wildcard, <c>"tag"</c> for strong tags, or <c>W/"tag"</c> for weak tags.
    /// </summary>
    /// <returns>The formatted HTTP header value.</returns>
    public string ToHeaderValue() =>
        IsWildcard ? "*" : IsWeak ? $"W/\"{OpaqueTag}\"" : $"\"{OpaqueTag}\"";

    /// <inheritdoc />
    public override string ToString() => ToHeaderValue();
}