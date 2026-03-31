namespace Trellis.Asp;

using Trellis;

/// <summary>
/// Default representation validator for aggregates. Uses the aggregate's built-in ETag.
/// When a variant key is provided, combines it with the ETag using a hash.
/// </summary>
public sealed class AggregateRepresentationValidator<T> : IRepresentationValidator<T>
    where T : IAggregate
{
    /// <inheritdoc />
    public EntityTagValue GenerateETag(T value, string? variantKey = null)
    {
        if (string.IsNullOrEmpty(variantKey))
            return EntityTagValue.Strong(value.ETag);

        // Combine ETag + variant key into a new strong ETag
        var combined = $"{value.ETag}:{variantKey}";
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(combined)))[..16];
        return EntityTagValue.Strong(hash);
    }
}
