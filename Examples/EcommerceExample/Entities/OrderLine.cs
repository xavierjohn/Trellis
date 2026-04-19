using Trellis.Primitives;

namespace EcommerceExample.Entities;

using EcommerceExample.ValueObjects;
using Trellis;

/// <summary>
/// Represents a line item in an order.
/// </summary>
public class OrderLine : Entity<ProductId>
{
    public string ProductName { get; }
    public Money UnitPrice { get; }
    public int Quantity { get; private set; }
    public Money LineTotal { get; private set; }

    private OrderLine(ProductId productId, string productName, Money unitPrice, int quantity, Money lineTotal)
        : base(productId)
    {
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = lineTotal;
    }

    public static Result<OrderLine> TryCreate(ProductId productId, string productName, Money unitPrice, int quantity)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return Result.Fail<EcommerceExample.Entities.OrderLine>(Error.Validation("Product name is required", nameof(productName)));

        if (quantity <= 0)
            return Result.Fail<EcommerceExample.Entities.OrderLine>(Error.Validation("Quantity must be greater than zero", nameof(quantity)));

        if (quantity > 1000)
            return Result.Fail<EcommerceExample.Entities.OrderLine>(Error.Validation("Quantity cannot exceed 1000 per line", nameof(quantity)));

        return unitPrice
            .Multiply(quantity)
            .Map(lineTotal => new OrderLine(productId, productName, unitPrice, quantity, lineTotal));
    }

    public Result<OrderLine> UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            return Result.Fail<EcommerceExample.Entities.OrderLine>(Error.Validation("Quantity must be greater than zero", nameof(newQuantity)));

        if (newQuantity > 1000)
            return Result.Fail<EcommerceExample.Entities.OrderLine>(Error.Validation("Quantity cannot exceed 1000 per line", nameof(newQuantity)));

        return UnitPrice
            .Multiply(newQuantity)
            .Map(lineTotal =>
            {
                Quantity = newQuantity;
                LineTotal = lineTotal;
                return this;
            });
    }
}
