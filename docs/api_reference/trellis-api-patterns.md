# Trellis Patterns API Reference

**Package:** multiple (`Trellis.Results`, `Trellis.Asp`, `Trellis.EntityFrameworkCore`, `Trellis.Http`, `Trellis.Authorization`, `Trellis.Mediator`)  
**Namespace:** mixed  
**Purpose:** Documents the supported cross-package usage patterns Trellis builds around results, scalar values, ASP.NET integration, EF Core, HTTP clients, and mediator pipelines.

See also: [trellis-api-results.md](trellis-api-results.md), [trellis-api-asp.md](trellis-api-asp.md), [trellis-api-entityframeworkcore.md](trellis-api-entityframeworkcore.md).

---

## Pattern: Scalar value validation in ASP.NET Core

### Applicable APIs

| API | Exact Signature | Notes |
| --- | --- | --- |
| MVC setup | `public static IMvcBuilder AddScalarValueValidation(this IMvcBuilder builder)` | Use with `AddControllers()` |
| Combined setup | `public static IServiceCollection AddScalarValueValidation(this IServiceCollection services)` | Configures JSON validation for MVC and Minimal APIs, but not MVC model binding/filtering |
| Minimal API setup | `public static IServiceCollection AddScalarValueValidationForMinimalApi(this IServiceCollection services)` | Minimal APIs only |
| Middleware | `public static IApplicationBuilder UseScalarValueValidation(this IApplicationBuilder app)` | Must run before routing/body handling |
| Minimal API filter | `public static RouteHandlerBuilder WithScalarValueValidation(this RouteHandlerBuilder builder)` | Checks collected validation errors |

### Required ordering

| Rule | Required |
| --- | --- |
| Call `AddControllers().AddScalarValueValidation()` for MVC controllers | yes |
| Call `AddScalarValueValidationForMinimalApi()` for Minimal API body binding | yes |
| Call `UseScalarValueValidation()` before routing/endpoints | yes |
| Call `WithScalarValueValidation()` on Minimal API handlers that accept validated body models | yes |
| `MapScalarApiReference()` | **sample-app only, not a Trellis framework API** |
| `WithDocumentPerVersion()` | **does not exist in this workspace** |

### Compile-correct MVC example

```csharp
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddScalarValueValidation();

var app = builder.Build();

app.UseScalarValueValidation();
app.UseAuthorization();
app.MapControllers();
```

### Compile-correct Minimal API example

```csharp
using Trellis.Asp;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddScalarValueValidationForMinimalApi();

var app = builder.Build();

app.UseScalarValueValidation();

app.MapPost("/users", (CreateUserRequest request) => Results.Ok(request))
   .WithScalarValueValidation();

public sealed record CreateUserRequest(string Name);
```

---

## Pattern: Scalar value object contracts

### Applicable APIs

| Type | Exact Signature |
| --- | --- |
| `IScalarValue<TSelf, TPrimitive>` | `static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)` |
|  | `static abstract Result<TSelf> TryCreate(string? value, string? fieldName = null)` |
|  | `static virtual TSelf Create(TPrimitive value)` |
|  | `TPrimitive Value { get; }` |
| `IFormattableScalarValue<TSelf, TPrimitive>` | `static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` |

### Guidance

| Scenario | Use |
| --- | --- |
| Primitive input already parsed | `TryCreate(TPrimitive, fieldName)` |
| Raw string input | `TryCreate(string?, fieldName)` |
| Culture-aware parsing | `TryCreate(string?, provider, fieldName)` |
| Known-valid constant/test data | `Create(TPrimitive)` |

### Compile-correct example

```csharp
using Trellis;

public readonly record struct Quantity(int Value)
    : IScalarValue<Quantity, int>
{
    public static Result<Quantity> TryCreate(int value, string? fieldName = null) =>
        value <= 0
            ? Error.Validation("Quantity must be greater than zero.", fieldName ?? "quantity")
            : Result.Success(new Quantity(value));

    public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
        int.TryParse(value, out var parsed)
            ? TryCreate(parsed, fieldName)
            : Error.Validation("Quantity must be a valid integer.", fieldName ?? "quantity");
}
```

---

## Pattern: MVC controllers returning `ActionResult`

### Applicable APIs

| API | Exact Signature |
| --- | --- |
| Sync mapping | `ToActionResult(this Result<TValue> result, ControllerBase controllerBase)` |
| Async mapping | `public static Task<ActionResult<TValue>> ToActionResultAsync<TValue>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase)` |
| Created response | `public static Task<ActionResult<TValue>> ToCreatedAtActionResultAsync<TValue>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, string? controllerName = null)` |
| Created+mapped response | `public static Task<ActionResult<TOut>> ToCreatedAtActionResultAsync<TValue, TOut>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, Func<TValue, TOut> map, string? controllerName = null)` |

### Notes

| Fact | Value |
| --- | --- |
| `Result.Success()` returns | `Result<Unit>` |
| `Unit.Value` | **does not exist** |
| Explicit unit value when needed | `default(Unit)` or `new Unit()` |

### Compile-correct example

```csharp
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

[ApiController]
[Route("products")]
public sealed class ProductsController : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken) =>
        ProductName.TryCreate(request.Name)
            .BindAsync(name => CreateProductAsync(name, cancellationToken))
            .ToCreatedAtActionResultAsync(
                this,
                actionName: nameof(GetById),
                routeValues: p => new { id = p.Id },
                map: ProductResponse.From);

    [HttpGet("{id}")]
    public ActionResult<string> GetById(Guid id) => Ok(id.ToString());

    private static Task<Result<Product>> CreateProductAsync(ProductName name, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new Product(Guid.NewGuid(), name)));
}

public sealed record CreateProductRequest(string Name);
public sealed record Product(Guid Id, ProductName Name);
public sealed record ProductName(string Value)
{
    public static Result<ProductName> TryCreate(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Error.Validation("Name is required.", "name")
            : Result.Success(new ProductName(value));
}
public sealed record ProductResponse(Guid Id, string Name)
{
    public static ProductResponse From(Product product) => new(product.Id, product.Name.Value);
}
```

---

## Pattern: Minimal APIs returning `IResult`

### Applicable APIs

| API | Exact Signature |
| --- | --- |
| Standard mapping | `public static Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this Task<Result<TValue>> resultTask, TrellisAspOptions? options = null)` |
| Created-at-route mapping | `public static Task<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue>(this Task<Result<TValue>> resultTask, string routeName, Func<TValue, RouteValueDictionary> routeValues, TrellisAspOptions? options = null)` |
| Metadata-aware mapping | `public static Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)` |
| Created+metadata mapping | `public static Task<Microsoft.AspNetCore.Http.IResult> ToCreatedHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, string> uriSelector, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)` |
| Update+Prefer mapping | `ToUpdatedHttpResultAsync(...)` async overloads on `Result<T>`/`Task<Result<T>>` |

### Compile-correct example

```csharp
using Trellis;
using Trellis.Asp;

app.MapGet("/products/{id:guid}", (Guid id, HttpContext httpContext) =>
    LoadProductAsync(id)
        .ToHttpResultAsync(
            httpContext,
            p => RepresentationMetadata.WithStrongETag(p.ETag),
            ProductResponse.From));

static Task<Result<Product>> LoadProductAsync(Guid id) =>
    Task.FromResult(Result.Success(new Product(id, "sample-etag")));

public sealed record Product(Guid Id, string ETag);
public sealed record ProductResponse(Guid Id)
{
    public static ProductResponse From(Product product) => new(product.Id);
}
```

---

## Pattern: EF Core query and save helpers

### Applicable APIs

| API | Exact Signature | Notes |
| --- | --- | --- |
| Maybe query | `public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default) where T : class` | Neutral absence |
| Maybe query + predicate | `public static Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class` | Predicate overload |
| Result query | `public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken cancellationToken = default) where T : class` | Not-found is failure |
| Result query + predicate | `public static Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken cancellationToken = default) where T : class` | Predicate overload |
| Save count | `public static Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken cancellationToken = default)` | Count on success |
| Save unit | `public static Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)` | Maps success with `.Map(_ => default(Unit))` |
| Save unit + EF option | `public static Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)` | Explicit EF option |
| Convention scan | `public static ModelConfigurationBuilder ApplyTrellisConventions(this ModelConfigurationBuilder configurationBuilder, params Assembly[] assemblies)` | Pre-convention value-object registration |

### Compile-correct example

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis;
using Trellis.EntityFrameworkCore;

public static async Task<Result<Unit>> UpdateNameAsync(
    AppDbContext db,
    Guid id,
    string name,
    CancellationToken cancellationToken)
{
    return await db.Products
        .FirstOrDefaultResultAsync(
            p => p.Id == id,
            Error.NotFound("Product not found.", id.ToString()),
            cancellationToken)
        .Tap(product => product.Name = name)
        .CheckAsync(_ => db.SaveChangesResultUnitAsync(cancellationToken));
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

public sealed class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

---

## Pattern: HTTP client result pipelines

### Applicable APIs

| API | Exact Signature |
| --- | --- |
| Not found mapping | `public static Task<Result<HttpResponseMessage>> HandleNotFoundAsync(this Task<HttpResponseMessage> responseTask, NotFoundError notFoundError)` |
| Unauthorized mapping | `public static Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(this Task<HttpResponseMessage> responseTask, UnauthorizedError unauthorizedError)` |
| Forbidden mapping | `public static Task<Result<HttpResponseMessage>> HandleForbiddenAsync(this Task<HttpResponseMessage> responseTask, ForbiddenError forbiddenError)` |
| Conflict mapping | `public static Task<Result<HttpResponseMessage>> HandleConflictAsync(this Task<HttpResponseMessage> responseTask, ConflictError conflictError)` |
| Success enforcement | `EnsureSuccessAsync(...)` overloads on `Task<HttpResponseMessage>` |
| JSON materialization | `ReadResultFromJsonAsync(...)` overloads on `Result<HttpResponseMessage>` / `Task<Result<HttpResponseMessage>>` |

### Compile-correct example

```csharp
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trellis;
using Trellis.Http;

public static Task<Result<UserResponse>> GetUserAsync(HttpClient httpClient, Guid id, CancellationToken cancellationToken) =>
    httpClient.GetAsync($"/users/{id}", cancellationToken)
        .HandleNotFoundAsync(Error.NotFound("User not found.", id.ToString()))
        .EnsureSuccessAsync()
        .ReadResultFromJsonAsync(UserJsonContext.Default.UserResponse, cancellationToken);

public sealed record UserResponse(Guid Id, string Name);

[JsonSerializable(typeof(UserResponse))]
internal partial class UserJsonContext : JsonSerializerContext;
```

---

## Pattern: Mediator validation and authorization markers

### Applicable APIs

| Type | Exact Signature | Meaning |
| --- | --- | --- |
| `IAuthorize` | `IReadOnlyList<string> RequiredPermissions { get; }` | Current actor must satisfy all listed permissions |
| `IValidate` | `IResult Validate()` | Message validates itself before handler execution |

### Compile-correct example

```csharp
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record CreateInvoice(string Number, decimal Amount) : IAuthorize, IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["invoice.create"];

    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Number)
            ? Result.Failure(Error.Validation("Invoice number is required.", "number"))
            : Result.Ensure(Amount > 0m, Error.Validation("Amount must be positive.", "amount"));
}
```

---

## Pattern corrections from previous drafts

| Incorrect claim | Correct source-backed statement |
| --- | --- |
| `Unit.Value` exists | `Unit` is a `record struct`; use `Result.Success()` or `default(Unit)` |
| `WithDocumentPerVersion()` is part of Trellis | No such API exists in this workspace |
| `MapScalarApiReference()` is a Trellis API | It appears only in the sample MVC app setup |
| `UseScalarValueValidation()` can be placed anywhere | Register it before routing/endpoints that deserialize request bodies |
| `IAuthorize.RequiredPermissions` is mutable or loosely typed | Its type is `IReadOnlyList<string>` |
| `IValidate.Validate()` returns `Result<Unit>` | Its declared return type is `IResult` |

