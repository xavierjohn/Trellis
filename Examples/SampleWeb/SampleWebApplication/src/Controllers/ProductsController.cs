namespace SampleWebApplication.Controllers;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
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

[ApiController]
[Route("[controller]")]
public class ProductsController(AppDbContext db) : ControllerBase
{
    // GET /products — paginated list with Specification filtering
    // Demonstrates: PartialContentResult (206), Content-Range header, Specification pattern
    [HttpGet]
    public async Task<ActionResult<ProductResponse[]>> GetAll(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool? inStock)
    {
        var pgSize = Math.Clamp(pageSize ?? 25, 1, 100);
        var pgNum = Math.Clamp(page ?? 0, 0, 10000);

        IQueryable<Product> query = db.Products;

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
            return Ok(Array.Empty<ProductResponse>());

        var products = await query
            .OrderBy(p => p.Name)
            .Skip(from)
            .Take(pgSize)
            .ToListAsync();

        var items = products.Select(ProductResponse.From).ToArray();

        // RFC 9110 §14: Return 206 Partial Content when not all items fit,
        // or 200 OK when the response contains the complete set.
        if (products.Count > 0 && products.Count < totalCount)
        {
            var to = from + products.Count - 1;
            return Result.Ok(items)
                .ToActionResult(this,
                    _ => new ContentRangeHeaderValue(from, to, totalCount) { Unit = "items" },
                    static r => r);
        }

        return Ok(items);
    }

    // GET /products/{id} — conditional GET with ETag
    // Demonstrates: If-None-Match → 304, RepresentationMetadata, strongly-typed route binding
    [HttpGet("{id}", Name = nameof(GetProduct))]
    public async Task<ActionResult<ProductResponse>> GetProduct(ProductId id)
    {
        var result = await db.Products
            .FirstOrDefaultResultAsync(p => p.Id == id,
                Error.NotFound("Product not found.", id));

        if (result.IsFailure)
            return result.Error.ToActionResult<ProductResponse>(this);

        return result.ToActionResult(this, product => RepresentationMetadata.WithStrongETag(product.ETag), ProductResponse.From);
    }

    // POST /products — create with ETag + Location
    // Demonstrates: 201 Created, ToCreatedAtActionResult with ETag metadata
    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request)
    {
        var result = await ProductName.TryCreate(request.Name)
            .Combine(MonetaryAmount.TryCreate(request.Price, "price"))
            .Bind((name, price) => Product.TryCreate(name, price, request.Stock))
            .Tap(product => db.Products.Add(product))
            .CheckAsync(_ => db.SaveChangesResultUnitAsync());

        if (result.IsFailure)
            return result.Error.ToActionResult<ProductResponse>(this);

        var product = result.Value;
        var response = ProductResponse.From(product);
        Response.Headers.ETag = $"\"{product.ETag}\"";
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id.Value }, response);
    }

    // PUT /products/{id} — update with If-Match + Prefer header
    // Demonstrates: OptionalETagAsync → 412/428, Prefer: return=minimal → 204
    [HttpPut("{id}")]
    public async Task<ActionResult<ProductResponse>> Update(ProductId id, [FromBody] UpdateProductRequest request)
    {
        var result = await db.Products
            .FirstOrDefaultResultAsync(p => p.Id == id,
                Error.NotFound("Product not found.", id))
            .OptionalETagAsync(ETagHelper.ParseIfMatch(Request))
            .BindAsync(p =>
                MonetaryAmount.TryCreate(request.Price, "price")
                    .Bind(price => p.UpdatePrice(price)))
            .CheckAsync(_ => db.SaveChangesResultUnitAsync());

        return result.ToUpdatedActionResult(
            this,
            p => RepresentationMetadata.WithStrongETag(p.ETag),
            ProductResponse.From);
    }

    // DELETE /products/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult<Unit>> Delete(ProductId id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null)
            return Error.NotFound("Product not found.", id).ToActionResult<Unit>(this);

        db.Products.Remove(product);
        var saveResult = await db.SaveChangesResultUnitAsync();
        return saveResult.Match(
            _ => (ActionResult<Unit>)new NoContentResult(),
            error => error.ToActionResult<Unit>(this));
    }

    // GET /products/legacy/{id} — redirect demo
    // Demonstrates: RFC 9110 §15.4.2 — 301 Moved Permanently
    [HttpGet("legacy/{id}")]
    public ActionResult LegacyRedirect(ProductId id) =>
        RedirectPermanent($"/products/{id.Value}");
}