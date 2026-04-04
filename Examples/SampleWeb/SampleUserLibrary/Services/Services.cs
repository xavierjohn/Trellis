namespace SampleUserLibrary;

using Trellis;

/// <summary>
/// Payment service interface with async Result signatures.
/// Demonstrates how external service calls integrate with ROP.
/// </summary>
public interface IPaymentService
{
    Task<Result<string>> ProcessPaymentAsync(OrderId orderId, decimal amount, CancellationToken ct = default);
    Task<Result<Unit>> RefundPaymentAsync(string paymentReference, CancellationToken ct = default);
}

/// <summary>
/// Notification service interface with async Result signatures.
/// </summary>
public interface INotificationService
{
    Task<Result<Unit>> SendOrderConfirmationAsync(OrderId orderId, CustomerId customerId, CancellationToken ct = default);
    Task<Result<Unit>> SendOrderCancellationAsync(OrderId orderId, CustomerId customerId, CancellationToken ct = default);
}

/// <summary>
/// Fake payment service for demo purposes.
/// Simulates async processing with Task.Delay.
/// </summary>
public class FakePaymentService : IPaymentService
{
    public async Task<Result<string>> ProcessPaymentAsync(OrderId orderId, decimal amount, CancellationToken ct = default)
    {
        await Task.Delay(50, ct);
        var paymentRef = $"PAY-{Guid.NewGuid():N}"[..16];
        return paymentRef;
    }

    public async Task<Result<Unit>> RefundPaymentAsync(string paymentReference, CancellationToken ct = default)
    {
        await Task.Delay(30, ct);
        return new Unit();
    }
}

/// <summary>
/// Fake notification service for demo purposes.
/// Simulates async sending with Task.Delay.
/// </summary>
public class FakeNotificationService : INotificationService
{
    public async Task<Result<Unit>> SendOrderConfirmationAsync(OrderId orderId, CustomerId customerId, CancellationToken ct = default)
    {
        await Task.Delay(20, ct);
        return new Unit();
    }

    public async Task<Result<Unit>> SendOrderCancellationAsync(OrderId orderId, CustomerId customerId, CancellationToken ct = default)
    {
        await Task.Delay(20, ct);
        return new Unit();
    }
}
