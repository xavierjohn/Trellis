using ConditionalRequestExample.Data;
using ConditionalRequestExample.Domain;
using Trellis;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

namespace ConditionalRequestExample.Api;

public record CreateProductRequest(ProductName Name, MonetaryAmount Price);
public record UpdateProductRequest(MonetaryAmount Price);
public record ProductResponse(Guid Id, string Name, decimal Price, string ETag)
{
    public static ProductResponse From(Product product) =>
        new(product.Id.Value, product.Name.Value, product.Price.Value, product.ETag);
}

/// <summary>
/// Demonstrates RFC 9110 with OPTIONAL If-Match — updates proceed without the header.
/// Uses RepresentationMetadata for response headers and ConditionalRequestEvaluator for precondition checks.
/// </summary>
public static class OptionalETagRoutes
{
    public static void MapOptionalETagRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/optional/products");

        // GET — returns ETag + Last-Modified headers; supports If-None-Match -> 304 Not Modified
        group.MapGet("/{id:guid}", (Guid id, ProductDbContext db, HttpContext httpContext) =>
            db.Products
                .FirstOrDefaultResultAsync(p => p.Id == ProductId.Create(id), new Error.NotFound(new ResourceRef("Resource", id.ToString()?.ToString())) { Detail = "Product not found." })
                .ToHttpResultAsync(httpContext, p => RepresentationMetadata.WithStrongETag(p.ETag), ProductResponse.From));

        // POST — creates product, returns 201 Created + ETag
        group.MapPost("/", async (CreateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
            await Product.TryCreate(request.Name, request.Price)
                .Tap(product => db.Products.Add(product))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .ToCreatedHttpResultAsync(httpContext,
                    p => $"/optional/products/{p.Id.Value}",
                    p => RepresentationMetadata.WithStrongETag(p.ETag),
                    ProductResponse.From))
            .WithScalarValueValidation();

        // PUT — If-Match is OPTIONAL. Without it, update proceeds unconditionally.
        // Uses typed EntityTagValue[] via ParseIfMatch.
        group.MapPut("/{id:guid}", (Guid id, UpdateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
            db.Products
                .FirstOrDefaultResultAsync(p => p.Id == ProductId.Create(id), new Error.NotFound(new ResourceRef("Resource", id.ToString()?.ToString())) { Detail = "Product not found." })
                .OptionalETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
                .BindAsync(p => p.UpdatePrice(request.Price))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .ToHttpResultAsync(httpContext, p => RepresentationMetadata.WithStrongETag(p.ETag), ProductResponse.From))
            .WithScalarValueValidation();
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

        // GET — returns ETag header; supports If-None-Match -> 304 Not Modified
        group.MapGet("/{id:guid}", (Guid id, ProductDbContext db, HttpContext httpContext) =>
            db.Products
                .FirstOrDefaultResultAsync(p => p.Id == ProductId.Create(id), new Error.NotFound(new ResourceRef("Resource", id.ToString()?.ToString())) { Detail = "Product not found." })
                .ToHttpResultAsync(httpContext, p => RepresentationMetadata.WithStrongETag(p.ETag), ProductResponse.From));

        // POST — creates product, returns 201 Created + ETag
        group.MapPost("/", async (CreateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
            await Product.TryCreate(request.Name, request.Price)
                .Tap(product => db.Products.Add(product))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .ToCreatedHttpResultAsync(httpContext,
                    p => $"/required/products/{p.Id.Value}",
                    p => RepresentationMetadata.WithStrongETag(p.ETag),
                    ProductResponse.From))
            .WithScalarValueValidation();

        // PUT — If-Match is REQUIRED. Without it -> 428 Precondition Required.
        // Uses typed EntityTagValue[] via ParseIfMatch.
        group.MapPut("/{id:guid}", (Guid id, UpdateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
            db.Products
                .FirstOrDefaultResultAsync(p => p.Id == ProductId.Create(id), new Error.NotFound(new ResourceRef("Resource", id.ToString()?.ToString())) { Detail = "Product not found." })
                .RequireETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
                .BindAsync(p => Task.FromResult(p.UpdatePrice(request.Price)))
                .CheckAsync(_ => db.SaveChangesResultUnitAsync())
                .ToHttpResultAsync(httpContext, p => RepresentationMetadata.WithStrongETag(p.ETag), ProductResponse.From))
            .WithScalarValueValidation();
    }
}