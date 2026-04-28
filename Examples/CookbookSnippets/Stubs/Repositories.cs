namespace CookbookSnippets.Stubs;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe06;
using Trellis;

public sealed class EfOrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Maybe<Order>> FindAsync(OrderId id, CancellationToken ct) =>
        Task.FromResult(Maybe<Order>.None);

    public void Add(Order order) => db.Orders.Add(order);
}

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly List<Order> _orders = new();

    public Task<Maybe<Order>> FindAsync(OrderId id, CancellationToken ct) =>
        Task.FromResult(Maybe<Order>.None);

    public void Add(Order order) => _orders.Add(order);

    public Order Last() => _orders[^1];
}

public sealed record BlobId(System.Guid Value);

public sealed class BlobContent
{
    public string Sha256Hex { get; init; } = string.Empty;
    public System.DateTimeOffset UploadedAt { get; init; }
    public long Length { get; init; }
}

public interface IBlobRepository
{
    Task<Result<BlobContent>> FindAsync(BlobId id, CancellationToken ct);
}