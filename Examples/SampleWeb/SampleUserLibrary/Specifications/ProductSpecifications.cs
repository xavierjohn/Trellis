namespace SampleUserLibrary;

using System.Linq.Expressions;
using Trellis;

/// <summary>
/// Specification for products that are in stock.
/// Demonstrates the Specification pattern for composable business rules.
/// </summary>
public class InStockSpecification : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        product => product.StockQuantity > 0;
}

/// <summary>
/// Specification for products within a price range.
/// Demonstrates parameterized specifications.
/// </summary>
public class PriceRangeSpecification(decimal minPrice, decimal maxPrice) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        product => product.Price.Value >= minPrice && product.Price.Value <= maxPrice;
}