using Trellis;
using Trellis.Primitives;

namespace ConditionalRequestExample.Domain;

/// <summary>
/// Strongly-typed Product identifier.
/// </summary>
public partial class ProductId : RequiredGuid<ProductId> { }

/// <summary>
/// Validated product name (1-100 characters).
/// </summary>
[StringLength(100)]
public partial class ProductName : RequiredString<ProductName> { }

/// <summary>
/// Product aggregate demonstrating ETag-based optimistic concurrency.
/// </summary>
public class Product : Aggregate<ProductId>
{
    public ProductName Name { get; private set; } = null!;
    public MonetaryAmount Price { get; private set; } = null!;

    private Product() : base(default!) { }

    private Product(ProductId id, ProductName name, MonetaryAmount price) : base(id)
    {
        Name = name;
        Price = price;
    }

    public static Result<Product> TryCreate(ProductName name, MonetaryAmount price) =>
        name.ToResult()
            .Map(_ => new Product(ProductId.NewUniqueV4(), name, price));

    public Result<Product> UpdatePrice(MonetaryAmount newPrice) =>
        this.ToResult()
            .Tap(_ => Price = newPrice);
}