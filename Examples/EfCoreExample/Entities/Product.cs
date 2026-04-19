namespace EfCoreExample.Entities;

using EfCoreExample.ValueObjects;
using Trellis;

/// <summary>
/// Product entity with strongly-typed ID (GUID) and validated name.
/// </summary>
public class Product : Entity<ProductId>
{
    public ProductName Name { get; private set; } = null!;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }

    // EF Core requires parameterless constructor
    private Product() : base(ProductId.NewUniqueV4()) { }

    private Product(ProductId id, ProductName name, decimal price, int stockQuantity) : base(id)
    {
        Name = name;
        Price = price;
        StockQuantity = stockQuantity;
    }

    /// <summary>
    /// Creates a new product with validation.
    /// </summary>
    public static Result<Product> TryCreate(string? name, decimal price, int stockQuantity) =>
        ProductName.TryCreate(name, nameof(name))
            .Ensure(_ => price > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(price)), "validation.error") { Detail = "Price must be greater than zero" })))
            .Ensure(_ => stockQuantity >= 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(stockQuantity)), "validation.error") { Detail = "Stock cannot be negative" })))
            .Map(productName => new Product(
                ProductId.NewUniqueV4(),
                productName,
                price,
                stockQuantity));

    /// <summary>
    /// Reduces stock when order is placed.
    /// </summary>
    public Result<Product> ReduceStock(int quantity) =>
        this.ToResult()
            .Ensure(_ => quantity > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(quantity)), "validation.error") { Detail = "Quantity must be positive" })))
            .Ensure(_ => StockQuantity >= quantity, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(quantity)), "validation.error") { Detail = $"Insufficient stock. Available: {StockQuantity}" })))
            .Tap(_ => StockQuantity -= quantity);
}