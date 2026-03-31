namespace Trellis;

/// <summary>
/// Interface for aggregates and entities that track their last modification timestamp.
/// When implemented alongside <see cref="IAggregate"/>, enables date-based conditional
/// request support (If-Modified-Since, If-Unmodified-Since) per RFC 9110 §8.8.2.
/// </summary>
/// <remarks>
/// <para>
/// The <c>LastModified</c> property is automatically set by Trellis EF Core interceptors
/// on save (similar to how <see cref="IAggregate.ETag"/> is auto-generated).
/// </para>
/// <para>
/// This is optional — aggregates only need to implement this interface when date-based
/// conditional requests are desired alongside ETag-based validation.
/// </para>
/// </remarks>
public interface ITrackLastModified
{
    /// <summary>
    /// Gets or sets the last modification timestamp of this entity.
    /// Automatically managed by Trellis EF Core interceptors.
    /// </summary>
    DateTimeOffset LastModified { get; set; }
}
