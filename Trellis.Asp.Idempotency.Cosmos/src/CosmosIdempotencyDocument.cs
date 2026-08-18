namespace Trellis.Asp.Idempotency.Cosmos;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Persisted shape of an idempotency entry. Mirrors the state machine of the in-process
/// reference store: an entry is <em>reserved</em> while <see cref="Snapshot"/> is <c>null</c> and
/// <em>completed</em> once it is set.
/// </summary>
/// <remarks>
/// Cosmos DB adds its own <c>_etag</c>, <c>_ts</c>, <c>_rid</c>, and <c>_self</c> properties to
/// every item. They are deliberately absent here: unknown properties are ignored on read, and the
/// concurrency token is taken from the response headers rather than the body, so a replace never
/// has to echo them back.
/// </remarks>
internal sealed class CosmosIdempotencyDocument
{
    /// <summary>Base64Url-encoded idempotency key. Cosmos DB item ids may not contain <c>/ \ ? #</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Partition key. The idempotency scope, which already isolates tenants and actors.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>The un-encoded idempotency key, retained so stored items are readable in Data Explorer.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Fingerprint of the request that created or currently holds this entry.</summary>
    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Token identifying the outstanding reservation; <c>null</c> once completed.</summary>
    [JsonPropertyName("reservationId")]
    public string? ReservationId { get; set; }

    /// <summary>When the current reservation was taken, in Unix milliseconds.</summary>
    [JsonPropertyName("reservedAt")]
    public long ReservedAtUnixMs { get; set; }

    /// <summary>When the response was recorded, in Unix milliseconds; <c>null</c> while reserved.</summary>
    [JsonPropertyName("completedAt")]
    public long? CompletedAtUnixMs { get; set; }

    /// <summary>The captured response; <c>null</c> while reserved.</summary>
    [JsonPropertyName("snapshot")]
    public CosmosResponseSnapshotDocument? Snapshot { get; set; }

    /// <summary>
    /// Cosmos DB per-item time-to-live in seconds, or <see cref="NeverExpires"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A storage-reclamation backstop only — the store never trusts it for correctness, because
    /// Cosmos DB deletes expired items on a best-effort background sweep and can still return one.
    /// <see cref="ReservedAtUnixMs"/> and <see cref="CompletedAtUnixMs"/> are authoritative.
    /// </para>
    /// <para>
    /// Consequently, deletion may only ever be applied to a document that the store's own rules
    /// have already made unreachable. That holds for a <em>completed</em> document, which is
    /// treated as absent once it outlives the configured TTL, but not for a <em>reserved</em> one:
    /// a reserved document is answerable forever, because a request reusing the key with a
    /// different body must keep being rejected. A reserved document therefore carries
    /// <see cref="NeverExpires"/>; were it given a finite value, the store's answer would come to
    /// depend on whether a background sweep had happened to run.
    /// </para>
    /// </remarks>
    [JsonPropertyName("ttl")]
    public int Ttl { get; set; }

    /// <summary>
    /// The Cosmos DB per-item <c>ttl</c> meaning "never expire", which overrides the container's
    /// <c>DefaultTimeToLive</c>.
    /// </summary>
    public const int NeverExpires = -1;
}

/// <summary>Persisted form of <see cref="IdempotencyResponseSnapshot"/>.</summary>
internal sealed class CosmosResponseSnapshotDocument
{
    /// <summary>HTTP status code of the captured response.</summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    /// <summary>Captured response headers.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string[]> Headers { get; set; } = [];

    /// <summary>Captured response body. Serialized as base64 by System.Text.Json.</summary>
    [JsonPropertyName("body")]
    public byte[] Body { get; set; } = [];

    /// <summary>
    /// Fingerprint recorded on the snapshot. Stored separately from
    /// <see cref="CosmosIdempotencyDocument.Fingerprint"/> so a replay returns exactly what
    /// <see cref="IIdempotencyStore.CompleteAsync"/> was handed rather than a reconstruction.
    /// </summary>
    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}

/// <summary>
/// Source-generated serialization context. Reflection-based serialization would trip the
/// repository-wide AOT and trim analyzers, which are errors here.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CosmosIdempotencyDocument))]
internal sealed partial class CosmosIdempotencyJsonContext : JsonSerializerContext;