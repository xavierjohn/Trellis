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
/// // Define domain events as immutable records with OccurredAt as the timestamp.
/// // OccurredAt is DateTimeOffset so the authored instant is preserved unambiguously
/// // through serialization (e.g., outbox tables, integration buses, audit projections).
/// public record OrderCreated(OrderId OrderId, CustomerId CustomerId, DateTimeOffset OccurredAt) : IDomainEvent;
/// public record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset OccurredAt) : IDomainEvent;
/// 
/// // Raise events from an aggregate. Use TimeProvider.GetUtcNow() (typically injected)
/// // so tests can pin time deterministically with a fake clock.
/// public class Order : Aggregate&lt;OrderId&gt;
/// {
///     private readonly TimeProvider _clock;
/// 
///     private Order(OrderId id, CustomerId customerId, TimeProvider clock) : base(id)
///     {
///         _clock = clock;
///         CustomerId = customerId;
///         DomainEvents.Add(new OrderCreated(id, customerId, _clock.GetUtcNow()));
///     }
///     
///     public Result&lt;Order&gt; Submit()
///     {
///         return this.ToResult()
///             .Ensure(_ => Status == OrderStatus.Draft, Error.UnprocessableContent.ForRule("invalid", "Wrong status"))
///             .Tap(_ =>
///             {
///                 Status = OrderStatus.Submitted;
///                 DomainEvents.Add(new OrderSubmitted(Id, Total, _clock.GetUtcNow()));
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
    /// Gets the timestamp when this domain event occurred.
    /// </summary>
    /// <value>
    /// The instant in time when the event was raised, as a <see cref="DateTimeOffset"/> with explicit UTC offset.
    /// </value>
    /// <remarks>
    /// <para>
    /// This timestamp represents when the business action occurred, not when the event was persisted or published.
    /// Author events using <see cref="TimeProvider.GetUtcNow"/> (typically injected) so the canonical UTC offset
    /// (<c>+00:00</c>) is recorded and tests can pin time deterministically with a fake clock.
    /// </para>
    /// <para>
    /// <see cref="DateTimeOffset"/> is preferred over <see cref="DateTime"/> because the offset is an explicit
    /// part of the value and round-trips unambiguously through serialization. Events stored in outbox tables,
    /// integration buses, and audit projections retain their authored instant without timezone-loss bugs.
    /// </para>
    /// <para>
    /// <strong>Caveat:</strong> C# implicitly converts <see cref="DateTime"/> to <see cref="DateTimeOffset"/>,
    /// so a caller that passes <c>DateTime.Now</c> (Local) or <c>new DateTime(...)</c> (Unspecified, treated as Local)
    /// will silently produce an event timestamped with the machine's local offset rather than UTC. The contract
    /// does not block this at the type level. Always author events from <see cref="TimeProvider.GetUtcNow"/> or
    /// <see cref="DateTimeOffset.UtcNow"/> to avoid local-time stamping on non-UTC machines. A static analyzer to
    /// flag <see cref="DateTime"/> sources flowing into <c>OccurredAt</c> may follow in a future release.
    /// </para>
    /// <para>
    /// Use <c>OccurredAt</c> as the single timestamp for your events - avoid adding redundant fields like 
    /// <c>CreatedAt</c>, <c>SubmittedAt</c>, etc. that duplicate this information:
    /// <code>
    /// // Good - OccurredAt captures when the event happened
    /// public record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset OccurredAt) : IDomainEvent;
    /// 
    /// // Avoid - redundant SubmittedAt duplicates OccurredAt
    /// public record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset SubmittedAt, DateTimeOffset OccurredAt) : IDomainEvent;
    /// </code>
    /// </para>
    /// </remarks>
    DateTimeOffset OccurredAt { get; }
}