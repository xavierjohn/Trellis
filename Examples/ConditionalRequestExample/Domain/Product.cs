using Trellis;

namespace ConditionalRequestExample.Domain;

/// <summary>
/// Strongly-typed Product identifier.
/// </summary>
public partial class ProductId : RequiredGuid<ProductId> { }

/// <summary>
/// Validated product name (1–100 characters).
/// </summary>
[StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

/// <summary>
/// Product aggregate demonstrating ETag-based optimistic concurrency.
/// </summary>
public class Product : Aggregate<ProductId>
{
    public ProductName Name { get; private set; } = null!;
    public decimal Price { get; private set; }

    private Product() : base(default!) { }

    private Product(ProductId id, ProductName name, decimal price) : base(id)
    {
        Name = name;
        Price = price;
    }

    public static Result<Product> TryCreate(ProductName name, decimal price) =>
        name.ToResult()
            .Ensure(_ => price > 0, Error.Validation("Price must be greater than zero", nameof(price)))
            .Map(_ => new Product(ProductId.NewUniqueV4(), name, price));

    public Result<Product> UpdatePrice(decimal newPrice) =>
        this.ToResult()
            .Ensure(_ => newPrice > 0, Error.Validation("Price must be greater than zero", nameof(newPrice)))
            .Tap(_ => Price = newPrice);
}
