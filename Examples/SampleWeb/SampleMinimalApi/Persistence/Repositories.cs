namespace SampleMinimalApi.Persistence;

using System.Collections.Concurrent;
using SampleUserLibrary;
using Trellis;

/// <summary>
/// Repository abstraction for <see cref="User"/>. Returns Result rather than throwing on miss
/// (axiom A11) and never references ASP.NET Core types (axiom A8 — domain-port purity).
/// </summary>
public interface IUserRepository
{
    Task<Result<User>> GetAsync(UserId id, CancellationToken cancellationToken);
    Task<Result> SaveAsync(User user, CancellationToken cancellationToken);
}

public interface IProductRepository
{
    Task<Result<Product>> GetAsync(ProductId id, CancellationToken cancellationToken);
    Task<Result> SaveAsync(Product product, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);
}

public interface IOrderRepository
{
    Task<Result<Order>> GetAsync(OrderId id, CancellationToken cancellationToken);
    Task<Result> SaveAsync(Order order, CancellationToken cancellationToken);
}

/// <summary>Thread-safe in-memory store backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>.</summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<UserId, User> _users = new();

    public Task<Result<User>> GetAsync(UserId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var result = _users.TryGetValue(id, out var user)
            ? Result.Ok(user)
            : Result.Fail<User>(NotFound("User", id.Value.ToString()));
        return Task.FromResult(result);
    }

    public Task<Result> SaveAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        _users[user.Id] = user;
        return Task.FromResult(Result.Ok());
    }

    private static Error.NotFound NotFound(string resource, string id) =>
        new(new ResourceRef(resource, id)) { Detail = $"{resource} '{id}' was not found." };
}

public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<ProductId, Product> _products = new();

    public Task<Result<Product>> GetAsync(ProductId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var result = _products.TryGetValue(id, out var product)
            ? Result.Ok(product)
            : Result.Fail<Product>(new Error.NotFound(new ResourceRef("Product", id.Value.ToString())) { Detail = $"Product '{id.Value}' was not found." });
        return Task.FromResult(result);
    }

    public Task<Result> SaveAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        _products[product.Id] = product;
        return Task.FromResult(Result.Ok());
    }

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(_products.Values.ToList());
}

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<OrderId, Order> _orders = new();

    public Task<Result<Order>> GetAsync(OrderId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var result = _orders.TryGetValue(id, out var order)
            ? Result.Ok(order)
            : Result.Fail<Order>(new Error.NotFound(new ResourceRef("Order", id.Value.ToString())) { Detail = $"Order '{id.Value}' was not found." });
        return Task.FromResult(result);
    }

    public Task<Result> SaveAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders[order.Id] = order;
        return Task.FromResult(Result.Ok());
    }
}
