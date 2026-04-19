namespace SampleMinimalApiNoAot.API;

using Microsoft.EntityFrameworkCore;
using SampleDataAccess;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

public record ProductResponse(Guid Id, string Name, decimal Price, int Stock, string ETag)
{
    public static ProductResponse From(Product p) =>
        new(p.Id.Value, p.Name.Value, p.Price.Value, p.StockQuantity, p.ETag);
}

public record CreateProductRequest(string Name, decimal Price, int Stock);
public record UpdateProductRequest(decimal Price);

public static class ProductRoutes
{
    public static void UseProductRoute(this WebApplication app)
    {
        var productApi = app.MapGroup("/products");

        // GET /products — paginated list with Specification filtering
        // Demonstrates: PartialContentResult (206), Content-Range header, Specification pattern
        productApi.MapGet("/", async (
            AppDbContext db,
            int? page,
            int? pageSize,
            decimal? minPrice,
            decimal? maxPrice,
            bool? inStock) =>
        {
            var pgSize = Math.Clamp(pageSize ?? 25, 1, 100);
            var pgNum = Math.Clamp(page ?? 0, 0, 10000);

            IQueryable<Product> query = db.Products;

            // Apply Specification-based filtering
            if (inStock == true)
                query = query.Where(new InStockSpecification());

            if (minPrice.HasValue || maxPrice.HasValue)
            {
                var spec = new PriceRangeSpecification(
                    minPrice ?? 0m,
                    maxPrice ?? decimal.MaxValue);
                query = query.Where(spec);
            }

            var totalCount = await query.CountAsync();
            var from = pgNum * pgSize;

            // Handle page beyond data
            if (from >= totalCount && totalCount > 0)
                return Results.Ok(Array.Empty<ProductResponse>());

            var products = await query
                .OrderBy(p => p.Name)
                .Skip(from)
                .Take(pgSize)
                .ToListAsync();

            var items = products.Select(ProductResponse.From).ToArray();

            // RFC 9110 §14: Return 206 Partial Content when not all items fit,
            // or 200 OK when the response contains the complete set or empty page.
            if (items.Length == 0)
                return Results.Ok(items);

            var to = from + items.Length - 1;
            return Result.Ok(items).ToHttpResult(from, to, totalCount);
        });

        // GET /products/{id} — conditional GET with ETag
        // Demonstrates: If-None-Match → 304, RepresentationMetadata, strongly-typed route binding
        productApi.MapGet("/{id}", (ProductId id, AppDbContext db, HttpContext httpContext) =>
            db.Products
                .FirstOrDefaultResultAsync(p => p.Id == id,
                    Error.NotFound("Product not found.", id))
                .ToHttpResultAsync(httpContext, p => RepresentationMetadata.WithStrongETag(p.ETag), ProductResponse.From))
            .WithScalarValueValidation();

        // POST /products — create with ETag + Location
        // Demonstrates: 201 Created + ETag + Location
        productApi.MapPost("/", async (CreateProductRequest request, AppDbContext db, HttpContext httpContext) =>
            await ProductName.TryCreate(request.Name)
                .Combine(MonetaryAmount.TryCreate(request.Price, "price"))
                .Bind((name, price) => Product.TryCreate(name, price, request.Stock))
                .Tap(product => db.Products.Add(product))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .ToCreatedHttpResultAsync(httpContext,
                    p => $"/products/{p.Id.Value}",
                    p => RepresentationMetadata.WithStrongETag(p.ETag),
                    ProductResponse.From))
            .WithScalarValueValidation();

        // PUT /products/{id} — update with If-Match + Prefer header
        // Demonstrates: OptionalETagAsync → 412/428, Prefer: return=minimal → 204
        productApi.MapPut("/{id}", (ProductId id, UpdateProductRequest request, AppDbContext db, HttpContext httpContext) =>
            db.Products
                .FirstOrDefaultResultAsync(p => p.Id == id,
                    Error.NotFound("Product not found.", id))
                .OptionalETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
                .BindAsync(p =>
                    MonetaryAmount.TryCreate(request.Price, "price")
                        .Bind(price => p.UpdatePrice(price)))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .ToUpdatedHttpResultAsync(httpContext,
                    p => RepresentationMetadata.WithStrongETag(p.ETag),
                    ProductResponse.From))
            .WithScalarValueValidation();

        // DELETE /products/{id}
        productApi.MapDelete("/{id}", async (ProductId id, AppDbContext db) =>
        {
            var product = await db.Products.FindAsync(id);
            if (product is null)
                return Error.NotFound("Product not found.", id).ToHttpResult();

            db.Products.Remove(product);
            var saveResult = await db.SaveChangesResultUnitAsync();
            return saveResult.Match(
                _ => Results.NoContent(),
                error => error.ToHttpResult());
        }).WithScalarValueValidation();

        // GET /products/legacy/{id} — redirect demo
        // Demonstrates: RFC 9110 §15.4.2 — 301 Moved Permanently
        productApi.MapGet("/legacy/{id}", (ProductId id) =>
            Results.Redirect($"/products/{id.Value}", permanent: true))
            .WithScalarValueValidation();
    }
}