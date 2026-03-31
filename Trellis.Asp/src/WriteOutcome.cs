namespace Trellis.Asp;

using Trellis;

/// <summary>
/// Represents the outcome of a write operation (PUT/POST/DELETE) per RFC 9110 §9.3.4.
/// </summary>
public abstract record WriteOutcome<T>
{
    private WriteOutcome() { }

    /// <summary>Resource was created — maps to 201 Created.</summary>
    public sealed record Created(T Value, string Location, RepresentationMetadata? Metadata = null) : WriteOutcome<T>;

    /// <summary>Resource was replaced/updated — maps to 200 OK.</summary>
    public sealed record Replaced(T Value, RepresentationMetadata? Metadata = null) : WriteOutcome<T>;

    /// <summary>Resource was replaced/updated with no body — maps to 204 No Content.</summary>
    public sealed record ReplacedNoContent(RepresentationMetadata? Metadata = null) : WriteOutcome<T>;

    /// <summary>Request accepted for async processing — maps to 202 Accepted.</summary>
    public sealed record Accepted(T? StatusBody = default, string? StatusMonitorUri = null, RetryAfterValue? RetryAfter = null) : WriteOutcome<T>;
}
