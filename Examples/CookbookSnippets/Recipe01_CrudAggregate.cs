// Cookbook Recipe 1 — CRUD aggregate (DDD value objects + entity + repository contract).
namespace CookbookSnippets.Recipe01;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Trellis;

// Strongly-typed ID: source-generated factory, equality, parsing, JSON converter.
public sealed partial class OrderId : RequiredGuid<OrderId>;

// Value object backed by a 3-letter ISO 4217 currency code.
[StringLength(3, MinimumLength = 3)]
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// Composite value object via primary-constructor class.
public sealed class Money(decimal amount, CurrencyCode currency) : ValueObject
{
    public decimal Amount { get; } = amount;
    public CurrencyCode Currency { get; } = currency;

    protected override IEnumerable<System.IComparable?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

// Trellis convention: model finite domain states as RequiredEnum<TSelf>
// (NOT C# enums). The partial keyword triggers the source generator.
public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();
    public static readonly OrderStatus Submitted = new();
    public static readonly OrderStatus Cancelled = new();
}

// Aggregate root.
public sealed class Order : Aggregate<OrderId>
{
    public Money Total { get; private set; } = default!;
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;

    // Owner identifier referenced by Recipe 7's resource-based authorization sample.
    public string OwnerId { get; private set; } = string.Empty;

    private Order(OrderId id) : base(id) { }   // EF Core ctor

    public static Result<Order> Create(OrderId id, Money total) =>
        Result.Ok(new Order(id) { Total = total, Status = OrderStatus.Draft });
}

// Repository contract — uses Maybe<T> for "may legitimately find nothing"
// Reserve Result<T> for failures the caller can act on.
public interface IOrderRepository
{
    Task<Maybe<Order>> FindAsync(OrderId id, CancellationToken ct);
    void Add(Order order);
}