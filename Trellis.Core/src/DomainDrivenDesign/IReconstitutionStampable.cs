namespace Trellis;

/// <summary>
/// Infrastructure seam that lets a non-EF persistence adapter restore a reconstituted aggregate's
/// persistence-managed metadata — audit timestamps and the optimistic-concurrency token — and clear its
/// uncommitted domain events in one infra-only call.
/// </summary>
/// <remarks>
/// <para>
/// Reconstitution rebuilds an aggregate from already-persisted, known-good state <em>without</em> running
/// its <c>Create</c>/<c>TryCreate</c> factory — no invariant re-validation, no new identity, and no
/// creation events. The aggregate author owns rebuilding the <em>domain</em> state: expose a
/// <c>Reconstitute(...)</c> factory that calls the (private) constructor and assigns get-only properties
/// and private child collections directly. This seam restores only the <em>infrastructure</em> metadata —
/// the audit timestamps and the concurrency token — and clears any uncommitted domain events.
/// </para>
/// <para>
/// <see cref="Aggregate{TId}"/> implements this interface <b>explicitly</b>, so the method stays off the
/// aggregate's domain surface and is reachable only by a persistence adapter that casts to
/// <see cref="IReconstitutionStampable"/>. The Trellis EF Core integration performs the equivalent through
/// its materializer and interceptors.
/// </para>
/// <para>
/// Typical adapter usage:
/// <code>
/// // The author's factory rebuilds domain state (private ctor + child collections); the adapter then
/// // restores the infrastructure metadata it loaded from storage.
/// var order = Order.Reconstitute(row.Id, row.CustomerId, row.Status, lineRows);
/// ((IReconstitutionStampable)order).StampReconstitutedState(row.CreatedAt, row.LastModified, row.ETag);
/// return order;
/// </code>
/// </para>
/// </remarks>
public interface IReconstitutionStampable
{
    /// <summary>
    /// Restores the persistence-managed metadata of a reconstituted aggregate — the audit timestamps and
    /// the optimistic-concurrency token — and clears any uncommitted domain events so the aggregate
    /// represents already-persisted state. With the default event-based change tracking the aggregate then
    /// reports <see cref="System.ComponentModel.IChangeTracking.IsChanged"/> as <see langword="false"/>.
    /// For persistence infrastructure only — not domain code.
    /// </summary>
    /// <param name="createdAt">The stored creation timestamp.</param>
    /// <param name="lastModified">The stored last-modified timestamp.</param>
    /// <param name="etag">
    /// The stored optimistic-concurrency token. Must be a valid unquoted RFC 9110 §8.8.1 opaque tag
    /// (see <see cref="IETagStampable.StampETag(string)"/>).
    /// </param>
    /// <exception cref="System.ArgumentNullException"><paramref name="etag"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="etag"/> is empty, whitespace, or contains characters that are not valid in an
    /// RFC 9110 opaque tag.
    /// </exception>
    void StampReconstitutedState(DateTimeOffset createdAt, DateTimeOffset lastModified, string etag);
}
