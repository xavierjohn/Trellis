namespace Trellis.Core.Tests.DomainDrivenDesign.Specification;

/// <summary>
/// Tests for the Or composition of <see cref="Trellis.Specification{T}"/>.
/// </summary>
public class OrSpecificationTests
{
    #region Truth Table

    [Fact]
    public void Or_TrueOrTrue_ReturnsTrue()
    {
        // Arrange
        var spec = new AlwaysTrueSpec().Or(new AlwaysTrueSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void Or_TrueOrFalse_ReturnsTrue()
    {
        // Arrange
        var spec = new AlwaysTrueSpec().Or(new AlwaysFalseSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void Or_FalseOrTrue_ReturnsTrue()
    {
        // Arrange
        var spec = new AlwaysFalseSpec().Or(new AlwaysTrueSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void Or_FalseOrFalse_ReturnsFalse()
    {
        // Arrange
        var spec = new AlwaysFalseSpec().Or(new AlwaysFalseSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    #endregion

    #region Business Rule Composition

    [Fact]
    public void Or_FirstConditionMet_ReturnsTrue()
    {
        // Arrange — price > 500 OR category == "Electronics"
        var spec = new PriceAboveSpec(500m).Or(new CategorySpec("Electronics"));
        var product = new Product("Laptop", 999m, "Clothing");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void Or_SecondConditionMet_ReturnsTrue()
    {
        // Arrange — price > 500 OR category == "Electronics"
        var spec = new PriceAboveSpec(500m).Or(new CategorySpec("Electronics"));
        var product = new Product("Cable", 5m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void Or_NeitherConditionMet_ReturnsFalse()
    {
        // Arrange — price > 500 OR category == "Electronics"
        var spec = new PriceAboveSpec(500m).Or(new CategorySpec("Electronics"));
        var product = new Product("Shirt", 25m, "Clothing");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    #endregion

    #region IQueryable Integration

    [Fact]
    public void Or_ExpressionTree_WorksWithIQueryable()
    {
        // Arrange
        var spec = new PriceAboveSpec(500m).Or(new CategorySpec("Electronics"));
        var products = new[]
        {
            new Product("Laptop", 999m, "Electronics"),
            new Product("Cable", 5m, "Electronics"),
            new Product("Designer Jacket", 800m, "Clothing"),
            new Product("Shirt", 25m, "Clothing")
        }.AsQueryable();

        // Act
        var result = products.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(p => p.Name == "Laptop");
        result.Should().Contain(p => p.Name == "Cable");
        result.Should().Contain(p => p.Name == "Designer Jacket");
    }

    #endregion
}