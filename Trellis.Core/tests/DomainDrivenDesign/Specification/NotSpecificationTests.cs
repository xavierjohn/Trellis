namespace Trellis.Core.Tests.DomainDrivenDesign.Specification;

/// <summary>
/// Tests for the Not composition of <see cref="Trellis.Specification{T}"/>.
/// </summary>
public class NotSpecificationTests
{
    #region Truth Table

    [Fact]
    public void Not_True_ReturnsFalse()
    {
        // Arrange
        var spec = new AlwaysTrueSpec().Not();
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void Not_False_ReturnsTrue()
    {
        // Arrange
        var spec = new AlwaysFalseSpec().Not();
        var product = new Product("Widget", 100m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    #endregion

    #region Business Rule Negation

    [Fact]
    public void Not_NegatesMatchingEntity()
    {
        // Arrange — NOT (category == "Electronics")
        var spec = new CategorySpec("Electronics").Not();
        var product = new Product("Laptop", 999m, "Electronics");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void Not_NegatesNonMatchingEntity()
    {
        // Arrange — NOT (category == "Electronics")
        var spec = new CategorySpec("Electronics").Not();
        var product = new Product("Shirt", 25m, "Clothing");

        // Act & Assert
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    #endregion

    #region IQueryable Integration

    [Fact]
    public void Not_ExpressionTree_WorksWithIQueryable()
    {
        // Arrange — NOT (price > 100)
        var spec = new PriceAboveSpec(100m).Not();
        var products = new[]
        {
            new Product("Cheap", 10m, "A"),
            new Product("Mid", 100m, "B"),
            new Product("Expensive", 200m, "C")
        }.AsQueryable();

        // Act
        var result = products.Where(spec.ToExpression()).ToList();

        // Assert — "Cheap" (10) and "Mid" (100, not > 100) should match
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "Cheap");
        result.Should().Contain(p => p.Name == "Mid");
    }

    #endregion
}