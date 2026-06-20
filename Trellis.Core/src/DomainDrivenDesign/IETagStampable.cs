namespace Trellis;

/// <summary>
/// Infrastructure seam that lets a persistence adapter stamp an aggregate's optimistic-concurrency
/// token (<see cref="IAggregate.ETag"/>) without reflection.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAggregate.ETag"/> is read-only on the domain surface — domain code must never assign it.
/// The Trellis EF Core integration stamps it through the change tracker (<c>AggregateETagInterceptor</c>),
/// which writes the value via EF metadata and never needs the CLR setter. A non-EF persistence adapter
/// (Dapper, raw ADO, a Cosmos SDK repository, ...) has no such mechanism, so <see cref="Aggregate{TId}"/>
/// implements this interface <b>explicitly</b>: the stamp method stays off the aggregate's public/domain
/// surface and is reachable only by code that deliberately casts to <see cref="IETagStampable"/>.
/// </para>
/// <para>
/// Typical adapter usage:
/// <code>
/// // On load: restore the stored concurrency token.
/// ((IETagStampable)aggregate).StampETag(row.ETag);
///
/// // On save: stamp a fresh, unquoted opaque token — e.g. Guid.NewGuid().ToString("N") to match the EF
/// // default — and use the previous value in the optimistic WHERE clause. A store-native token that comes
/// // quoted (such as a Cosmos _etag) must be normalized to its unquoted opaque form before stamping.
/// ((IETagStampable)aggregate).StampETag(newETag);
/// </code>
/// </para>
/// </remarks>
public interface IETagStampable
{
    /// <summary>
    /// Sets the aggregate's optimistic-concurrency token (<see cref="IAggregate.ETag"/>) to
    /// <paramref name="etag"/>. For persistence infrastructure only — not domain code.
    /// </summary>
    /// <param name="etag">
    /// The concurrency token to stamp. Must be a valid unquoted RFC 9110 §8.8.1 opaque tag — non-null,
    /// non-whitespace, and free of double-quotes and control characters (e.g. <c>Guid.NewGuid().ToString("N")</c>).
    /// This is the value the HTTP layer emits as a strong <c>ETag</c>, so a store-native quoted token
    /// (such as a Cosmos <c>_etag</c>) must be normalized to its unquoted form first.
    /// </param>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="etag"/> is <see langword="null"/>, empty, whitespace, or contains characters that are
    /// not valid in an RFC 9110 opaque tag.
    /// </exception>
    void StampETag(string etag);
}
