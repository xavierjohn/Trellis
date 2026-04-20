namespace SampleMinimalApi.Models;

using SampleUserLibrary;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Wire representation of a registered user. Every field is a value object so the JSON
/// shape carries the same constraints the domain enforces (axiom A1b).
/// </summary>
public sealed record UserResponse(
    UserId Id,
    FirstName FirstName,
    LastName LastName,
    EmailAddress Email,
    PhoneNumber Phone,
    Age Age,
    CountryCode Country,
    Maybe<Url> Website)
{
    public static UserResponse From(User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email,
        user.Phone,
        user.Age,
        user.Country,
        user.Website);
}

/// <summary>
/// Wire representation of a product.
/// </summary>
public sealed record ProductResponse(
    ProductId Id,
    ProductName Name,
    MonetaryAmount Price,
    int StockQuantity)
{
    public static ProductResponse From(Product product) =>
        new(product.Id, product.Name, product.Price, product.StockQuantity);
}

/// <summary>
/// Wire representation of an order line.
/// </summary>
public sealed record OrderLineResponse(
    OrderLineId Id,
    ProductId ProductId,
    ProductName ProductName,
    MonetaryAmount UnitPrice,
    int Quantity)
{
    public static OrderLineResponse From(OrderLine line) =>
        new(line.Id, line.ProductId, line.ProductName, line.UnitPrice, line.Quantity);
}

/// <summary>
/// Wire representation of an order. <see cref="Total"/> is a <see cref="MonetaryAmount"/>
/// VO instead of a raw decimal so the JSON contract matches the domain (axiom A1b).
/// </summary>
public sealed record OrderResponse(
    OrderId Id,
    CustomerId CustomerId,
    OrderState State,
    MonetaryAmount Total,
    IReadOnlyList<OrderLineResponse> Lines)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.State,
        // Total is a sum of valid prices × non-negative quantities, so MonetaryAmount.TryCreate
        // is provably non-failing here. Match-with-fallback keeps us off the .Value axiom (A3).
        MonetaryAmount.TryCreate(order.Total).Match(m => m, _ => MonetaryAmount.Zero),
        order.Lines.Select(OrderLineResponse.From).ToList());
}
