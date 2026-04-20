namespace SampleMinimalApi.Workflows;

using SampleMinimalApi.Persistence;
using SampleUserLibrary;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// Application boundary for state-changing product use cases (axiom A10).
/// </summary>
public sealed class ProductWorkflow(IProductRepository products)
{
    private readonly IProductRepository _products = products;

    public Task<Result<Product>> CreateAsync(ProductName name, MonetaryAmount price, int stockQuantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(price);
        return Product.TryCreate(name, price, stockQuantity)
            .TapAsync(product => CommitAsync(product, cancellationToken));
    }

    public Task<Result<Product>> AdjustStockAsync(ProductId productId, int delta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productId);
        return _products.GetAsync(productId, cancellationToken)
            .BindAsync(product => Task.FromResult(product.AdjustStock(delta)))
            .TapAsync(product => CommitAsync(product, cancellationToken));
    }

    private Task<Result> CommitAsync(Product product, CancellationToken cancellationToken) =>
        _products.SaveAsync(product, cancellationToken);
}
