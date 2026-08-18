// Cookbook Recipe 23 — Concurrency control on aggregate-mutating endpoints: when to require If-Match.
//
// State-transition POST: no If-Match — the state machine guards the transition.
// Full-update PUT: RequireETag at the read-modify-write boundary.
namespace CookbookSnippets.Recipe23;

using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using global::Mediator;
using Trellis;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id) : base(id) { }

    public string CustomerReference { get; private set; } = string.Empty;

    public Result<Order> Replace(ReplaceOrderRequest request) =>
        Result.Ensure(
                !string.IsNullOrWhiteSpace(request?.CustomerReference),
                Error.InvalidInput.ForField("customerReference", "required"))
            .Tap(() => CustomerReference = request!.CustomerReference)
            .Map(_ => this);
}

public sealed record ReplaceOrderRequest(string CustomerReference);

public sealed record OrderResponse(Guid Id, string CustomerReference)
{
    public static OrderResponse From(Order order) =>
        new(order.Id.Value, order.CustomerReference);
}

public sealed record ApproveOrderCommand(OrderId Id) : ICommand<Result<Order>>;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Order>().HasKey(o => o.Id);
    }
}

public static class ConcurrencyEndpoints
{
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // State-transition POST — no If-Match. The state machine guards the transition.
        app.MapPost("/orders/{id:guid}/approve", (OrderId id, ISender sender, CancellationToken ct) =>
            sender.Send(new ApproveOrderCommand(id), ct)
                .ToHttpResponseAsync(OrderResponse.From));

        // Full-update PUT — RequireETag at the read-modify-write boundary.
        // Missing If-Match → 428; stale → 412; current → proceeds.
        app.MapPut("/orders/{id:guid}", (
            OrderId id,
            ReplaceOrderRequest request,
            OrderDbContext db,
            HttpContext httpContext,
            CancellationToken ct) =>
            db.Orders
                .FirstOrDefaultResultAsync(o => o.Id == id, new Error.NotFound(ResourceRef.For<Order>(id)), ct)
                .RequireETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
                .BindAsync(o => o.Replace(request))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync(ct))
                .ToHttpResponseAsync(OrderResponse.From, opts => opts.HonorPrefer()));
    }
}
