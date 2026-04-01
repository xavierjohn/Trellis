namespace EfCoreExample.Entities;

using EfCoreExample.Enums;
using EfCoreExample.ValueObjects;
using Trellis;

/// <summary>
/// Order aggregate using RequiredEnum for state management.
/// Demonstrates how RequiredEnum encapsulates business rules within the state itself.
/// </summary>
public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    public CustomerId CustomerId { get; private set; } = null!;
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// The order state as a RequiredEnum with rich behavior.
    /// </summary>
    public OrderState State { get; private set; } = null!;

    public decimal Total => _lines.Sum(l => l.LineTotal);
    public DateTime? ConfirmedAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    // EF Core requires parameterless constructor
    private Order() : base(OrderId.NewUniqueV4()) { }

    private Order(CustomerId customerId) : base(OrderId.NewUniqueV4())
    {
        CustomerId = customerId;
        State = OrderState.Draft;
    }

    /// <summary>
    /// Creates a new order for a customer.
    /// </summary>
    public static Result<Order> TryCreate(CustomerId customerId) =>
        customerId.ToResult()
            .Ensure(id => id != null, Error.Validation("Customer ID is required", nameof(customerId)))
            .Map(_ => new Order(customerId));

    /// <summary>
    /// Adds a product to the order.
    /// Uses RequiredEnum behavior to check if order can be modified.
    /// </summary>
    public Result<Order> AddLine(Product product, int quantity) =>
        this.ToResult()
            // RequiredEnum provides the CanModify property
            .Ensure(_ => State.CanModify, Error.Validation($"Cannot modify order in '{State}' state"))
            .Ensure(_ => quantity > 0, Error.Validation("Quantity must be positive", nameof(quantity)))
            .Tap(_ => _lines.Add(new OrderLine(Id, product, quantity)));

    /// <summary>
    /// Confirms the order.
    /// Uses RequiredEnum's transition validation.
    /// </summary>
    public Result<Order> Confirm() =>
        this.ToResult()
            .Ensure(_ => _lines.Count > 0, Error.Validation("Order must have at least one item"))
            // RequiredEnum validates the transition
            .Bind(_ => State.TryTransitionTo(OrderState.Confirmed))
            .Tap(newState =>
            {
                State = newState;
                ConfirmedAt = DateTime.UtcNow;
            })
            .Map(_ => this);

    /// <summary>
    /// Ships the order.
    /// </summary>
    public Result<Order> Ship() =>
        this.ToResult()
            .Bind(_ => State.TryTransitionTo(OrderState.Shipped))
            .Tap(newState =>
            {
                State = newState;
                ShippedAt = DateTime.UtcNow;
            })
            .Map(_ => this);

    /// <summary>
    /// Marks the order as delivered.
    /// </summary>
    public Result<Order> Deliver() =>
        this.ToResult()
            .Bind(_ => State.TryTransitionTo(OrderState.Delivered))
            .Tap(newState =>
            {
                State = newState;
                DeliveredAt = DateTime.UtcNow;
            })
            .Map(_ => this);

    /// <summary>
    /// Cancels the order.
    /// Uses RequiredEnum behavior to check if order can be cancelled.
    /// </summary>
    public Result<Order> Cancel() =>
        this.ToResult()
            // RequiredEnum provides the CanCancel property
            .Ensure(_ => State.CanCancel, Error.Validation($"Cannot cancel order in '{State}' state"))
            .Bind(_ => State.TryTransitionTo(OrderState.Cancelled))
            .Tap(newState =>
            {
                State = newState;
                CancelledAt = DateTime.UtcNow;
            })
            .Map(_ => this);

    // For EF Core to populate the lines collection
    internal void SetLines(List<OrderLine> lines) => _lines.AddRange(lines);
}