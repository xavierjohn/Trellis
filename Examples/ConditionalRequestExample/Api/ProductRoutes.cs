using ConditionalRequestExample.Data;
using ConditionalRequestExample.Domain;
using Trellis;
using Trellis.Asp;

namespace ConditionalRequestExample.Api;

public record CreateProductRequest(string? Name, decimal Price);
public record UpdateProductRequest(decimal Price);
public record ProductResponse(Guid Id, string Name, decimal Price, string ETag)
{
    public static ProductResponse From(Product product) =>
        new(product.Id.Value, product.Name.Value, product.Price, product.ETag);
}

/// <summary>
/// Demonstrates RFC 9110 with OPTIONAL If-Match — updates proceed without the header.
/// </summary>
public static class OptionalETagRoutes
{
    public static void MapOptionalETagRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/optional/products");

        group.MapGet("/{id:guid}", async (Guid id, ProductDbContext db, HttpContext httpContext) =>
        {
            var product = await db.Products.FindAsync(ProductId.Create(id));
            var result = product is not null
                ? Result.Success(product)
                : Result.Failure<Product>(Error.NotFound($"Product {id} not found"));

            return result.ToHttpResult(httpContext, p => p.ETag, ProductResponse.From);
        });

        group.MapPost("/", async (CreateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
        {
            var result = ProductName.TryCreate(request.Name, nameof(request.Name))
                .Bind(name => Product.TryCreate(name, request.Price));

            if (result.IsFailure)
                return result.Error.ToHttpResult();

            var product = result.Value;
            db.Products.Add(product);
            await db.SaveChangesAsync();

            httpContext.Response.Headers.ETag = $"\"{product.ETag}\"";
            return Results.Created($"/optional/products/{product.Id.Value}", ProductResponse.From(product));
        });

        // PUT — If-Match is OPTIONAL. Without it, update proceeds unconditionally.
        group.MapPut("/{id:guid}", async (Guid id, UpdateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
        {
            var ifMatchETags = ETagHelper.ParseIfMatch(httpContext.Request);

            var product = await db.Products.FindAsync(ProductId.Create(id));
            if (product is null)
                return Error.NotFound($"Product {id} not found").ToHttpResult();

            var result = product.ToResult()
                .OptionalETag(ifMatchETags)
                .Bind(p => p.UpdatePrice(request.Price));

            if (result.IsSuccess)
                await db.SaveChangesAsync();

            return result.ToHttpResult(httpContext, p => p.ETag, ProductResponse.From);
        });
    }
}

/// <summary>
/// Demonstrates RFC 9110 with REQUIRED If-Match — updates without the header get 428.
/// </summary>
public static class RequiredETagRoutes
{
    public static void MapRequiredETagRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/required/products");

        group.MapGet("/{id:guid}", async (Guid id, ProductDbContext db, HttpContext httpContext) =>
        {
            var product = await db.Products.FindAsync(ProductId.Create(id));
            var result = product is not null
                ? Result.Success(product)
                : Result.Failure<Product>(Error.NotFound($"Product {id} not found"));

            return result.ToHttpResult(httpContext, p => p.ETag, ProductResponse.From);
        });

        group.MapPost("/", async (CreateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
        {
            var result = ProductName.TryCreate(request.Name, nameof(request.Name))
                .Bind(name => Product.TryCreate(name, request.Price));

            if (result.IsFailure)
                return result.Error.ToHttpResult();

            var product = result.Value;
            db.Products.Add(product);
            await db.SaveChangesAsync();

            httpContext.Response.Headers.ETag = $"\"{product.ETag}\"";
            return Results.Created($"/required/products/{product.Id.Value}", ProductResponse.From(product));
        });

        // PUT — If-Match is REQUIRED. Without it -> 428 Precondition Required.
        group.MapPut("/{id:guid}", async (Guid id, UpdateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
        {
            var ifMatchETags = ETagHelper.ParseIfMatch(httpContext.Request);

            var product = await db.Products.FindAsync(ProductId.Create(id));
            if (product is null)
                return Error.NotFound($"Product {id} not found").ToHttpResult();

            var result = product.ToResult()
                .RequireETag(ifMatchETags)
                .Bind(p => p.UpdatePrice(request.Price));

            if (result.IsSuccess)
                await db.SaveChangesAsync();

            return result.ToHttpResult(httpContext, p => p.ETag, ProductResponse.From);
        });
    }
}