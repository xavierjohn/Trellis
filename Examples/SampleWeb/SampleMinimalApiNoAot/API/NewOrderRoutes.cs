namespace SampleMinimalApiNoAot.API;

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

public static class NewOrderRoutes
{
    public static void UseNewOrderRoute(this WebApplication app)
    {
        var orderApi = app.MapGroup("/orders");

        // POST /orders — create order
        // Demonstrates: Combine, Bind, CheckAsync with EF Core
        orderApi.MapPost("/", async (CreateOrderRequest request, AppDbContext db, HttpContext httpContext) =>
        {
            var customerIdResult = CustomerId.TryCreate(request.CustomerId);
            if (customerIdResult.IsFailure)
                return customerIdResult.Error.ToHttpResult();

            var orderResult = Order.TryCreate(customerIdResult.Value);
            if (orderResult.IsFailure)
                return orderResult.Error.ToHttpResult();

            var order = orderResult.Value;
            foreach (var line in request.Lines)
            {
                var product = await db.Products.FindAsync(ProductId.Create(line.ProductId));
                if (product is null)
                    return Error.NotFound($"Product {line.ProductId} not found.", "productId").ToHttpResult();

                var addResult = order.AddLine(product, line.Quantity);
                if (addResult.IsFailure)
                    return addResult.Error.ToHttpResult();
            }

            db.Orders.Add(order);
            var saveResult = await db.SaveChangesResultUnitAsync();
            if (saveResult.IsFailure)
                return saveResult.Error.ToHttpResult();

            return Result.Success(order)
                .ToCreatedHttpResult(httpContext,
                    o => $"/orders/{o.Id.Value}",
                    o => o.ETag,
                    OrderResponse.From);
        });

        // GET /orders/{id} — conditional GET with ETag
        // Demonstrates: RepresentationMetadata, If-None-Match → 304
        orderApi.MapGet("/{id:guid}", (Guid id, AppDbContext db, HttpContext httpContext) =>
            db.Orders
                .Include(o => o.Lines)
                .FirstOrDefaultResultAsync(o => o.Id == OrderId.Create(id),
                    Error.NotFound("Order not found.", id.ToString()))
                .ToHttpResultAsync(httpContext, o => o.ETag, OrderResponse.From));

        // POST /orders/{id}/confirm — confirm with async ROP + auth
        // Demonstrates: Ensure (auth), BindAsync (fetch), Bind (confirm),
        //               BindAsync (payment), TapAsync (notify), CheckAsync (save)
        orderApi.MapPost("/{id:guid}/confirm", async (
            Guid id,
            IPaymentService paymentService,
            INotificationService notificationService,
            IActorProvider actorProvider,
            AppDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Step 1: Authorize
            var actor = await actorProvider.GetCurrentActorAsync(ct);
            var authResult = actor.ToResult()
                .Ensure(a => a.HasPermission("orders:write"),
                    Error.Forbidden("Permission 'orders:write' required."));
            if (authResult.IsFailure)
                return authResult.Error.ToHttpResult();

            // Step 2: Fetch order → Confirm → Save → Pay → Notify
            // Note: Save before external calls ensures DB consistency. If payment/notification
            // fails after save, the order is confirmed but the caller gets an error.
            // Production systems should use an outbox pattern for guaranteed delivery.
            var result = await db.Orders.Include(o => o.Lines)
                .FirstOrDefaultResultAsync(o => o.Id == OrderId.Create(id),
                    Error.NotFound("Order not found.", id.ToString()), ct)
                .BindAsync(order => order.Confirm())
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .BindAsync(order =>
                    paymentService.ProcessPaymentAsync(order.Id, order.Total, ct)
                        .MapAsync(_ => order))
                .TapAsync(order =>
                    notificationService.SendOrderConfirmationAsync(order.Id, order.CustomerId, ct));

            return result.ToHttpResult(httpContext, o => o.ETag, OrderResponse.From);
        });

        // POST /orders/{id}/cancel — cancel with RecoverOnFailureAsync
        // Demonstrates: RecoverOnFailureAsync for cleanup on unexpected errors
        orderApi.MapPost("/{id:guid}/cancel", async (
            Guid id,
            INotificationService notificationService,
            IActorProvider actorProvider,
            AppDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Step 1: Authorize
            var actor = await actorProvider.GetCurrentActorAsync(ct);
            var authResult = actor.ToResult()
                .Ensure(a => a.HasPermission("orders:write"),
                    Error.Forbidden("Permission 'orders:write' required."));
            if (authResult.IsFailure)
                return authResult.Error.ToHttpResult();

            // Step 2: Fetch → Cancel → Save → Notify
            // Note: Same save-first pattern as confirm. See confirm comment for tradeoff explanation.
            var result = await db.Orders.Include(o => o.Lines)
                .FirstOrDefaultResultAsync(o => o.Id == OrderId.Create(id),
                    Error.NotFound("Order not found.", id.ToString()), ct)
                .BindAsync(order => order.Cancel())
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .TapAsync(order =>
                    notificationService.SendOrderCancellationAsync(order.Id, order.CustomerId, ct))
                .RecoverOnFailureAsync(
                    error => error.Code == "unexpected",
                    _ => Task.FromResult(
                        Result.Failure<Order>(Error.Unexpected("Cancellation failed. Please try again."))));

            return result.ToHttpResult(httpContext, o => o.ETag, OrderResponse.From);
        });

        // POST /orders/{id}/receipt — redirect after POST
        // Demonstrates: RFC 9110 §15.4.4 — 303 See Other (redirect to GET after POST)
        orderApi.MapPost("/{id:guid}/receipt", (Guid id, HttpContext httpContext) =>
        {
            httpContext.Response.Headers.Location = $"/orders/{id}";
            return Results.StatusCode(303);
        });
    }
}
