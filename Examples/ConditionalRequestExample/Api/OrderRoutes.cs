using Trellis;
using Trellis.Asp;

namespace ConditionalRequestExample.Api;

public static class OrderRoutes
{
    public static void MapOrderRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/orders");

        group.MapGet("/{id:guid}", (Guid id) =>
        {
            var order = OrderResponse.For(id);

            return Result.Ok(order).ToHttpResponse(opts => opts
                .WithETag(o => o.ETag)
                .WithLastModified(o => o.LastModified)
                .EvaluatePreconditions());
        });
    }
}

public sealed record OrderResponse(Guid Id, string Number, decimal Total, string ETag, DateTimeOffset LastModified)
{
    private static readonly DateTimeOffset RepresentationLastModified =
        new(2025, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static OrderResponse For(Guid id) =>
        new(id, "ORD-1001", 125.50m, $"order-{id:N}-v1", RepresentationLastModified);
}