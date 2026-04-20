namespace SampleMinimalApi.Endpoints;

using Microsoft.AspNetCore.Routing;
using SampleMinimalApi.Models;
using SampleMinimalApi.Persistence;
using SampleMinimalApi.Workflows;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Product endpoints. Demonstrates Specification-driven querying and workflow-routed creation.
/// </summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        group.MapPost("/", (CreateProductDto dto, ProductWorkflow workflow, CancellationToken cancellationToken) =>
            workflow.CreateAsync(dto.Name, dto.Price, dto.StockQuantity, cancellationToken)
                .ToCreatedAtRouteHttpResultAsync(
                    "GetProduct",
                    product => new RouteValueDictionary { ["productId"] = product.Id.Value },
                    ProductResponse.From))
            .WithScalarValueValidation()
            .Produces<ProductResponse>(StatusCodes.Status201Created);

        group.MapGet("/{productId}", (ProductId productId, IProductRepository products, CancellationToken cancellationToken) =>
            products.GetAsync(productId, cancellationToken)
                .ToHttpResultAsync(ProductResponse.From))
            .WithName("GetProduct")
            .Produces<ProductResponse>();

        group.MapGet("/", async (
            IProductRepository products,
            CancellationToken cancellationToken,
            bool? inStock = null,
            decimal? minPrice = null,
            decimal? maxPrice = null) =>
        {
            var all = await products.ListAsync(cancellationToken).ConfigureAwait(false);
            var filtered = ApplySpecs(all, inStock, minPrice, maxPrice).Select(ProductResponse.From).ToList();
            return Results.Ok(filtered);
        })
            .Produces<IReadOnlyList<ProductResponse>>();

        return app;
    }

    private static IEnumerable<Product> ApplySpecs(
        IReadOnlyList<Product> source,
        bool? inStock,
        decimal? minPrice,
        decimal? maxPrice)
    {
        IEnumerable<Product> query = source;
        if (inStock == true)
        {
            var spec = new InStockSpecification();
            query = query.Where(spec.IsSatisfiedBy);
        }

        if (minPrice.HasValue || maxPrice.HasValue)
        {
            var spec = new PriceRangeSpecification(minPrice ?? decimal.MinValue, maxPrice ?? decimal.MaxValue);
            query = query.Where(spec.IsSatisfiedBy);
        }

        return query;
    }
}
