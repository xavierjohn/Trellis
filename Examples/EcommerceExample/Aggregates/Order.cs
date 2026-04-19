using Trellis.Primitives;

namespace EcommerceExample.Aggregates;

using EcommerceExample.Entities;
using EcommerceExample.Events;
using EcommerceExample.ValueObjects;
using Trellis;

/// <summary>
/// Represents the status of an order in the system.
/// </summary>
public enum OrderStatus
{
    Draft,
    Pending,
    PaymentProcessing,
    PaymentFailed,
    Confirmed,
    Shipped,
    Delivered,
    Cancelled
}

/// <summary>
/// Order aggregate root managing the complete order lifecycle with domain events.
/// </summary>
public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    public CustomerId CustomerId { get; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();
    public Money Total { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public string? PaymentTransactionId { get; private set; }

    private Order(CustomerId customerId) : base(OrderId.NewUniqueV4())
    {
        CustomerId = customerId;
        Total = Money.Create(0, "USD");
        Status = OrderStatus.Draft;

        // Raise domain event for order creation
        DomainEvents.Add(new OrderCreated(Id, customerId, DateTime.UtcNow));
    }

    public static Result<Order> TryCreate(CustomerId customerId)
    {
        if (customerId is null)
            return Result.Fail<EcommerceExample.Aggregates.Order>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(customerId)), "validation.error") { Detail = "Customer ID is required" })));

        return Result.Ok(new Order(customerId));
    }

    public static Order Create(CustomerId customerId) =>
        TryCreate(customerId).Match(
            onSuccess: o => o,
            onFailure: error => throw new InvalidOperationException($"Failed to create Order: {error.GetDisplayMessage()}"));

    /// <summary>
    /// Adds a product to the order with Railway Oriented Programming pattern.
    /// </summary>
    public Result<Order> AddLine(ProductId productId, string productName, Money unitPrice, int quantity)
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft,
                new Error.Conflict(null, "domain.violation") { Detail = $"Cannot add items to order in {Status} status" })
            .Bind(_ => OrderLine.TryCreate(productId, productName, unitPrice, quantity))
            .Tap(line =>
            {
                var existingLine = _lines.FirstOrDefault(l => l.Id.Equals(productId));
                if (existingLine != null)
                {
                    _lines.Remove(existingLine);
                    line = existingLine.UpdateQuantity(existingLine.Quantity + quantity).Match(
                        onSuccess: l => l,
                        onFailure: e => throw new InvalidOperationException($"Failed to update line: {e.Detail}"));
                }

                _lines.Add(line);

                // Raise domain event
                DomainEvents.Add(new OrderLineAdded(
                    Id,
                    productId,
                    productName,
                    unitPrice,
                    quantity,
                    line.LineTotal,
                    DateTime.UtcNow));
            })
            .Bind(_ => RecalculateTotal())
            .Map(() => this);
    }

    /// <summary>
    /// Removes a product line from the order.
    /// </summary>
    public Result<Order> RemoveLine(ProductId productId)
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft,
                new Error.Conflict(null, "domain.violation") { Detail = $"Cannot remove items from order in {Status} status" })
            .Ensure(_ => _lines.Any(l => l.Id.Equals(productId)),
                new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Product not found in order" })
            .Tap(_ =>
            {
                _lines.RemoveAll(l => l.Id.Equals(productId));

                // Raise domain event
                DomainEvents.Add(new OrderLineRemoved(Id, productId, DateTime.UtcNow));
            })
            .Bind(_ => RecalculateTotal())
            .Map(() => this);
    }

    /// <summary>
    /// Submits the order for processing with validation.
    /// </summary>
    public Result<Order> Submit()
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft,
                new Error.Conflict(null, "conflict") { Detail = $"Cannot submit order in {Status} status" })
            .Ensure(_ => _lines.Count > 0,
                new Error.Conflict(null, "domain.violation") { Detail = "Cannot submit empty order" })
            .Ensure(_ => Total.Amount > 0,
                new Error.Conflict(null, "domain.violation") { Detail = "Order total must be greater than zero" })
            .Tap(_ =>
            {
                Status = OrderStatus.Pending;

                // Raise domain event
                DomainEvents.Add(new OrderSubmitted(
                    Id,
                    CustomerId,
                    Total,
                    _lines.Count,
                    DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    /// <summary>
    /// Processes payment with error handling and compensation.
    /// </summary>
    public Result<Order> ProcessPayment(string transactionId)
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Pending,
                new Error.Conflict(null, "conflict") { Detail = $"Cannot process payment for order in {Status} status" })
            .Ensure(_ => !string.IsNullOrWhiteSpace(transactionId),
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(transactionId)), "validation.error") { Detail = "Transaction ID is required" })))
            .Tap(_ =>
            {
                Status = OrderStatus.PaymentProcessing;
                PaymentTransactionId = transactionId;

                // Raise domain event
                DomainEvents.Add(new PaymentProcessingStarted(Id, transactionId, DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    /// <summary>
    /// Confirms the order after successful payment.
    /// </summary>
    public Result<Order> Confirm()
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.PaymentProcessing,
                new Error.Conflict(null, "conflict") { Detail = $"Cannot confirm order in {Status} status" })
            .Ensure(_ => !string.IsNullOrWhiteSpace(PaymentTransactionId),
                new Error.Conflict(null, "domain.violation") { Detail = "Payment transaction ID is missing" })
            .Tap(_ =>
            {
                Status = OrderStatus.Confirmed;
                ConfirmedAt = DateTime.UtcNow;

                // Raise domain event
                DomainEvents.Add(new OrderConfirmed(
                    Id,
                    CustomerId,
                    Total,
                    PaymentTransactionId!,
                    DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    /// <summary>
    /// Marks payment as failed and allows retry or cancellation.
    /// </summary>
    public Result<Order> MarkPaymentFailed()
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.PaymentProcessing,
                new Error.Conflict(null, "conflict") { Detail = $"Cannot mark payment failed for order in {Status} status" })
            .Tap(_ =>
            {
                Status = OrderStatus.PaymentFailed;
                PaymentTransactionId = null;

                // Raise domain event
                DomainEvents.Add(new PaymentFailed(Id, DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    /// <summary>
    /// Cancels the order with validation.
    /// </summary>
    public Result<Order> Cancel(string reason)
    {
        return this.ToResult()
            .Ensure(_ => Status is OrderStatus.Draft or OrderStatus.Pending or OrderStatus.PaymentFailed,
                new Error.Conflict(null, "domain.violation") { Detail = $"Cannot cancel order in {Status} status" })
            .Ensure(_ => !string.IsNullOrWhiteSpace(reason),
                new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(reason)), "validation.error") { Detail = "Cancellation reason is required" })))
            .Tap(_ =>
            {
                var previousStatus = Status;
                Status = OrderStatus.Cancelled;

                // Raise domain event
                DomainEvents.Add(new OrderCancelled(Id, reason, previousStatus, DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    /// <summary>
    /// Marks the order as shipped.
    /// </summary>
    public Result<Order> MarkAsShipped()
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Confirmed,
                new Error.Conflict(null, "conflict") { Detail = $"Cannot ship order in {Status} status" })
            .Tap(_ =>
            {
                Status = OrderStatus.Shipped;

                // Raise domain event
                DomainEvents.Add(new OrderShipped(Id, DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    /// <summary>
    /// Marks the order as delivered.
    /// </summary>
    public Result<Order> MarkAsDelivered()
    {
        return this.ToResult()
            .Ensure(_ => Status == OrderStatus.Shipped,
                new Error.Conflict(null, "conflict") { Detail = $"Cannot mark order as delivered in {Status} status" })
            .Tap(_ =>
            {
                Status = OrderStatus.Delivered;

                // Raise domain event
                DomainEvents.Add(new OrderDelivered(Id, DateTime.UtcNow));
            })
            .Map(_ => this);
    }

    private Result RecalculateTotal()
    {
        var total = Money.Create(0, "USD");

        foreach (var line in _lines)
        {
            var addResult = total.Add(line.LineTotal);
            if (addResult.TryGetError(out var addError))
                return Result.Fail(addError);

            // Safe: TryGetError returned false above, so addResult is success.
            if (!addResult.TryGetValue(out var newTotal))
                throw new InvalidOperationException("Result is in an unexpected state.");
            total = newTotal;
        }

        Total = total;
        return Result.Ok();
    }
}
