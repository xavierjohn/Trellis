namespace Trellis;

using System.Collections.Generic;
using System.ComponentModel;

/// <summary>
/// Defines the contract for an aggregate root in Domain-Driven Design.
/// An aggregate is a cluster of domain objects that can be treated as a single unit for data changes.
/// </summary>
/// <remarks>
/// <para>
/// Aggregates are the primary building blocks of domain models in DDD. Key characteristics:
/// <list type="bullet">
/// <item>Aggregate roots are the only entry point for modifications to the aggregate</item>
/// <item>Ensures all business rules and invariants within the aggregate boundary are maintained</item>
/// <item>Tracks domain events that occur during state changes</item>
/// <item>External objects can only hold references to the aggregate root, not internal entities</item>
/// </list>
/// </para>
/// <para>
/// This interface combines change tracking (from <see cref="IChangeTracking"/>) with domain event management.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class Order : Aggregate&lt;OrderId&gt;
/// {
///     private readonly List&lt;OrderLine&gt; _lines = [];
///     
///     public Result&lt;Order&gt; AddLine(ProductId productId, int quantity)
///     {
///         // Business logic here
///         DomainEvents.Add(new OrderLineAddedEvent(Id, productId, quantity));
///         return this;
///     }
/// }
/// </code>
/// </example>
public interface IAggregate : IChangeTracking
{
    /// <summary>
    /// Gets all domain events that have been raised but not yet marked as committed.
    /// </summary>
    /// <returns>
    /// A read-only list of domain events that occurred during state changes since the last call to <see cref="IChangeTracking.AcceptChanges"/>.
    /// Returns an empty list if no uncommitted events exist.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Domain events represent significant state changes that have occurred within the aggregate.
    /// These events should be:
    /// <list type="bullet">
    /// <item>Published to an event bus or message broker after successful persistence</item>
    /// <item>Used to trigger side effects or update read models</item>
    /// <item>Cleared by calling <see cref="IChangeTracking.AcceptChanges"/> after successful processing</item>
    /// </list>
    /// </para>
    /// <para>
    /// Typical workflow:
    /// <code>
    /// // 1. Execute domain operation
    /// var result = order.Submit();
    /// 
    /// // 2. Save aggregate to repository
    /// await repository.SaveAsync(order);
    /// 
    /// // 3. Publish uncommitted events
    /// foreach (var evt in order.UncommittedEvents())
    /// {
    ///     await eventBus.PublishAsync(evt);
    /// }
    /// 
    /// // 4. Mark changes as committed
    /// order.AcceptChanges();
    /// </code>
    /// </para>
    /// </remarks>
    IReadOnlyList<IDomainEvent> UncommittedEvents();

    /// <summary>
    /// Gets the entity tag (ETag) for optimistic concurrency per RFC 9110.
    /// </summary>
    /// <value>
    /// An opaque string token that changes each time the aggregate is persisted.
    /// Used by the persistence layer to detect concurrent modifications and by the
    /// HTTP layer for conditional requests (<c>ETag</c>/<c>If-Match</c> headers).
    /// </value>
    /// <remarks>
    /// <para>
    /// The ETag is managed automatically by the persistence infrastructure:
    /// <list type="bullet">
    /// <item>For SQL databases, the EF Core interceptor generates a new GUID on each save</item>
    /// <item>For CosmosDB, the native <c>_etag</c> is used directly</item>
    /// </list>
    /// Domain code should not modify this property directly.
    /// </para>
    /// </remarks>
    string ETag { get; }
}