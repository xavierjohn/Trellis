namespace Trellis;

/// <summary>
/// Non-generic interface for all entities in Domain-Driven Design.
/// Provides automatic timestamp tracking — <see cref="CreatedAt"/> records when the entity
/// was first persisted, and <see cref="LastModified"/> tracks the most recent change.
/// </summary>
/// <remarks>
/// <para>
/// Timestamps are managed automatically by the Trellis EF Core interceptor
/// (<c>EntityTimestampInterceptor</c>) on save:
/// <list type="bullet">
/// <item><see cref="CreatedAt"/> is set once when the entity is first added</item>
/// <item><see cref="LastModified"/> is set on every add or modification</item>
/// </list>
/// </para>
/// <para>
/// Public setters are provided for EF Core materialization (loading from storage)
/// and data migration scenarios. Domain code should not set these directly —
/// they are infrastructure-managed.
/// </para>
/// <para>
/// This interface follows the same pattern as <see cref="IAggregate"/> — a non-generic
/// marker that enables type-safe detection in EF Core interceptors without requiring
/// knowledge of the generic type parameter.
/// </para>
/// </remarks>
public interface IEntity
{
    /// <summary>
    /// Gets or sets the UTC timestamp when this entity was first persisted.
    /// </summary>
    /// <value>
    /// The date and time in UTC when the entity was initially saved to storage.
    /// Defaults to <c>default(DateTimeOffset)</c> for unsaved entities.
    /// </value>
    /// <remarks>
    /// Set automatically by <c>EntityTimestampInterceptor</c> on <c>EntityState.Added</c>.
    /// Public setter is required for EF Core materialization and data migration.
    /// </remarks>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the most recent modification to this entity.
    /// </summary>
    /// <value>
    /// The date and time in UTC when the entity was last saved (created or updated).
    /// Defaults to <c>default(DateTimeOffset)</c> for unsaved entities.
    /// </value>
    /// <remarks>
    /// <para>
    /// Set automatically by <c>EntityTimestampInterceptor</c> on both
    /// <c>EntityState.Added</c> and <c>EntityState.Modified</c>.
    /// </para>
    /// <para>
    /// For aggregate roots, this value enables RFC 9110 date-based conditional
    /// requests (If-Modified-Since, If-Unmodified-Since) via <c>RepresentationMetadata</c>.
    /// </para>
    /// </remarks>
    DateTimeOffset LastModified { get; set; }
}