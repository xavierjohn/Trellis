namespace SampleUserLibrary;

using Trellis;
using Trellis.Primitives;

/// <summary>
/// Product aggregate demonstrating ETag-based optimistic concurrency,
/// Specification-based querying, and value-object-rich domain modeling.
/// </summary>
public class Product : Aggregate<ProductId>
{
    public ProductName Name { get; private set; } = null!;
    public MonetaryAmount Price { get; private set; } = null!;
    public int StockQuantity { get; private set; }

    // EF Core parameterless constructor
    private Product() : base(default!) { }

    private Product(ProductId id, ProductName name, MonetaryAmount price, int stockQuantity) : base(id)
    {
        Name = name;
        Price = price;
        StockQuantity = stockQuantity;
    }

    /// <summary>
    /// Creates a new product with validation.
    /// </summary>
    public static Result<Product> TryCreate(ProductName name, MonetaryAmount price, int stockQuantity) =>
        name.ToResult()
            .Ensure(_ => stockQuantity >= 0, Error.Validation("Stock cannot be negative", nameof(stockQuantity)))
            .Map(_ => new Product(ProductId.NewUniqueV7(), name, price, stockQuantity));

    /// <summary>
    /// Updates the product price. Returns Result for railway chaining.
    /// </summary>
    public Result<Product> UpdatePrice(MonetaryAmount newPrice) =>
        this.ToResult()
            .Tap(_ => Price = newPrice);

    /// <summary>
    /// Adjusts stock by a delta (positive to add, negative to remove).
    /// </summary>
    public Result<Product> AdjustStock(int delta) =>
        this.ToResult()
            .Ensure(_ => StockQuantity + delta >= 0,
                Error.Validation($"Insufficient stock. Available: {StockQuantity}, requested: {-delta}", nameof(delta)))
            .Tap(_ => StockQuantity += delta);

    /// <summary>
    /// Reserves stock for an order (reduces stock).
    /// </summary>
    public Result<Product> ReserveStock(int quantity) =>
        this.ToResult()
            .Ensure(_ => quantity > 0, Error.Validation("Quantity must be positive", nameof(quantity)))
            .Ensure(_ => StockQuantity >= quantity,
                Error.Validation($"Insufficient stock. Available: {StockQuantity}", nameof(quantity)))
            .Tap(_ => StockQuantity -= quantity);

    /// <summary>
    /// Releases previously reserved stock (restores stock).
    /// </summary>
    public Result<Product> ReleaseStock(int quantity) =>
        this.ToResult()
            .Ensure(_ => quantity > 0, Error.Validation("Quantity must be positive", nameof(quantity)))
            .Tap(_ => StockQuantity += quantity);
}