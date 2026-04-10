namespace SampleUserLibrary;

using Trellis;
using Trellis.Primitives;

/// <summary>
/// Order line entity — child of Order aggregate.
/// Demonstrates Entity&lt;T&gt; with strongly-typed IDs and computed properties.
/// </summary>
public class OrderLine : Entity<OrderLineId>
{
    public OrderId OrderId { get; private set; } = null!;
    public ProductId ProductId { get; private set; } = null!;
    public ProductName ProductName { get; private set; } = null!;
    public MonetaryAmount UnitPrice { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice.Value * Quantity;

    // EF Core parameterless constructor
    private OrderLine() : base(default!) { }

    internal OrderLine(OrderId orderId, Product product, int quantity)
        : base(OrderLineId.NewUniqueV7())
    {
        OrderId = orderId;
        ProductId = product.Id;
        ProductName = product.Name;
        UnitPrice = product.Price;
        Quantity = quantity;
    }
}