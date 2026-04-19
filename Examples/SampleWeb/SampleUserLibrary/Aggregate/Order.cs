namespace SampleUserLibrary;

using Trellis;

/// <summary>
/// Order aggregate with full lifecycle state transitions.
/// Demonstrates Ensure-based guards, state machine with RequiredEnum,
/// and rich domain behavior with ROP chains.
/// </summary>
public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    public CustomerId CustomerId { get; private set; } = null!;
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public OrderState State { get; private set; } = null!;
    public decimal Total => _lines.Sum(l => l.LineTotal);
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    // EF Core parameterless constructor
    private Order() : base(default!) { }

    private Order(CustomerId customerId) : base(OrderId.NewUniqueV7())
    {
        CustomerId = customerId;
        State = OrderState.Draft;
    }

    /// <summary>
    /// Creates a new draft order for a customer.
    /// </summary>
    public static Result<Order> TryCreate(CustomerId customerId) =>
        customerId.ToResult()
            .Ensure(id => id != null, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(customerId)), "validation.error") { Detail = "Customer ID is required" })))
            .Map(_ => new Order(customerId));

    /// <summary>
    /// Adds a product line to the order. Only allowed in Draft state.
    /// </summary>
    public Result<Order> AddLine(Product product, int quantity) =>
        this.ToResult()
            .Ensure(_ => State.CanModify, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Cannot modify order in '{State}' state" })
            .Ensure(_ => quantity > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(quantity)), "validation.error") { Detail = "Quantity must be positive" })))
            .Tap(_ => _lines.Add(new OrderLine(Id, product, quantity)));

    /// <summary>
    /// Confirms the order. Requires at least one line item.
    /// </summary>
    public Result<Order> Confirm() =>
        this.ToResult()
            .Ensure(_ => _lines.Count > 0, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Order must have at least one item" })
            .Bind(_ => State.TryTransitionTo(OrderState.Confirmed))
            .Tap(newState =>
            {
                State = newState;
                ConfirmedAt = DateTimeOffset.UtcNow;
            })
            .Map(_ => this);

    /// <summary>
    /// Ships the order. Must be confirmed first.
    /// </summary>
    public Result<Order> Ship() =>
        this.ToResult()
            .Bind(_ => State.TryTransitionTo(OrderState.Shipped))
            .Tap(newState =>
            {
                State = newState;
                ShippedAt = DateTimeOffset.UtcNow;
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
                DeliveredAt = DateTimeOffset.UtcNow;
            })
            .Map(_ => this);

    /// <summary>
    /// Cancels the order. Only possible before shipping.
    /// </summary>
    public Result<Order> Cancel() =>
        this.ToResult()
            .Ensure(_ => State.CanCancel, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Cannot cancel order in '{State}' state" })
            .Bind(_ => State.TryTransitionTo(OrderState.Cancelled))
            .Tap(newState =>
            {
                State = newState;
                CancelledAt = DateTimeOffset.UtcNow;
            })
            .Map(_ => this);

    // For EF Core to populate the lines collection
    internal void SetLines(List<OrderLine> lines) => _lines.AddRange(lines);
}