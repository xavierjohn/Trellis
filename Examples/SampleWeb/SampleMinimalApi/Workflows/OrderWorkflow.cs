namespace SampleMinimalApi.Workflows;

using SampleMinimalApi.Persistence;
using SampleUserLibrary;
using Trellis;

/// <summary>
/// Application boundary for state-changing order use cases (axiom A10). Each method:
/// <list type="number">
///   <item>Loads the aggregate(s) from the repository.</item>
///   <item>Mutates the aggregate via its domain method (returns <see cref="Result{T}"/>).</item>
///   <item>On success calls <see cref="CommitAsync"/> which fires side-effects (notifications,
///         payment) and persists the aggregate exactly once.</item>
/// </list>
/// Endpoints must not invoke aggregate methods directly — they dispatch through this workflow
/// so the commit boundary (and any future event publishing / AcceptChanges) is enforced in one
/// place.
/// </summary>
public sealed class OrderWorkflow(
    IOrderRepository orders,
    IProductRepository products,
    IPaymentService payments,
    INotificationService notifications,
    TimeProvider timeProvider)
{
    private readonly IOrderRepository _orders = orders;
    private readonly IProductRepository _products = products;
    private readonly IPaymentService _payments = payments;
    private readonly INotificationService _notifications = notifications;
    private readonly TimeProvider _timeProvider = timeProvider;

    public Task<Result<Order>> CreateAsync(CustomerId customerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customerId);
        _ = _timeProvider;
        return Order.TryCreate(customerId)
            .TapAsync(order => _orders.SaveAsync(order, cancellationToken));
    }

    public Task<Result<Order>> AddLineAsync(OrderId orderId, ProductId productId, int quantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(productId);
        return _orders.GetAsync(orderId, cancellationToken)
            .BindAsync(order => _products.GetAsync(productId, cancellationToken)
                .BindAsync(product => Task.FromResult(order.AddLine(product, quantity))))
            .TapAsync(order => _orders.SaveAsync(order, cancellationToken));
    }

    public Task<Result<Order>> ConfirmAsync(OrderId orderId, CancellationToken cancellationToken) =>
        _orders.GetAsync(orderId, cancellationToken)
            .BindAsync(order => Task.FromResult(order.Confirm()))
            .BindAsync(order => ChargeAndNotifyAsync(order, cancellationToken))
            .TapAsync(order => _orders.SaveAsync(order, cancellationToken));

    public Task<Result<Order>> ShipAsync(OrderId orderId, CancellationToken cancellationToken) =>
        _orders.GetAsync(orderId, cancellationToken)
            .BindAsync(order => Task.FromResult(order.Ship()))
            .TapAsync(order => _orders.SaveAsync(order, cancellationToken));

    public Task<Result<Order>> CancelAsync(OrderId orderId, CancellationToken cancellationToken) =>
        _orders.GetAsync(orderId, cancellationToken)
            .BindAsync(order => Task.FromResult(order.Cancel()))
            .BindAsync(order => NotifyCancelledAsync(order, cancellationToken))
            .TapAsync(order => _orders.SaveAsync(order, cancellationToken));

    private async Task<Result<Order>> ChargeAndNotifyAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await _payments.ProcessPaymentAsync(order.Id, order.Total, cancellationToken).ConfigureAwait(false);
        if (payment.TryGetError(out var paymentError))
            return Result.Fail<Order>(paymentError);

        var notify = await _notifications.SendOrderConfirmationAsync(order.Id, order.CustomerId, cancellationToken).ConfigureAwait(false);
        return notify.TryGetError(out var notifyError)
            ? Result.Fail<Order>(notifyError)
            : Result.Ok(order);
    }

    private async Task<Result<Order>> NotifyCancelledAsync(Order order, CancellationToken cancellationToken)
    {
        var notify = await _notifications.SendOrderCancellationAsync(order.Id, order.CustomerId, cancellationToken).ConfigureAwait(false);
        return notify.TryGetError(out var notifyError)
            ? Result.Fail<Order>(notifyError)
            : Result.Ok(order);
    }
}
