namespace Trellis.Core.Tests.DomainDrivenDesign.Specification;

/// <summary>
/// Tests for the And composition of <see cref="Trellis.Specification{T}"/>.
/// </summary>
public class AndSpecificationTests
{
    #region Truth Table

    [Fact]
    public void And_TrueAndTrue_ReturnsTrue()
    {
        // Arrange
        var spec = new AlwaysTrueSpec().And(new AlwaysTrueSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void And_TrueAndFalse_ReturnsFalse()
    {
        // Arrange
        var spec = new AlwaysTrueSpec().And(new AlwaysFalseSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void And_FalseAndTrue_ReturnsFalse()
    {
        // Arrange
        var spec = new AlwaysFalseSpec().And(new AlwaysTrueSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void And_FalseAndFalse_ReturnsFalse()
    {
        // Arrange
        var spec = new AlwaysFalseSpec().And(new AlwaysFalseSpec());
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    #endregion

    #region Business Rule Composition

    [Fact]
    public void And_BothConditionsMet_ReturnsTrue()
    {
        // Arrange — price > 50 AND category == "Electronics"
        var spec = new PriceAboveSpec(50m).And(new CategorySpec("Electronics"));
        var product = new Product("Laptop", 999m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void And_OnlyFirstConditionMet_ReturnsFalse()
    {
        // Arrange — price > 50 AND category == "Electronics"
        var spec = new PriceAboveSpec(50m).And(new CategorySpec("Electronics"));
        var product = new Product("Jacket", 200m, "Clothing");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void And_OnlySecondConditionMet_ReturnsFalse()
    {
        // Arrange — price > 50 AND category == "Electronics"
        var spec = new PriceAboveSpec(50m).And(new CategorySpec("Electronics"));
        var product = new Product("Cable", 5m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    #endregion

    #region IQueryable Integration

    [Fact]
    public void And_ExpressionTree_WorksWithIQueryable()
    {
        // Arrange
        var spec = new PriceAboveSpec(50m).And(new CategorySpec("Electronics"));
        var products = new[]
        {
            new Product("Laptop", 999m, "Electronics"),
            new Product("Cable", 5m, "Electronics"),
            new Product("Jacket", 200m, "Clothing"),
            new Product("Phone", 699m, "Electronics")
        }.AsQueryable();

        // Act
        var result = products.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "Laptop");
        result.Should().Contain(p => p.Name == "Phone");
    }

    #endregion
}