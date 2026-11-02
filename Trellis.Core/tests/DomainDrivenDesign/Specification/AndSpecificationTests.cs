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

    #region Evaluation

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void And_Evaluation_SelectedRight_ExecutesPredicatesOnceInOrder(bool rightResult)
    {
        List<string> calls = [];
        var left = new PredicateSpec(_ =>
        {
            calls.Add("left");
            return true;
        });
        var right = new PredicateSpec(_ =>
        {
            calls.Add("right");
            return rightResult;
        });
        var spec = left.And(right);
        var product = new Product("Widget", 100m, "Electronics");

        spec.IsSatisfiedBy(product).Should().Be(rightResult);
        calls.Should().Equal(["left", "right"]);
    }

    [Fact]
    public void And_LeftFalse_ThrowingRight_IsNotEvaluated()
    {
        var spec = new AlwaysFalseSpec().And(
            new PredicateSpec(_ => throw new InvalidOperationException("Right predicate evaluated.")));
        var product = new Product("Widget", 100m, "Electronics");

        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void And_NullOperand_PublicCombinator_ThrowsArgumentNullException()
    {
        var act = () => new AlwaysTrueSpec().And(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("other");
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