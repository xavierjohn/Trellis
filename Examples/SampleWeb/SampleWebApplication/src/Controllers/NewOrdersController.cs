namespace SampleWebApplication.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SampleDataAccess;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;
using Trellis.Authorization;
using Trellis.EntityFrameworkCore;

public record CreateOrderRequest(CustomerId CustomerId, OrderLineRequest[] Lines);
public record OrderLineRequest(ProductId ProductId, int Quantity);

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
    // Demonstrates: Ensure, Bind, TraverseAsync, CheckAsync with EF Core
    // Note: A production system would also call Product.ReserveStock() to enforce inventory
    // limits and release stock on cancellation. Omitted here to keep the sample focused on ROP patterns.
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var result = await Result.Ensure(
                request.Lines is { Length: > 0 },
                Error.Validation("Order must have at least one line item.", "lines"))
            .Bind(_ => Order.TryCreate(request.CustomerId))
            .BindAsync(order =>
                request.Lines.TraverseAsync(line =>
                    db.Products
                        .FirstOrDefaultResultAsync(p => p.Id == line.ProductId,
                            Error.NotFound("Product not found.", line.ProductId))
                        .BindAsync(product => order.AddLine(product, line.Quantity)))
                .MapAsync(_ => order))
            .TapAsync(order => { db.Orders.Add(order); return Task.CompletedTask; })
            .CheckAsync(_ => db.SaveChangesResultUnitAsync());

        return result.Match<Order, ActionResult<OrderResponse>>(
            onSuccess: created =>
            {
                var response = OrderResponse.From(created);
                Response.Headers.ETag = $"\"{created.ETag}\"";
                return CreatedAtAction(nameof(GetOrder), new { id = created.Id.Value }, response);
            },
            onFailure: error => error.ToActionResult<OrderResponse>(this));
    }

    // GET /orders/{id} — conditional GET with ETag
    // Demonstrates: RepresentationMetadata, If-None-Match → 304, strongly-typed route binding
    [HttpGet("{id}", Name = nameof(GetOrder))]
    public async Task<ActionResult<OrderResponse>> GetOrder(OrderId id)
    {
        var result = await db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultResultAsync(o => o.Id == id,
                Error.NotFound("Order not found.", id));

        if (result.TryGetError(out var fetchError))
            return fetchError.ToActionResult<OrderResponse>(this);

        return result.ToActionResult(this,
            order => RepresentationMetadata.WithStrongETag(order.ETag),
            OrderResponse.From);
    }

    // POST /orders/{id}/confirm — confirm with async ROP + auth
    // Demonstrates: Ensure (auth), BindAsync (fetch), Bind (confirm),
    //               BindAsync (payment), CheckAsync (notify)
    [HttpPost("{id}/confirm")]
    public async Task<ActionResult<OrderResponse>> Confirm(OrderId id, CancellationToken ct)
    {
        // Step 1: Authorize
        var actor = await actorProvider.GetCurrentActorAsync(ct);
        var authResult = actor.ToResult()
            .Ensure(a => a.HasPermission("orders:write"),
                Error.Forbidden("Permission 'orders:write' required."));
        if (authResult.TryGetError(out var authError))
            return authError.ToActionResult<OrderResponse>(this);

        // Step 2: Fetch order → Confirm → Save → Pay → Notify
        // Note: Save before external calls ensures DB consistency. If payment/notification
        // fails after save, the order is confirmed but the caller gets an error.
        // Production systems should use an outbox pattern for guaranteed delivery.
        // The payment reference should also be stored on the order for refund support.
        var result = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultResultAsync(o => o.Id == id,
                Error.NotFound("Order not found.", id), ct)
            .BindAsync(order => order.Confirm())
            .CheckAsync(_ => db.SaveChangesResultUnitAsync(ct))
            .BindAsync(order =>
                paymentService.ProcessPaymentAsync(order.Id, order.Total, ct)
                    .MapAsync(_ => order))
            .CheckAsync(order =>
                notificationService.SendOrderConfirmationAsync(order.Id, order.CustomerId, ct));

        if (result.TryGetError(out var confirmError))
            return confirmError.ToActionResult<OrderResponse>(this);

        return result.ToActionResult(this,
            order => RepresentationMetadata.WithStrongETag(order.ETag),
            OrderResponse.From);
    }

    // POST /orders/{id}/cancel— cancel with RecoverOnFailureAsync
    // Demonstrates: RecoverOnFailureAsync for cleanup on unexpected errors
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(OrderId id, CancellationToken ct)
    {
        // Step 1: Authorize
        var actor = await actorProvider.GetCurrentActorAsync(ct);
        var authResult = actor.ToResult()
            .Ensure(a => a.HasPermission("orders:write"),
                Error.Forbidden("Permission 'orders:write' required."));
        if (authResult.TryGetError(out var authError))
            return authError.ToActionResult<OrderResponse>(this);

        // Step 2: Fetch → Cancel → Save → Notify
        // Note: Same save-first pattern as confirm. A production system would also call
        // RefundPaymentAsync here if the order was previously confirmed with payment.
        var result = await db.Orders.Include(o => o.Lines)
            .FirstOrDefaultResultAsync(o => o.Id == id,
                Error.NotFound("Order not found.", id), ct)
            .BindAsync(order => order.Cancel())
            .CheckAsync(_ => db.SaveChangesResultUnitAsync(ct))
            .CheckAsync(order =>
                notificationService.SendOrderCancellationAsync(order.Id, order.CustomerId, ct))
            .RecoverOnFailureAsync(
                error => error.Code == "unexpected.error",
                _ => Result.Fail<Order>(
                    Error.Unexpected("Cancellation failed. Please try again.")));

        if (result.TryGetError(out var cancelError))
            return cancelError.ToActionResult<OrderResponse>(this);

        return result.ToActionResult(this,
            order => RepresentationMetadata.WithStrongETag(order.ETag),
            OrderResponse.From);
    }

    // POST /orders/{id}/receipt— redirect after POST
    // Demonstrates: RFC 9110 §15.4.4 — 303 See Other (redirect to GET after POST)
    [HttpPost("{id}/receipt")]
    public ActionResult Receipt(OrderId id)
    {
        Response.Headers.Location = $"/orders/{id.Value}";
        return StatusCode(303);
    }
}