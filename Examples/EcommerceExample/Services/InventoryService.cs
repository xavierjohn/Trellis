namespace EcommerceExample.Services;

using EcommerceExample.ValueObjects;
using Trellis;

/// <summary>
/// Manages product inventory with stock validation.
/// </summary>
public class InventoryService
{
    private readonly Dictionary<ProductId, int> _stock = [];

    public InventoryService()
    {
        // Initialize with sample data
        var product1 = ProductId.NewUniqueV4();
        var product2 = ProductId.NewUniqueV4();
        var product3 = ProductId.NewUniqueV4();

        _stock[product1] = 100;
        _stock[product2] = 50;
        _stock[product3] = 5;
    }

    /// <summary>
    /// Checks if sufficient stock is available for a product.
    /// </summary>
    public Result<Unit> CheckAvailability(ProductId productId, int quantity)
    {
        if (quantity <= 0)
            return Error.Validation("Quantity must be greater than zero", nameof(quantity));

        if (!_stock.TryGetValue(productId, out var available))
            return Error.NotFound($"Product {productId} not found in inventory");

        if (available < quantity)
            return Error.Validation($"Insufficient stock. Available: {available}, Requested: {quantity}");

        return Result.Ok();
    }

    /// <summary>
    /// Reserves stock for an order with Railway Oriented Programming.
    /// </summary>
    public async Task<Result<Unit>> ReserveStockAsync(ProductId productId, int quantity, CancellationToken cancellationToken = default)
    {
        await Task.Delay(100, cancellationToken); // Simulate async operation

        return CheckAvailability(productId, quantity)
            .Tap(_ =>
            {
                _stock[productId] -= quantity;
                Console.WriteLine($"Reserved {quantity} units of product {productId}. Remaining: {_stock[productId]}");
            });
    }

    /// <summary>
    /// Releases reserved stock if order is cancelled.
    /// </summary>
    public async Task<Result<Unit>> ReleaseStockAsync(ProductId productId, int quantity, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate async operation

        if (!_stock.TryGetValue(productId, out _))
            return Error.NotFound($"Product {productId} not found in inventory");

        _stock[productId] += quantity;
        Console.WriteLine($"Released {quantity} units of product {productId}. New total: {_stock[productId]}");

        return Result.Ok();
    }

    /// <summary>
    /// Gets current stock level for a product.
    /// </summary>
    public Result<int> GetStockLevel(ProductId productId)
    {
        if (!_stock.TryGetValue(productId, out var level))
            return Error.NotFound($"Product {productId} not found in inventory");

        return level;
    }

    public IReadOnlyDictionary<ProductId, int> GetAllStock() => _stock;
}