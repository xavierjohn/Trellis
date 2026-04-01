namespace Trellis.Asp;

using Trellis;

/// <summary>
/// Strategy for generating representation-specific validators (ETags) that account for
/// content negotiation variants per RFC 9110 §8.8.3.1/§8.8.3.3.
/// </summary>
/// <remarks>
/// <para>
/// The default Trellis behavior generates ETags from aggregate state only.
/// When the same resource has multiple representations (different encodings, languages,
/// media types, or projections), each variant needs a distinct ETag.
/// </para>
/// <para>
/// Implement this interface to combine domain version with variant metadata.
/// Register as a service; Trellis response mappers will use it when available.
/// </para>
/// </remarks>
/// <typeparam name="T">The domain type being represented.</typeparam>
public interface IRepresentationValidator<in T>
{
    /// <summary>
    /// Generates an EntityTagValue for the given value and representation variant.
    /// </summary>
    /// <param name="value">The domain value.</param>
    /// <param name="variantKey">Optional variant key (e.g., encoding, language, projection ID).</param>
    /// <returns>The entity tag for this specific representation.</returns>
    EntityTagValue GenerateETag(T value, string? variantKey = null);
}
