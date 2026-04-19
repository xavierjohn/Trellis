namespace EcommerceExample.Services;

using EcommerceExample.ValueObjects;
using Trellis;

/// <summary>
/// Handles sending notifications to customers.
/// </summary>
public class NotificationService
{
    public async Task<Result> SendOrderCreatedEmailAsync(CustomerId customerId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        Console.WriteLine($"📧 Email sent to customer {customerId}: Order {orderId} created");
        return Result.Ok();
    }

    public async Task<Result> SendOrderConfirmedEmailAsync(CustomerId customerId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        Console.WriteLine($"📧 Email sent to customer {customerId}: Order {orderId} confirmed and will be shipped soon");
        return Result.Ok();
    }

    public async Task<Result> SendPaymentFailedEmailAsync(CustomerId customerId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        Console.WriteLine($"📧 Email sent to customer {customerId}: Payment failed for order {orderId}");
        return Result.Ok();
    }

    public async Task<Result> SendOrderShippedEmailAsync(CustomerId customerId, OrderId orderId, string trackingNumber, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken);
        Console.WriteLine($"📧 Email sent to customer {customerId}: Order {orderId} shipped with tracking number {trackingNumber}");
        return Result.Ok();
    }
}