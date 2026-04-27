namespace Trellis;

/// <summary>
/// Represents a domain event - a record of something significant that happened in the business domain.
/// Domain events capture state changes and business occurrences that domain experts care about.
/// </summary>
/// <remarks>
/// <para>
/// Domain events are a key tactical pattern in Domain-Driven Design that enable:
/// <list type="bullet">
/// <item>Loose coupling between aggregates and bounded contexts</item>
/// <item>Temporal decoupling of side effects from core business logic</item>
/// <item>Event sourcing and audit trails</item>
/// <item>Integration between microservices</item>
/// <item>Communication of state changes to external systems</item>
/// </list>
/// </para>
/// <para>
/// Best practices for domain events:
/// <list type="bullet">
/// <item>Name events in the past tense (e.g., OrderSubmitted, PaymentProcessed)</item>
/// <item>Make events immutable - use readonly properties or init-only setters</item>
/// <item>Include all relevant data needed by handlers to avoid querying</item>
/// <item>Keep events focused on domain concepts, not technical implementation</item>
/// <item>Use <see cref="OccurredAt"/> for the event timestamp - avoid redundant timestamp fields</item>
/// </list>
/// </para>
/// <para>
/// Domain events vs. Integration events:
/// <list type="bullet">
/// <item><strong>Domain events</strong>: Internal to the bounded context, raised by aggregates</item>
/// <item><strong>Integration events</strong>: Published across bounded contexts, may be derived from domain events</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Define domain events as immutable records with OccurredAt as the timestamp
/// public record OrderCreated(OrderId OrderId, CustomerId CustomerId, DateTime OccurredAt) : IDomainEvent;
/// public record OrderSubmitted(OrderId OrderId, Money Total, DateTime OccurredAt) : IDomainEvent;
/// 
/// // Raise events from an aggregate
/// public class Order : Aggregate&lt;OrderId&gt;
/// {
///     private Order(OrderId id, CustomerId customerId) : base(id)
///     {
///         CustomerId = customerId;
///         DomainEvents.Add(new OrderCreated(id, customerId, DateTime.UtcNow));
///     }
///     
///     public Result&lt;Order&gt; Submit()
///     {
///         return this.ToResult()
///             .Ensure(_ => Status == OrderStatus.Draft, Error.UnprocessableContent.ForRule("invalid", "Wrong status"))
///             .Tap(_ =>
///             {
///                 Status = OrderStatus.Submitted;
///                 SubmittedAt = DateTime.UtcNow;
///                 DomainEvents.Add(new OrderSubmitted(Id, Total, DateTime.UtcNow));
///             });
///     }
/// }
/// 
/// // Handle the event
/// public class OrderSubmittedHandler
/// {
///     public async Task Handle(OrderSubmitted evt, CancellationToken ct)
///     {
///         _logger.LogInformation("Order {OrderId} submitted at {OccurredAt}", 
///             evt.OrderId, evt.OccurredAt);
///         await _emailService.SendOrderConfirmationAsync(evt.OrderId, ct);
///     }
/// }
/// </code>
/// </example>
public interface IDomainEvent
{
    /// <summary>
    /// Gets the UTC timestamp when this domain event occurred.
    /// </summary>
    /// <value>
    /// The date and time in UTC when the event was raised.
    /// </value>
    /// <remarks>
    /// <para>
    /// This timestamp represents when the business action occurred, not when the event was persisted or published.
    /// Always use UTC to ensure consistency across distributed systems and time zones.
    /// </para>
    /// <para>
    /// Use <c>OccurredAt</c> as the single timestamp for your events - avoid adding redundant fields like 
    /// <c>CreatedAt</c>, <c>SubmittedAt</c>, etc. that duplicate this information:
    /// <code>
    /// // Good - OccurredAt captures when the event happened
    /// public record OrderSubmitted(OrderId OrderId, Money Total, DateTime OccurredAt) : IDomainEvent;
    /// 
    /// // Avoid - redundant SubmittedAt duplicates OccurredAt
    /// public record OrderSubmitted(OrderId OrderId, Money Total, DateTime SubmittedAt, DateTime OccurredAt) : IDomainEvent;
    /// </code>
    /// </para>
    /// </remarks>
    DateTime OccurredAt { get; }
}