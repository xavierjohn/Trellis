using Trellis.Primitives;

namespace EcommerceExample.Workflows;

using EcommerceExample.Aggregates;
using EcommerceExample.Entities;
using EcommerceExample.Events;
using EcommerceExample.Services;
using EcommerceExample.ValueObjects;
using Trellis;

/// <summary>
/// Demonstrates a complete order processing workflow using Railway Oriented Programming.
/// This showcases how complex business workflows can be composed using Result, Bind, Ensure, Tap, and RecoverOnFailure.
/// Also demonstrates domain event publishing with UncommittedEvents and AcceptChanges.
/// </summary>
public class OrderWorkflow
{
    private readonly PaymentService _paymentService;
    private readonly InventoryService _inventoryService;
    private readonly NotificationService _notificationService;

    public OrderWorkflow(
        PaymentService paymentService,
        InventoryService inventoryService,
        NotificationService notificationService)
    {
        _paymentService = paymentService;
        _inventoryService = inventoryService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Complete order processing workflow with error handling, recoverys, and domain event publishing.
    /// Demonstrates: Bind, Ensure, Tap, BindAsync, TapAsync, RecoverOnFailureAsync, UncommittedEvents, AcceptChanges
    /// </summary>
    public async Task<Result<Order>> ProcessOrderAsync(
        CustomerId customerId,
        List<OrderLineRequest> items,
        PaymentInfo paymentInfo,
        CancellationToken cancellationToken = default)
    {
        Order? currentOrder = null;
        var ct = cancellationToken;

        return await Order.TryCreate(customerId)
            .TapAsync(order =>
            {
                currentOrder = order;
                return _notificationService.SendOrderCreatedEmailAsync(customerId, order.Id, ct);
            })
            .BindAsync(order => AddItemsToOrderAsync(order, items, ct))
            .BindAsync(async order =>
            {
                var reserveResult = await ReserveInventoryAsync(order, ct)
                    .RecoverOnFailureAsync(
                        predicate: error => error is ValidationError,
                        funcAsync: () => SuggestAlternativeProductsAsync(order, ct));

                if (reserveResult.TryGetError(out var reserveError))
                {
                    order.Cancel("Inventory unavailable");
                    // Don't publish events for cancelled orders due to inventory issues
                    return reserveError;
                }

                return Result.Ok(order);
            })
            .BindAsync(async order =>
            {
                var submitResult = order.Submit();
                if (submitResult.IsFailure && currentOrder != null)
                {
                    await ReleaseInventoryAsync(currentOrder, ct);
                }

                return submitResult;
            })
            .BindAsync(async order =>
            {
                var paymentResult = await ProcessPaymentWithrecoveryAsync(order, paymentInfo, ct);

                if (paymentResult.TryGetError(out var paymentError))
                {
                    await ReleaseInventoryAsync(order, ct);
                    order.Cancel("Payment failed");
                    // Publish events including cancellation
                    await PublishEventsAndAcceptChangesAsync(order, ct);
                    await _notificationService.SendPaymentFailedEmailAsync(customerId, order.Id, ct);
                    return paymentError;
                }

                return Result.Ok(order);
            })
            .BindAsync(order => Task.FromResult(order.Confirm()))
            .TapAsync(async order =>
            {
                // Publish all domain events after successful confirmation
                await PublishEventsAndAcceptChangesAsync(order, ct);
                await _notificationService.SendOrderConfirmedEmailAsync(customerId, order.Id, ct);
            });
    }

    /// <summary>
    /// Demonstrates parallel validation of multiple order lines using TraverseAsync.
    /// Note: For validating collections, TraverseAsync is more appropriate than ParallelAsync.
    /// ParallelAsync is best for 2-9 independent operations that need to run concurrently.
    /// </summary>
    private async Task<Result<Order>> AddItemsToOrderAsync(
        Order order,
        List<OrderLineRequest> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return Error.Validation("Order must contain at least one item");

        // Validate all items - errors are automatically aggregated
        var validationResult = await items.TraverseAsync(
            (item, ct) => Task.FromResult(_inventoryService.CheckAvailability(item.ProductId, item.Quantity)),
            cancellationToken
        );

        // If any validation failed, return aggregated errors
        if (validationResult.TryGetError(out var validationError))
            return validationError;

        // Add all items to order
        var currentOrder = Result.Ok(order);
        foreach (var item in items)
        {
            currentOrder = currentOrder.Bind(o =>
                o.AddLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity)
            );

            if (currentOrder.IsFailure)
                return currentOrder;
        }

        return currentOrder;
    }

    /// <summary>
    /// Reserves inventory for all order lines.
    /// </summary>
    private async Task<Result<Unit>> ReserveInventoryAsync(Order order, CancellationToken cancellationToken)
    {
        foreach (var line in order.Lines)
        {
            var reserveResult = await _inventoryService.ReserveStockAsync(
                line.Id,
                line.Quantity,
                cancellationToken
            );

            if (reserveResult.IsFailure)
            {
                // Release any previously reserved items
                await ReleaseInventoryAsync(order, cancellationToken);
                return reserveResult;
            }
        }

        return Result.Ok();
    }

    /// <summary>
    /// Releases all reserved inventory for an order.
    /// </summary>
    private async Task ReleaseInventoryAsync(Order order, CancellationToken cancellationToken)
    {
        foreach (var line in order.Lines)
        {
            await _inventoryService.ReleaseStockAsync(line.Id, line.Quantity, cancellationToken);
        }
    }

    /// <summary>
    /// Processes payment with automatic retry on transient failures.
    /// Demonstrates: RecoverOnFailure with predicate for conditional error recovery.
    /// </summary>
    private async Task<Result<string>> ProcessPaymentWithrecoveryAsync(
        Order order,
        PaymentInfo paymentInfo,
        CancellationToken cancellationToken)
    {
        return await Task.FromResult(order.ProcessPayment("PENDING"))
            .BindAsync(_ => _paymentService.ProcessPaymentAsync(order, paymentInfo.CardNumber, paymentInfo.CVV, cancellationToken))
            .RecoverOnFailureAsync(
                predicate: error => error is UnexpectedError, // Retry on unexpected errors (e.g., timeouts)
                funcAsync: async () =>
                {
                    Console.WriteLine("Payment gateway timeout, retrying...");
                    await Task.Delay(1000, cancellationToken);
                    return await _paymentService.ProcessPaymentAsync(order, paymentInfo.CardNumber, paymentInfo.CVV, cancellationToken);
                })
            .TapOnFailureAsync(async error =>
            {
                await Task.FromResult(order.MarkPaymentFailed());
                Console.WriteLine($"Payment failed: {error.Detail}");
            });
    }

    /// <summary>
    /// Suggests alternative products when requested items are out of stock.
    /// Demonstrates: RecoverOnFailure for providing alternative paths.
    /// </summary>
    private async Task<Result<Unit>> SuggestAlternativeProductsAsync(Order order, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        Console.WriteLine($"Inventory unavailable for order {order.Id}. Suggesting alternatives...");

        // In a real system, this would query for similar products
        // For now, just return the original error
        return Error.Domain("Products out of stock. Please check alternative products.");
    }

    /// <summary>
    /// Demonstrates the repository pattern with domain event publishing.
    /// This simulates what a real repository would do after saving an aggregate.
    /// </summary>
    private static async Task PublishEventsAndAcceptChangesAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        // 1. Get uncommitted events before accepting changes
        var events = order.UncommittedEvents();

        if (events.Count == 0)
            return;

        // 2. Simulate persisting the aggregate (in real code, this would save to database)
        await Task.Delay(20, cancellationToken);

        // 3. Publish each domain event
        foreach (var domainEvent in events)
        {
            await PublishEventAsync(domainEvent, cancellationToken);
        }

        // 4. Accept changes - clears the uncommitted events list
        order.AcceptChanges();

        Console.WriteLine($"📢 Published {events.Count} domain event(s) for order {order.Id}");
    }

    private static async Task PublishEventAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);

        // Log the event type and key information
        var eventInfo = domainEvent switch
        {
            OrderCreated e => $"OrderCreated: {e.OrderId} for customer {e.CustomerId}",
            OrderLineAdded e => $"OrderLineAdded: {e.ProductName} x{e.Quantity} = {e.LineTotal}",
            OrderLineRemoved e => $"OrderLineRemoved: Product {e.ProductId}",
            OrderSubmitted e => $"OrderSubmitted: {e.OrderId}, Total: {e.Total}, Lines: {e.LineCount}",
            PaymentProcessingStarted e => $"PaymentProcessingStarted: TxnId: {e.TransactionId}",
            PaymentFailed e => $"PaymentFailed: Order {e.OrderId}",
            OrderConfirmed e => $"OrderConfirmed: {e.OrderId}, TxnId: {e.PaymentTransactionId}",
            OrderCancelled e => $"OrderCancelled: {e.OrderId}, Reason: {e.Reason}",
            OrderShipped e => $"OrderShipped: {e.OrderId}",
            OrderDelivered e => $"OrderDelivered: {e.OrderId}",
            _ => domainEvent.GetType().Name
        };

        Console.WriteLine($"   📋 Event: {eventInfo}");
    }
}

/// <summary>
/// Request object for adding items to an order.
/// </summary>
public record OrderLineRequest(
    ProductId ProductId,
    string ProductName,
    Money UnitPrice,
    int Quantity
);

/// <summary>
/// Payment information for processing orders.
/// </summary>
public record PaymentInfo(
    string CardNumber,
    string CVV,
    string CardHolderName
);