# Aggregate Factory Pattern

## The Problem: How to Handle Both New and Existing Aggregates?

When working with DDD aggregates, you need **two different creation scenarios**:

1. **Creating NEW aggregates** - Generate fresh ID
2. **Reconstituting EXISTING aggregates** - Preserve existing ID (from database, tests, etc.)

If you only have one factory method that always generates a new ID, you **can't** load existing aggregates from the database!

## The Solution: Dual Factory Methods

### Pattern Overview

```csharp
public class Product : Aggregate<ProductId>
{
    // ✅ Pattern 1: Parameterless constructor for EF Core
    private Product() : base(null!) { }

    // ✅ Pattern 2: Private constructor accepting ID
    private Product(ProductId id, ...) : base(id) { }

    // ✅ Pattern 3: TryCreate for NEW aggregates (generates ID)
    public static Result<Product> TryCreate(...) =>
        // ... validation ...
        .Map(() => new Product(ProductId.NewUniqueV7(), ...));

    // ✅ Pattern 4: TryCreateExisting for EXISTING aggregates (accepts ID)
    public static Result<Product> TryCreateExisting(ProductId id, ...) =>
        // ... validation ...
        .Map(() => new Product(id, ...));

    // ✅ Pattern 5: Convenience methods that throw
    public static Product Create(...)
    {
        var result = TryCreate(...);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to create Product: {result.Error.Detail}");

        return result.Value;
    }

    public static Product CreateExisting(ProductId id, ...)
    {
        var result = TryCreateExisting(id, ...);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to create Product: {result.Error.Detail}");

        return result.Value;
    }
}
```

## When to Use Each Method

| Method | Use Case | ID Handling | Example |
|--------|----------|-------------|---------|
| `TryCreate` | Creating new domain objects | Generates new ID | `Product.TryCreate("Laptop", "SKU-001", 999.99m, "Electronics")` |
| `TryCreateExisting` | Loading from database, tests with known IDs | Accepts existing ID | `Product.TryCreateExisting(productId, "Laptop", "SKU-001", 999.99m, "Electronics")` |
| `Create` | Tests where validation should never fail | Generates new ID, throws | `var product = Product.Create("Laptop", "SKU-001", 999.99m, "Electronics")` |
| `CreateExisting` | Tests needing specific ID | Accepts existing ID, throws | `var product = Product.CreateExisting(knownId, "Laptop", "SKU-001", 999.99m, "Electronics")` |

## Real-World Examples

### Example 1: Creating a New Product (Domain Logic)

```csharp
// ✅ Use TryCreate - generates new ID
public async Task<Result<Product>> CreateProductAsync(ProductDto dto)
{
    return await Product.TryCreate(
            dto.Name,
            dto.Sku,
            dto.Price,
            dto.Category,
            dto.StockQuantity)
        .EnsureAsync(
            async p => !await _repository.SkuExistsAsync(p.Sku),
            Error.Conflict("SKU already exists"))
        .TapAsync(async p => await _repository.SaveAsync(p));
}
```

### Example 2: Loading from Database (EF Core)

```csharp
// ✅ EF Core uses parameterless constructor + property setters
var product = await _dbContext.Products
    .FirstOrDefaultAsync(p => p.Id == productId);
// EF Core reconstitutes: new Product() { Id = productId, Name = ..., etc. }
```

### Example 3: Testing with Known IDs

```csharp
[Fact]
public void Product_with_specific_id_for_testing()
{
    // ✅ Use CreateExisting in tests when you need a specific ID
    var knownId = ProductId.Create(Guid.Parse("12345678-1234-1234-1234-123456789abc"));
    
    var product = Product.CreateExisting(
        knownId,
        "Test Product",
        "TEST-SKU",
        99.99m,
        "Test Category");
    
    product.Id.Should().Be(knownId);
}
```

### Example 4: Updating an Existing Product

```csharp
// ✅ Load existing product, then update
public async Task<Result<Product>> UpdateProductAsync(ProductId id, UpdateProductDto dto)
{
    return await _repository.GetByIdAsync(id)  // Loads with existing ID
        .ToResultAsync(Error.NotFound($"Product {id} not found"))
        .Bind(product => product.UpdateDetails(dto.Name, dto.Price, dto.Category))
        .TapAsync(async product => await _repository.SaveAsync(product));
}
```

### Example 5: Manual Reconstitution (if not using EF Core)

```csharp
// ✅ Use TryCreateExisting when manually deserializing
public Result<Product> DeserializeProduct(ProductData data) =>
    Product.TryCreateExisting(
        data.Id,
        data.Name,
        data.Sku,
        data.Price,
        data.Category,
        data.StockQuantity,
        data.IsActive);
```

## Why Two Factory Methods?

### ❌ Anti-Pattern: Single Factory Always Generates ID

```csharp
// ❌ BAD: Can't load existing products!
public static Result<Product> TryCreate(string name, ...) =>
    // ...
    .Map(() => new Product(ProductId.NewUniqueV7(), ...));  // Always new ID!

// Problem 1: Can't load from database
var existingProduct = Product.TryCreate(dbData.Name, ...);  // ❌ Creates NEW ID!

// Problem 2: Can't test with known IDs
var testId = ProductId.Create(Guid.Parse("..."));
var product = Product.TryCreate(...);  // ❌ Generates random ID, can't use testId
```

### ✅ Correct Pattern: Dual Factory Methods

```csharp
// ✅ GOOD: Separate methods for different scenarios

// For creating NEW products
public static Result<Product> TryCreate(string name, ...) =>
    .Map(() => new Product(ProductId.NewUniqueV7(), ...));  // ✅ New ID

// For EXISTING products
public static Result<Product> TryCreateExisting(ProductId id, string name, ...) =>
    .Map(() => new Product(id, ...));  // ✅ Existing ID preserved
```

## Benefits

✅ **Type-safe** - Compiler ensures you provide an ID when needed  
✅ **Clear intent** - Method name tells you if ID is new or existing  
✅ **EF Core compatible** - Parameterless constructor for ORM  
✅ **Testable** - Can create products with specific IDs in tests  
✅ **Domain-driven** - `TryCreate` for business logic, `TryCreateExisting` for infrastructure  

## Summary

| Scenario | Method | Why |
|----------|--------|-----|
| **Creating new product in domain** | `TryCreate` | Business logic should generate IDs |
| **Loading from database** | EF Core parameterless constructor | ORM handles reconstitution |
| **Manual deserialization** | `TryCreateExisting` | Preserve existing ID from source |
| **Testing with known ID** | `CreateExisting` | Tests need predictable IDs |
| **Quick test setup** | `Create` | Tests where validation won't fail |

**Key Insight:** The `TryCreate` vs `TryCreateExisting` distinction mirrors the DDD principle that **aggregate identity is immutable**. New aggregates get new IDs; existing aggregates keep their IDs. 🎯
