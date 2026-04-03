namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SampleDataAccess;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;

public record CreateOrderRequest(Guid CustomerId, OrderLineRequest[] Lines);
public record OrderLineRequest(Guid ProductId, int Quantity);

public record OrderResponse(Guid Id, Guid CustomerId, string State, decimal Total, string ETag,
    DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt, OrderLineResponse[] Lines)
{
    public static OrderResponse From(Order o) => new(
        o.Id.Value, o.CustomerId.Value, o.State.ToString()!, o.Total, o.ETag, o.CreatedAt,
        o.ConfirmedAt, [.. o.Lines.Select(l => new OrderLineResponse(l.ProductName.Value, l.UnitPrice.Value, l.Quantity, l.LineTotal))]);
}

public record OrderLineResponse(string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

[ApiController]
[Route("orders")]
public class NewOrdersController(
    AppDbContext db,
    IPaymentService paymentService,
    INotificationService notificationService,
    IActorProvider actorProvider) : ControllerBase
{
    // POST /orders — create order
    // Demonstrates: Combine, Bind, CheckAsync with EF Core
    // Note: A production system would also call Product.ReserveStock() to enforce inventory
    // limits and release stock on cancellation. Omitted here to keep the sample focused on ROP patterns.
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        if (request.Lines is null || request.Lines.Length == 0)
            return Error.Validation("Order must have at least one line item.", "lines").ToActionResult<OrderResponse>(this);

        var customerIdResult = CustomerId.TryCreate(request.CustomerId);
        if (customerIdResult.IsFailure)
            return customerIdResult.Error.ToActionResult<OrderResponse>(this);

        var orderResult = Order.TryCreate(customerIdResult.Value);
        if (orderResult.IsFailure)
            return orderResult.Error.ToActionResult<OrderResponse>(this);

        var order = orderResult.Value;
        foreach (var line in request.Lines)
        {
            var product = await db.Products.FindAsync(ProductId.Create(line.ProductId));
            if (product is null)
                return Error.NotFound($"Product {line.ProductId} not found.", "productId")
                    .ToActionResult<OrderResponse>(this);

            var addResult = order.AddLine(product, line.Quantity);
            if (addResult.IsFailure)
                return addResult.Error.ToActionResult<OrderResponse>(this);
        }

        db.Orders.Add(order);
        var saveResult = await db.SaveChangesResultUnitAsync();
        if (saveResult.IsFailure)
            return saveResult.Error.ToActionResult<OrderResponse>(this);

        var response = OrderResponse.From(order);
        Response.Headers.ETag = $"\"{order.ETag}\"";
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id.Value }, response);
    }

    // GET /orders/{id} — conditional GET with ETag
    // Demonstrates: RepresentationMetadata, If-None-Match → 304, strongly-typed route binding
    [HttpGet("{id}", Name = nameof(GetOrder))]
    public async Task<ActionResult<OrderResponse>> GetOrder(OrderId id)
    {
        var result = await db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultResultAsync(o => o.Id == id,
                Error.NotFound("Order not found.", id.ToString(CultureInfo.InvariantCulture)));

        if (result.IsFailure)
            return result.Error.ToActionResult<OrderResponse>(this);

        var order = result.Value;
        var metadata = RepresentationMetadata.WithStrongETag(order.ETag);
        return result.ToActionResult(this, metadata, OrderResponse.From);
    }

    // POST /orders/{id}/confirm — confirm with async ROP + auth
    // Demonstrates: Ensure (auth), BindAsync (fetch), Bind (confirm),
    //               BindAsync (payment), TapAsync (notify), CheckAsync (save)
    [HttpPost("{id}/confirm")]
    public async Task<ActionResult<OrderResponse>> Confirm(OrderId id, CancellationToken ct)
    {
        // Step 1: Authorize
        var actor = await actorProvider.GetCurrentActorAsync(ct);
        var authResult = actor.ToResult()
            .Ensure(a => a.HasPermission("orders:write"),
                Error.Forbidden("Permission 'orders:write' required."));
        if (authResult.IsFailure)
            return authResult.Error.ToActionResult<OrderResponse>(this);

        // Step 2: Fetch order → Confirm → Save → Pay → Notify
        // Note: Save before external calls ensures DB consistency. If payment/notification
        // fails after save, the order is confirmed but the caller gets an error.
        // Production systems should use an outbox pattern for guaranteed delivery.
        // The payment reference should also be stored on the order for refund support.
        var result = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultResultAsync(o => o.Id == id,
                Error.NotFound("Order not found.", id.ToString(CultureInfo.InvariantCulture)), ct)
            .BindAsync(order => order.Confirm())
            .CheckAsync(_ => db.SaveChangesResultUnitAsync())
            .BindAsync(order =>
                paymentService.ProcessPaymentAsync(order.Id, order.Total, ct)
                    .MapAsync(_ => order))
            .TapAsync(order =>
                notificationService.SendOrderConfirmationAsync(order.Id, order.CustomerId, ct));

        if (result.IsFailure)
            return result.Error.ToActionResult<OrderResponse>(this);

        var metadata = RepresentationMetadata.WithStrongETag(result.Value.ETag);
        return result.ToActionResult(this, metadata, OrderResponse.From);
    }

    // POST /orders/{id}/cancel — cancel with RecoverOnFailureAsync
    // Demonstrates: RecoverOnFailureAsync for cleanup on unexpected errors
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(OrderId id, CancellationToken ct)
    {
        // Step 1: Authorize
        var actor = await actorProvider.GetCurrentActorAsync(ct);
        var authResult = actor.ToResult()
            .Ensure(a => a.HasPermission("orders:write"),
                Error.Forbidden("Permission 'orders:write' required."));
        if (authResult.IsFailure)
            return authResult.Error.ToActionResult<OrderResponse>(this);

        // Step 2: Fetch → Cancel → Save → Notify
        // Note: Same save-first pattern as confirm. A production system would also call
        // RefundPaymentAsync here if the order was previously confirmed with payment.
        var result = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultResultAsync(o => o.Id == id,
                Error.NotFound("Order not found.", id.ToString(CultureInfo.InvariantCulture)), ct)
            .BindAsync(order => order.Cancel())
            .CheckAsync(_ => db.SaveChangesResultUnitAsync())
            .TapAsync(order =>
                notificationService.SendOrderCancellationAsync(order.Id, order.CustomerId, ct))
            .RecoverOnFailureAsync(
                error => error.Code == "unexpected",
                _ => Result.Failure<Order>(
                    Error.Unexpected("Cancellation failed. Please try again.")));

        if (result.IsFailure)
            return result.Error.ToActionResult<OrderResponse>(this);

        var metadata = RepresentationMetadata.WithStrongETag(result.Value.ETag);
        return result.ToActionResult(this, metadata, OrderResponse.From);
    }

    // POST /orders/{id}/receipt — redirect after POST
    // Demonstrates: RFC 9110 §15.4.4 — 303 See Other (redirect to GET after POST)
    [HttpPost("{id}/receipt")]
    public ActionResult Receipt(OrderId id)
    {
        Response.Headers.Location = $"/orders/{id.Value}";
        return StatusCode(303);
    }
}
