// Cookbook Recipe 37 — Reconstituting an aggregate without its factory (non-EF repositories).
namespace CookbookSnippets.Recipe37;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trellis;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed partial class OrderLineId : RequiredGuid<OrderLineId>;

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed partial class ProductId : RequiredString<ProductId>;

public sealed partial class OrderStatus : RequiredString<OrderStatus>
{
    public static OrderStatus Draft { get; } =
        TryCreate("Draft").GetValueOrThrow("\"Draft\" must be a valid OrderStatus.");
}

// Child entities follow the same pattern: a private reconstitution constructor + Reconstitute factory.
public sealed class OrderLine : Entity<OrderLineId>
{
    private OrderLine(OrderLineId id, ProductId productId, int quantity) : base(id)
    {
        ProductId = productId;
        Quantity = quantity;
    }

    public ProductId ProductId { get; }

    public int Quantity { get; }

    internal static OrderLine Reconstitute(OrderLineId id, ProductId productId, int quantity) =>
        new(id, productId, quantity);
}

public sealed class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    private Order(OrderId id, CustomerId customerId) : base(id)   // create-time ctor
    {
        CustomerId = customerId;
        Status = OrderStatus.Draft;
    }

    // Reconstitution ctor - pure assignment, no Create, no behavior methods, no events.
    private Order(OrderId id, CustomerId customerId, OrderStatus status, IEnumerable<OrderLine> lines)
        : base(id)
    {
        CustomerId = customerId;
        Status = status;
        _lines.AddRange(lines);
    }

    public CustomerId CustomerId { get; }

    public OrderStatus Status { get; }

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public static Result<Order> Create(CustomerId customerId, OrderId id) =>
        Result.Ok(new Order(id, customerId));

    // Rebuilds an Order from stored domain state. Does NOT run Create or raise events.
    internal static Order Reconstitute(
        OrderId id, CustomerId customerId, OrderStatus status, IEnumerable<OrderLine> lines) =>
        new(id, customerId, status, lines);
}

// Stand-ins for the storage rows a Dapper/ADO/Cosmos adapter would materialize.
public sealed record OrderRow(
    Guid Id,
    Guid CustomerId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModified,
    string ETag);

public sealed record OrderLineRow(Guid Id, string ProductId, int Quantity);

public interface IOrderRowStore
{
    Task<(OrderRow? Order, IReadOnlyList<OrderLineRow> Lines)> LoadAsync(
        Guid id, CancellationToken cancellationToken);
}

public interface IOrderRepository
{
    Task<Result<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken);
}

public sealed class DapperOrderRepository(IOrderRowStore db) : IOrderRepository
{
    public async Task<Result<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        var (row, lineRows) = await db.LoadAsync(id.Value, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Order>(id)));

        var lines = lineRows.Select(r => OrderLine.Reconstitute(
            OrderLineId.TryCreate(r.Id).GetValueOrThrow($"Corrupt OrderLine.Id in row {r.Id}"),
            ProductId.TryCreate(r.ProductId).GetValueOrThrow($"Corrupt OrderLine.ProductId in row {r.Id}"),
            r.Quantity));

        var order = Order.Reconstitute(
            OrderId.TryCreate(row.Id).GetValueOrThrow($"Corrupt Order.Id in row {row.Id}"),
            CustomerId.TryCreate(row.CustomerId).GetValueOrThrow($"Corrupt Order.CustomerId in row {row.Id}"),
            OrderStatus.TryCreate(row.Status).GetValueOrThrow($"Corrupt Order.Status in row {row.Id}"),
            lines);

        // Restore the infrastructure metadata loaded from the row. A store-native quoted token (e.g. a
        // Cosmos _etag) must be normalized to its unquoted opaque form before stamping.
        ((IReconstitutionStampable)order)
            .StampReconstitutedState(row.CreatedAt, row.LastModified, row.ETag);

        return Result.Ok(order);
    }
}
