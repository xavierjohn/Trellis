namespace Trellis;

using System.Text.Json.Serialization;

/// <summary>
/// Base class for aggregate roots in Domain-Driven Design.
/// An aggregate is a cluster of domain objects (entities and value objects) that form a consistency boundary.
/// The aggregate root is the only entry point for modifications and ensures all invariants within the boundary are maintained.
/// </summary>
/// <typeparam name="TId">The type of the aggregate root's unique identifier. Must be non-nullable.</typeparam>
/// <remarks>
/// <para>
/// Aggregates are the fundamental building blocks for maintaining consistency in DDD. Key characteristics:
/// <list type="bullet">
/// <item>Consistency boundary: All business rules and invariants within the aggregate are enforced</item>
/// <item>Single entry point: External objects can only modify the aggregate through the aggregate root</item>
/// <item>Transaction boundary: Changes to an aggregate are typically saved atomically</item>
/// <item>Event source: Aggregates raise domain events to communicate state changes</item>
/// <item>Lifecycle: The root controls the lifecycle of all entities within the aggregate</item>
/// </list>
/// </para>
/// <para>
/// Design principles for aggregates:
/// <list type="bullet">
/// <item><strong>Keep them small</strong>: Only include entities that must change together</item>
/// <item><strong>Reference by ID</strong>: Use IDs to reference other aggregates, not object references</item>
/// <item><strong>Enforce invariants</strong>: All business rules must be maintained after each operation</item>
/// <item><strong>Eventual consistency</strong>: Use domain events for cross-aggregate consistency</item>
/// <item><strong>Protect internals</strong>: Internal entities should not be exposed outside the aggregate</item>
/// </list>
/// </para>
/// <para>
/// This base class combines:
/// <list type="bullet">
/// <item><see cref="Entity{TId}"/>: Provides identity-based equality</item>
/// <item><see cref="IAggregate"/>: Provides domain event tracking</item>
/// <item><see cref="System.ComponentModel.IChangeTracking"/>: Provides change tracking support</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Simple aggregate with validation and domain events:
/// <code><![CDATA[
/// public class Order : Aggregate<OrderId>
/// {
///     private readonly List<OrderLine> _lines = [];
///     
///     public CustomerId CustomerId { get; private set; }
///     public OrderStatus Status { get; private set; }
///     public Money Total { get; private set; }
///     public DateTime? SubmittedAt { get; private set; }
///     
///     // CreatedAt and LastModified are inherited from Entity&lt;TId&gt;
///     // and automatically managed by EntityTimestampInterceptor.
///     
///     // Internal entities are protected and accessed through methods
///     public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
///     
///     private Order(OrderId id, CustomerId customerId)
///         : base(id)
///     {
///         CustomerId = customerId;
///         Status = OrderStatus.Draft;
///         Total = Money.Zero("USD").Match(
///             m => m,
///             err => throw new System.InvalidOperationException(err.GetDisplayMessage()));
///     }
///     
///     public static Result<Order> Create(CustomerId customerId) =>
///         customerId.ToResult(Error.InvalidInput.ForField("customerId", "invalid", "Customer ID required"))
///             .Map(id => new Order(OrderId.NewUniqueV7(), id));
///     
///     // All modifications go through methods that enforce invariants.
///     // Money.Multiply / Money.Add return Result&lt;Money&gt; (multiplication and addition can
///     // fail on currency mismatch or overflow); compose with Bind so AddLine surfaces those
///     // as a typed failure rather than silently dropping them.
///     public Result&lt;Order&gt; AddLine(ProductId productId, int quantity, Money unitPrice) =>
///         this.ToResult()
///             .Ensure(_ => Status == OrderStatus.Draft,
///                    Error.InvalidInput.ForRule("invalid", "Cannot modify submitted order"))
///             .Ensure(_ => quantity > 0,
///                    Error.InvalidInput.ForField("quantity", "invalid", "Quantity must be positive"))
///             .Ensure(_ => _lines.Count &lt; 100,
///                    Error.InvalidInput.ForRule("invalid", "Order cannot have more than 100 lines"))
///             .Bind(order => unitPrice.Multiply(quantity)
///                 .Bind(lineTotal => Total.Add(lineTotal))
///                 .Tap(newTotal =>
///                 {
///                     _lines.Add(new OrderLine(productId, quantity, unitPrice));
///                     Total = newTotal;
///                     DomainEvents.Add(new OrderLineAddedEvent(Id, productId, quantity));
///                 })
///                 .Map(_ => order));
///     
///     public Result<Order> Submit() =>
///         this.ToResult()
///             .Ensure(_ => Status == OrderStatus.Draft,
///                    Error.InvalidInput.ForRule("invalid", "Order already submitted"))
///             .Ensure(_ => _lines.Count > 0,
///                    Error.InvalidInput.ForRule("invalid", "Cannot submit empty order"))
///             .Tap(_ =>
///             {
///                 Status = OrderStatus.Submitted;
///                 SubmittedAt = DateTime.UtcNow;
///                 
///                 // Raise domain event
///                 DomainEvents.Add(new OrderSubmittedEvent(Id, CustomerId, Total, SubmittedAt.Value));
///             });
/// }
/// 
/// // Internal entity - never exposed outside the aggregate
/// internal class OrderLine : Entity<Guid>
/// {
///     public ProductId ProductId { get; }
///     public int Quantity { get; }
///     public Money UnitPrice { get; }
///     
///     internal OrderLine(ProductId productId, int quantity, Money unitPrice)
///         : base(Guid.NewGuid())
///     {
///         ProductId = productId;
///         Quantity = quantity;
///         UnitPrice = unitPrice;
///     }
/// }
/// ]]></code>
/// </example>
/// <example>
/// NOTE: Repositories should stage aggregate changes only. Do not publish
/// <see cref="IDomainEvent"/> instances manually from repository <c>SaveAsync</c>
/// methods or call <c>AcceptChanges()</c> there. Use the Trellis Mediator
/// unit-of-work pipeline plus tracked aggregate domain-event dispatch so events
/// are dispatched after a successful transaction commit (<c>AddTrellisUnitOfWork&lt;TContext&gt;()</c>
/// with <c>AddTrackedAggregateDomainEventDispatch()</c>, or the matching
/// ServiceDefaults <c>UseEntityFrameworkUnitOfWork&lt;TContext&gt;()</c> and
/// <c>UseTrackedAggregateDomainEvents()</c> slots).
/// </example>
public abstract class Aggregate<TId> : Entity<TId>, IAggregate, IETagStampable, IReconstitutionStampable
    where TId : notnull
{
    /// <summary>
    /// Gets the list of domain events that have been raised but not yet committed.
    /// </summary>
    /// <value>
    /// A mutable list of domain events. Add events to this list when state changes occur.
    /// </value>
    /// <remarks>
    /// <para>
    /// This property is protected to allow derived classes to add events using:
    /// <code>DomainEvents.Add(new SomethingHappenedEvent(...));</code>
    /// </para>
    /// <para>
    /// Events should be added within methods that change state, typically inside Tap or Map operations
    /// to ensure they're only added when the operation succeeds.
    /// </para>
    /// </remarks>
    protected List<IDomainEvent> DomainEvents { get; } = [];

    /// <summary>
    /// Gets the entity tag (ETag) for optimistic concurrency per RFC 9110.
    /// </summary>
    /// <value>
    /// An opaque string token that is automatically generated each time the aggregate
    /// is persisted by the infrastructure layer.
    /// </value>
    /// <remarks>
    /// <para>
    /// This property enables optimistic concurrency control via HTTP conditional requests:
    /// <list type="bullet">
    /// <item>GET responses include the <c>ETag</c> header (derived from this property)</item>
    /// <item>PUT/PATCH/DELETE requests include <c>If-Match</c> header with the expected ETag</item>
    /// <item>If the ETag doesn't match, the server returns 412 Precondition Failed</item>
    /// </list>
    /// </para>
    /// <para>
    /// At the persistence layer, EF Core uses this as a concurrency token. The generated SQL
    /// includes <c>WHERE ETag = @originalETag</c>. If another process modified the aggregate,
    /// the update affects zero rows and EF Core throws <c>DbUpdateConcurrencyException</c>,
    /// which Trellis maps to <see cref="Error.Conflict"/> (HTTP 409).
    /// </para>
    /// <para>
    /// The ETag is managed by <c>AggregateETagConvention</c> (marks it as a concurrency token)
    /// and <c>AggregateETagInterceptor</c> (generates a new value on save). Domain code should
    /// not modify this property directly; non-EF persistence adapters stamp it through
    /// <see cref="IETagStampable.StampETag(string)"/>.
    /// </para>
    /// </remarks>
    public string ETag { get; private set; } = string.Empty;

    /// <inheritdoc />
    void IETagStampable.StampETag(string etag) => ETag = ValidateETag(etag);

    private static string ValidateETag(string etag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);
        foreach (var c in etag)
        {
            // RFC 9110 §8.8.1: an unquoted opaque tag may contain only etagc = %x21 / %x23-7E / obs-text
            // (0x80-FF). Mirrors EntityTagValue's validation so a stamped token is always HTTP ETag-safe.
            if (c is < '\x21' or '"' or '\x7F' or > '\xFF')
                throw new ArgumentException(
                    $"Invalid character in ETag: U+{(int)c:X4}. Must be an unquoted RFC 9110 opaque tag " +
                    "(e.g. Guid.NewGuid().ToString(\"N\")); normalize quoted store-native tokens before stamping.",
                    nameof(etag));
        }

        return etag;
    }

    /// <inheritdoc />
    void IReconstitutionStampable.StampReconstitutedState(DateTimeOffset createdAt, DateTimeOffset lastModified, string etag)
    {
        ETag = ValidateETag(etag);
        CreatedAt = createdAt;
        LastModified = lastModified;
        AcceptChanges();
    }

    /// <summary>
    /// Gets a value indicating whether the aggregate has uncommitted changes.
    /// </summary>
    /// <value>
    /// <c>true</c> if there are uncommitted domain events; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// <para>
    /// This property implements <see cref="System.ComponentModel.IChangeTracking.IsChanged"/>.
    /// The default implementation returns true if there are any uncommitted domain events.
    /// </para>
    /// <para>
    /// Override this property if your aggregate needs custom change tracking logic
    /// (e.g., tracking property changes independently of domain events).
    /// </para>
    /// <para>
    /// The <c>[JsonIgnore]</c> attribute prevents this property from being serialized,
    /// as it represents transient state that shouldn't be persisted.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public virtual bool IsChanged => DomainEvents.Count > 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Aggregate{TId}"/> class with the specified identifier.
    /// </summary>
    /// <param name="id">The unique identifier for this aggregate root. Must not be null or default.</param>
    /// <remarks>
    /// This constructor should be called by derived classes to set the aggregate's identity.
    /// Aggregates typically have a private constructor and a static factory method (e.g., Create)
    /// that performs validation and returns a Result.
    /// </remarks>
    protected Aggregate(TId id) : base(id)
    {
    }

    /// <summary>
    /// Gets all domain events that have been raised since the last call to <see cref="AcceptChanges"/>.
    /// </summary>
    /// <returns>
    /// A read-only list of uncommitted domain events. Returns an empty list if no events have been raised.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is typically called by the repository or unit of work pattern after successfully
    /// persisting the aggregate to retrieve events for publishing.
    /// </para>
    /// <para>
    /// Typical workflow:
    /// <code>
    /// // 1. Execute domain operation
    /// var result = order.Submit();
    /// 
    /// // 2. Save to database
    /// await repository.SaveAsync(order);
    /// 
    /// // 3. Publish events
    /// foreach (var evt in order.UncommittedEvents())
    /// {
    ///     await eventBus.PublishAsync(evt);
    /// }
    /// 
    /// // 4. Clear events
    /// order.AcceptChanges();
    /// </code>
    /// </para>
    /// </remarks>
    public IReadOnlyList<IDomainEvent> UncommittedEvents()
        => Array.AsReadOnly([.. DomainEvents]);

    /// <summary>
    /// Marks all changes as committed and clears the list of uncommitted domain events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method implements <see cref="System.ComponentModel.IChangeTracking.AcceptChanges"/>.
    /// It should be called after successfully persisting the aggregate and publishing its domain events.
    /// </para>
    /// <para>
    /// The method:
    /// <list type="bullet">
    /// <item>Clears the <see cref="DomainEvents"/> list</item>
    /// <item>Causes <see cref="IsChanged"/> to return <c>false</c></item>
    /// <item>Prepares the aggregate for the next set of changes</item>
    /// </list>
    /// </para>
    /// <para>
    /// Important: Only call this method after events have been successfully published.
    /// If publishing fails, events should remain uncommitted so they can be retried.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// // In a repository or unit of work
    /// public async Task<Result> SaveAsync(Order order, CancellationToken ct)
    /// {
    ///     using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
    ///     try
    ///     {
    ///         // 1. Save aggregate
    ///         _dbContext.Orders.Update(order);
    ///         await _dbContext.SaveChangesAsync(ct);
    ///         
    ///         // 2. Publish events
    ///         foreach (var evt in order.UncommittedEvents())
    ///         {
    ///             await _eventBus.PublishAsync(evt, ct);
    ///         }
    ///         
    ///         // 3. Only after successful publish
    ///         order.AcceptChanges();
    ///         
    ///         await transaction.CommitAsync(ct);
    ///         return Result.Ok();
    ///     }
    ///     catch (Exception ex)
    ///     {
    ///         await transaction.RollbackAsync(ct);
    ///         return new Error.Unexpected("transaction_failed") { Detail = "Transaction failed. See logs for details." };
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    public void AcceptChanges()
        => DomainEvents.Clear();
}
