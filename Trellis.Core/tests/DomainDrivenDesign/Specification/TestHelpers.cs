namespace Trellis.Core.Tests.DomainDrivenDesign.Specification;

using System.Linq.Expressions;
using Trellis;

/// <summary>
/// Simple test entity used across all specification tests.
/// </summary>
internal record Product(string Name, decimal Price, string Category);

/// <summary>
/// Specification that matches products with a price above the given threshold.
/// </summary>
internal class PriceAboveSpec(decimal threshold) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        p => p.Price > threshold;
}

/// <summary>
/// Specification that matches products in the given category.
/// </summary>
internal class CategorySpec(string category) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        p => p.Category == category;
}

/// <summary>
/// Specification that matches products whose name contains the given text.
/// </summary>
internal class NameContainsSpec(string text) : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        p => p.Name.Contains(text);
}

/// <summary>
/// Specification that always returns true — useful for composition tests.
/// </summary>
internal class AlwaysTrueSpec : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        _ => true;
}

/// <summary>
/// Specification that always returns false — useful for composition tests.
/// </summary>
internal class AlwaysFalseSpec : Specification<Product>
{
    public override Expression<Func<Product, bool>> ToExpression() =>
        _ => false;
}