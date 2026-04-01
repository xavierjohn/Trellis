namespace Trellis;

/// <summary>
/// Carries HTTP representation metadata (RFC 9110 §8) through Trellis response mappers.
/// Used to emit <c>ETag</c>, <c>Last-Modified</c>, <c>Vary</c>, <c>Content-Language</c>,
/// <c>Content-Location</c>, and <c>Accept-Ranges</c> response headers consistently
/// across MVC and Minimal API responses (200, 201, 206, 304).
/// </summary>
public sealed class RepresentationMetadata
{
    /// <summary>Gets the entity tag validator for the selected representation.</summary>
    public EntityTagValue? ETag { get; }

    /// <summary>Gets the last modification date of the selected representation.</summary>
    public DateTimeOffset? LastModified { get; }

    /// <summary>Gets the Vary header field names indicating which request fields influenced selection.</summary>
    public IReadOnlyList<string>? Vary { get; }

    /// <summary>Gets the Content-Language values for the selected representation.</summary>
    public IReadOnlyList<string>? ContentLanguage { get; }

    /// <summary>Gets the Content-Location URI for the selected representation.</summary>
    public string? ContentLocation { get; }

    /// <summary>Gets the Accept-Ranges value (e.g., "bytes" or "none").</summary>
    public string? AcceptRanges { get; }

    private RepresentationMetadata(
        EntityTagValue? eTag,
        DateTimeOffset? lastModified,
        IReadOnlyList<string>? vary,
        IReadOnlyList<string>? contentLanguage,
        string? contentLocation,
        string? acceptRanges)
    {
        ETag = eTag;
        LastModified = lastModified;
        Vary = vary;
        ContentLanguage = contentLanguage;
        ContentLocation = contentLocation;
        AcceptRanges = acceptRanges;
    }

    /// <summary>Creates a new <see cref="Builder"/> for constructing <see cref="RepresentationMetadata"/>.</summary>
    public static Builder Create() => new();

    /// <summary>Creates metadata with just an ETag.</summary>
    /// <param name="eTag">The entity tag value.</param>
    /// <returns>A new <see cref="RepresentationMetadata"/> containing only the specified ETag.</returns>
    public static RepresentationMetadata WithETag(EntityTagValue eTag) =>
        new(eTag, null, null, null, null, null);

    /// <summary>Creates metadata with just a strong ETag from an opaque tag string.</summary>
    /// <param name="opaqueTag">The opaque tag string for a strong entity tag.</param>
    /// <returns>A new <see cref="RepresentationMetadata"/> containing only a strong ETag.</returns>
    public static RepresentationMetadata WithStrongETag(string opaqueTag) =>
        new(EntityTagValue.Strong(opaqueTag), null, null, null, null, null);

    /// <summary>
    /// Fluent builder for constructing <see cref="RepresentationMetadata"/> instances.
    /// </summary>
    public sealed class Builder
    {
        private EntityTagValue? _eTag;
        private DateTimeOffset? _lastModified;
        private List<string>? _vary;
        private List<string>? _contentLanguage;
        private string? _contentLocation;
        private string? _acceptRanges;

        /// <summary>Sets the entity tag value.</summary>
        /// <param name="eTag">The entity tag.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder SetETag(EntityTagValue eTag)
        {
            _eTag = eTag;
            return this;
        }

        /// <summary>Sets a strong entity tag from an opaque tag string.</summary>
        /// <param name="opaqueTag">The opaque tag string.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder SetStrongETag(string opaqueTag)
        {
            _eTag = EntityTagValue.Strong(opaqueTag);
            return this;
        }

        /// <summary>Sets a weak entity tag from an opaque tag string.</summary>
        /// <param name="opaqueTag">The opaque tag string.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder SetWeakETag(string opaqueTag)
        {
            _eTag = EntityTagValue.Weak(opaqueTag);
            return this;
        }

        /// <summary>Sets the last modification date.</summary>
        /// <param name="lastModified">The last modification date.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder SetLastModified(DateTimeOffset lastModified)
        {
            _lastModified = lastModified;
            return this;
        }

        /// <summary>Adds one or more Vary field names, deduplicating case-insensitively.</summary>
        /// <param name="fieldNames">The request header field names that influenced representation selection.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder AddVary(params string[] fieldNames)
        {
            _vary ??= [];
            foreach (var name in fieldNames)
                if (!_vary.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _vary.Add(name);
            return this;
        }

        /// <summary>Adds one or more Content-Language values.</summary>
        /// <param name="languages">The language tags for the selected representation.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder AddContentLanguage(params string[] languages)
        {
            _contentLanguage ??= [];
            foreach (var lang in languages)
                if (!_contentLanguage.Contains(lang, StringComparer.OrdinalIgnoreCase))
                    _contentLanguage.Add(lang);
            return this;
        }

        /// <summary>Sets the Content-Location URI.</summary>
        /// <param name="uri">The URI for the selected representation.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder SetContentLocation(string uri)
        {
            _contentLocation = uri;
            return this;
        }

        /// <summary>Sets the Accept-Ranges value.</summary>
        /// <param name="value">The accept-ranges value (e.g., "bytes" or "none").</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder SetAcceptRanges(string value)
        {
            _acceptRanges = value;
            return this;
        }

        /// <summary>Builds the <see cref="RepresentationMetadata"/> instance from the current builder state.</summary>
        /// <returns>A new <see cref="RepresentationMetadata"/> instance.</returns>
        public RepresentationMetadata Build() =>
            new(
                _eTag,
                _lastModified,
                _vary is { Count: > 0 } ? (IReadOnlyList<string>)[.. _vary] : null,
                _contentLanguage is { Count: > 0 } ? (IReadOnlyList<string>)[.. _contentLanguage] : null,
                _contentLocation,
                _acceptRanges);
    }
}
