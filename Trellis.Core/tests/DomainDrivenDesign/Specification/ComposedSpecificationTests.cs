namespace Trellis.Core.Tests.DomainDrivenDesign.Specification;

using Trellis.Testing;

/// <summary>
/// Tests for complex composed specifications — triple composition,
/// deep nesting, IQueryable compatibility, and immutability.
/// </summary>
public class ComposedSpecificationTests
{
    #region Triple Composition

    [Fact]
    public void Composed_AndThenOr_EvaluatesCorrectly()
    {
        // Arrange — (price > 50 AND category == "Electronics") OR name contains "Pro"
        var spec = new PriceAboveSpec(50m)
            .And(new CategorySpec("Electronics"))
            .Or(new NameContainsSpec("Pro"));

        var expensiveElectronics = new Product("Laptop", 999m, "Electronics");
        var cheapElectronics = new Product("Cable", 5m, "Electronics");
        var proClothing = new Product("Pro Jacket", 25m, "Clothing");
        var cheapClothing = new Product("Shirt", 25m, "Clothing");

        // Act & Assert
        spec.Should().BeSatisfiedBy(expensiveElectronics, "expensive electronics matches AND clause");
        spec.Should().NotBeSatisfiedBy(cheapElectronics, "cheap electronics fails AND; no 'Pro' in name");
        spec.Should().BeSatisfiedBy(proClothing, "'Pro' in name matches OR clause");
        spec.Should().NotBeSatisfiedBy(cheapClothing, "neither clause is satisfied");
    }

    #endregion

    #region Deep Nesting

    [Fact]
    public void Composed_DeeplyNested_EvaluatesCorrectly()
    {
        // Arrange — price > 50 AND (category == "Electronics" OR NOT(name contains "Cheap"))
        var innerSpec = new CategorySpec("Electronics")
            .Or(new NameContainsSpec("Cheap").Not());

        var spec = new PriceAboveSpec(50m).And(innerSpec);

        // price > 50 AND (Electronics OR name does NOT contain "Cheap")
        var expensiveElectronics = new Product("Laptop", 999m, "Electronics");
        var expensiveClothing = new Product("Designer Jacket", 200m, "Clothing");
        var cheapItem = new Product("Cheap Widget", 100m, "General");

        // Act & Assert
        spec.Should().BeSatisfiedBy(expensiveElectronics, "expensive + Electronics → both clauses pass");
        spec.Should().BeSatisfiedBy(expensiveClothing, "expensive + NOT 'Cheap' in name → passes");
        // "Cheap Widget" has price > 50, category != Electronics, name contains "Cheap" → inner OR is false
        spec.Should().NotBeSatisfiedBy(cheapItem, "name contains 'Cheap' and not Electronics → inner OR fails");
    }

    #endregion

    #region IQueryable Integration

    [Fact]
    public void Composed_ExpressionTree_WorksWithIQueryable()
    {
        // Arrange — (price > 100 AND category == "Electronics") OR name contains "Pro"
        var spec = new PriceAboveSpec(100m)
            .And(new CategorySpec("Electronics"))
            .Or(new NameContainsSpec("Pro"));

        var products = new[]
        {
            new Product("Laptop", 999m, "Electronics"),         // AND clause ✓
            new Product("Cable", 5m, "Electronics"),             // AND fails (price)
            new Product("Pro Jacket", 25m, "Clothing"),          // OR clause ✓
            new Product("Shirt", 25m, "Clothing"),               // Neither ✗
            new Product("Pro Monitor", 500m, "Electronics")      // Both clauses ✓
        }.AsQueryable();

        // Act
        var result = products.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(p => p.Name == "Laptop");
        result.Should().Contain(p => p.Name == "Pro Jacket");
        result.Should().Contain(p => p.Name == "Pro Monitor");
    }

    [Fact]
    public void Composed_DeeplyNested_WorksWithIQueryable()
    {
        // Arrange — price > 50 AND (category == "Electronics" OR NOT(name contains "Cheap"))
        var spec = new PriceAboveSpec(50m)
            .And(new CategorySpec("Electronics").Or(new NameContainsSpec("Cheap").Not()));

        var products = new[]
        {
            new Product("Laptop", 999m, "Electronics"),          // ✓ price>50, Electronics
            new Product("Designer Jacket", 200m, "Clothing"),    // ✓ price>50, NOT "Cheap"
            new Product("Cheap Widget", 100m, "General"),        // ✗ price>50, but "Cheap" + not Electronics
            new Product("Budget Item", 30m, "General")           // ✗ price<=50
        }.AsQueryable();

        // Act
        var result = products.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "Laptop");
        result.Should().Contain(p => p.Name == "Designer Jacket");
    }

    #endregion

    #region Immutability

    [Fact]
    public void Composition_PreservesOriginalSpecifications()
    {
        // Arrange
        var priceSpec = new PriceAboveSpec(50m);
        var categorySpec = new CategorySpec("Electronics");

        // Act — compose does NOT modify originals
        var composedSpec = priceSpec.And(categorySpec);

        var cheapElectronics = new Product("Cable", 5m, "Electronics");
        var expensiveClothing = new Product("Jacket", 200m, "Clothing");

        // Assert — original specs still work independently
        priceSpec.Should().NotBeSatisfiedBy(cheapElectronics, "price spec: 5 is not > 50");
        priceSpec.Should().BeSatisfiedBy(expensiveClothing, "price spec: 200 > 50");

        categorySpec.Should().BeSatisfiedBy(cheapElectronics, "category spec: Electronics");
        categorySpec.Should().NotBeSatisfiedBy(expensiveClothing, "category spec: not Electronics");

        // Composed spec has its own behavior
        composedSpec.Should().NotBeSatisfiedBy(cheapElectronics, "composed: price fails");
        composedSpec.Should().NotBeSatisfiedBy(expensiveClothing, "composed: category fails");
    }

    [Fact]
    public void MultipleCompositions_FromSameSpec_AreIndependent()
    {
        // Arrange
        var priceSpec = new PriceAboveSpec(50m);
        var andSpec = priceSpec.And(new CategorySpec("Electronics"));
        var orSpec = priceSpec.Or(new CategorySpec("Electronics"));

        var cheapElectronics = new Product("Cable", 5m, "Electronics");

        // Act & Assert — And and Or produce different results
        andSpec.Should().NotBeSatisfiedBy(cheapElectronics, "AND: price fails");
        orSpec.Should().BeSatisfiedBy(cheapElectronics, "OR: category passes");
    }

    #endregion
}