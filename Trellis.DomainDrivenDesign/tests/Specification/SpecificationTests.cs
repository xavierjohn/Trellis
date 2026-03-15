namespace Trellis.DomainDrivenDesign.Tests.Specification;

using System.Linq.Expressions;
using Trellis;

/// <summary>
/// Tests for <see cref="Specification{T}"/> base class:
/// IsSatisfiedBy, ToExpression, and implicit conversion.
/// </summary>
public class SpecificationTests
{
    #region IsSatisfiedBy

    [Fact]
    public void IsSatisfiedBy_MatchingEntity_ReturnsTrue()
    {
        // Arrange
        var spec = new PriceAboveSpec(50m);
        var product = new Product("Widget", 100m, "Electronics");

        // Act
        var result = spec.IsSatisfiedBy(product);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSatisfiedBy_NonMatchingEntity_ReturnsFalse()
    {
        // Arrange
        var spec = new PriceAboveSpec(50m);
        var product = new Product("Widget", 30m, "Electronics");

        // Act
        var result = spec.IsSatisfiedBy(product);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSatisfiedBy_BoundaryValue_ReturnsFalse()
    {
        // Arrange — spec is "price > 50", so exactly 50 should be false
        var spec = new PriceAboveSpec(50m);
        var product = new Product("Widget", 50m, "Electronics");

        // Act
        var result = spec.IsSatisfiedBy(product);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ToExpression

    [Fact]
    public void ToExpression_ReturnsValidExpressionTree_ThatCanBeCompiledAndInvoked()
    {
        // Arrange
        var spec = new CategorySpec("Electronics");
        var matching = new Product("TV", 500m, "Electronics");
        var nonMatching = new Product("Shirt", 20m, "Clothing");

        // Act
        var expression = spec.ToExpression();
        var compiled = expression.Compile();

        // Assert
        compiled(matching).Should().BeTrue();
        compiled(nonMatching).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_CanBeUsedWithIQueryable()
    {
        // Arrange
        var spec = new PriceAboveSpec(50m);
        var products = new[]
        {
            new Product("Cheap", 10m, "A"),
            new Product("Mid", 60m, "B"),
            new Product("Expensive", 200m, "C")
        }.AsQueryable();

        // Act
        var result = products.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "Mid");
        result.Should().Contain(p => p.Name == "Expensive");
    }

    #endregion

    #region Implicit Conversion

    [Fact]
    public void ImplicitConversion_ToExpression_WorksWithWhere()
    {
        // Arrange
        var spec = new NameContainsSpec("Pro");
        var products = new[]
        {
            new Product("Product A", 10m, "A"),
            new Product("Item B", 20m, "B"),
            new Product("Pro Widget", 30m, "C")
        }.AsQueryable();

        // Act — implicit conversion: spec → Expression<Func<Product, bool>>
        Expression<Func<Product, bool>> expression = spec;
        var result = products.Where(expression).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "Product A");
        result.Should().Contain(p => p.Name == "Pro Widget");
    }

    [Fact]
    public void ImplicitConversion_ProducesEquivalentExpression()
    {
        // Arrange
        var spec = new PriceAboveSpec(100m);
        var product = new Product("Expensive", 150m, "A");

        // Act
        Expression<Func<Product, bool>> implicitExpr = spec;
        var explicitExpr = spec.ToExpression();

        // Assert
        implicitExpr.Compile()(product).Should().Be(explicitExpr.Compile()(product));
    }

    #endregion

    #region IsSatisfiedBy caching

    [Fact]
    public void IsSatisfiedBy_CalledMultipleTimes_DoesNotRecompileExpression()
    {
        var spec = new CompileCountingSpec(50m);
        var product = new Product("Widget", 100m, "Electronics");

        spec.IsSatisfiedBy(product);
        spec.IsSatisfiedBy(product);
        spec.IsSatisfiedBy(product);

        spec.CompileCount.Should().Be(1,
            "IsSatisfiedBy should cache the compiled predicate instead of recompiling on each call");
    }

    #endregion
}

file class CompileCountingSpec(decimal threshold) : Specification<Product>
{
    public int CompileCount { get; private set; }

    public override Expression<Func<Product, bool>> ToExpression()
    {
        CompileCount++;
        return p => p.Price > threshold;
    }
}