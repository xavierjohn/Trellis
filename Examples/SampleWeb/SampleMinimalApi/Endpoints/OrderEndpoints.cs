namespace SampleMinimalApi.Endpoints;

using Microsoft.AspNetCore.Routing;
using SampleMinimalApi.Models;
using SampleMinimalApi.Persistence;
using SampleMinimalApi.Workflows;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Order endpoints. Every state transition goes through <see cref="OrderWorkflow"/>.
/// Endpoints never call <c>order.Confirm()</c>, <c>order.AddLine(..)</c>, etc. directly —
/// that would bypass the commit / payment / notification boundary (axiom A10).
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", (CreateOrderRequest dto, OrderWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.CreateAsync(dto.CustomerId, cancellationToken)
                .ToCreatedAtRouteHttpResultAsync(
                    "GetOrder",
                    order => new RouteValueDictionary { ["orderId"] = order.Id.Value },
                    OrderResponse.From))
            .WithScalarValueValidation()
            .Produces<OrderResponse>(StatusCodes.Status201Created);

        group.MapGet("/{orderId}", (OrderId orderId, IOrderRepository orders, CancellationToken cancellationToken) =>
            orders.GetAsync(orderId, cancellationToken)
                .ToHttpResultAsync(OrderResponse.From))
            .WithName("GetOrder")
            .Produces<OrderResponse>();

        group.MapPost("/{orderId}/lines", (OrderId orderId, AddLineDto dto, OrderWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.AddLineAsync(orderId, dto.ProductId, dto.Quantity, cancellationToken)
                .ToHttpResultAsync(OrderResponse.From))
            .WithScalarValueValidation()
            .Produces<OrderResponse>();

        group.MapPost("/{orderId}/confirm", (OrderId orderId, OrderWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.ConfirmAsync(orderId, cancellationToken)
                .ToHttpResultAsync(OrderResponse.From))
            .Produces<OrderResponse>();

        group.MapPost("/{orderId}/ship", (OrderId orderId, OrderWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.ShipAsync(orderId, cancellationToken)
                .ToHttpResultAsync(OrderResponse.From))
            .Produces<OrderResponse>();

        group.MapPost("/{orderId}/cancel", (OrderId orderId, OrderWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.CancelAsync(orderId, cancellationToken)
                .ToHttpResultAsync(OrderResponse.From))
            .Produces<OrderResponse>();

        return app;
    }
}
