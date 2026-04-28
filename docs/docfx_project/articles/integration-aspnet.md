# ASP.NET Core Integration

**Level:** Intermediate | **Time:** 25-35 min | **Prerequisites:** [Basics](basics.md)

When your application already returns `Result<T>`, the next problem is predictable HTTP behavior: correct status codes, useful Problem Details responses, clean controller code, and support for web concerns like ETags, `Prefer`, and pagination. `Trellis.Asp` solves that boundary.

> [!TIP]
> Register `AddTrellisAsp()` even though Trellis has fallback defaults. It makes your HTTP mappings explicit and gives you one obvious place to customize them later.

## What this package gives you

`Trellis.Asp` is the ASP.NET Core adapter layer for Trellis.

It gives you:

- `ToHttpResponse(...)` for mapping `Result<T>` to HTTP responses in Minimal APIs and MVC
- `AsActionResult<T>()` for typed MVC `ActionResult<T>` signatures
- default error-type-to-status-code mappings
- Problem Details responses for failures
- automatic `204 No Content` for successful `Result`
- scalar value validation for MVC and Minimal APIs
- representation metadata support for headers like `ETag` and `Last-Modified`
- `Prefer`-aware update helpers
- partial-content helpers for paginated responses

## Quick start: MVC controllers

If you are using controllers, this is the smallest complete setup:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddScalarValueValidation();

builder.Services.AddTrellisAsp();

var app = builder.Build();
app.UseScalarValueValidation();
app.MapControllers();
app.Run();

public interface IUserService
{
    Task<Result<User>> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task<Result<User>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
}

public sealed record User(string Id, string Email);
public sealed record CreateUserRequest(string Email);
public sealed record UserResponse(string Id, string Email)
{
    public static UserResponse From(User user) => new(user.Id, user.Email);
}

[ApiController]
[Route("users")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet("{id}", Name = nameof(GetById))]
    public async Task<ActionResult<UserResponse>> GetById(string id, CancellationToken ct)
    {
        Result<User> result = await users.GetByIdAsync(id, ct);
        return result
            .ToHttpResponse(UserResponse.From)
            .AsActionResult<UserResponse>();
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct)
    {
        Result<User> result = await users.CreateAsync(request, ct);
        return result
            .ToHttpResponse(
                UserResponse.From,
                opts => opts.CreatedAtRoute(
                    nameof(GetById),
                    user => new RouteValueDictionary(new { id = user.Id })))
            .AsActionResult<UserResponse>();
    }
}
```

Why this works well:

- your service layer stays focused on domain results
- your controller only handles HTTP concerns
- success and failure paths stay visible

## Quick start: Minimal APIs

If you prefer Minimal APIs, use the Minimal API helpers instead:

```csharp
using Microsoft.AspNetCore.Routing;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrellisAsp();
builder.Services.AddScalarValueValidationForMinimalApi();

var app = builder.Build();
app.UseScalarValueValidation();

app.MapGet("/users/{id}", async (
    string id,
    IUserService users,
    CancellationToken ct) =>
    await users.GetByIdAsync(id, ct)
        .ToHttpResponseAsync(UserResponse.From))
    .WithName("GetUser");

app.MapPost("/users", async (
    CreateUserRequest request,
    IUserService users,
    CancellationToken ct) =>
    await users.CreateAsync(request, ct)
        .ToHttpResponseAsync(
            UserResponse.From,
            opts => opts.CreatedAtRoute(
                "GetUser",
                user => new RouteValueDictionary(new { id = user.Id }))));

app.Run();

public interface IUserService
{
    Task<Result<User>> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task<Result<User>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
}

public sealed record User(string Id, string Email);
public sealed record CreateUserRequest(string Email);
public sealed record UserResponse(string Id, string Email)
{
    public static UserResponse From(User user) => new(user.Id, user.Email);
}
```

## `AddTrellisAsp()` overloads

There are two registration styles:

```csharp
builder.Services.AddTrellisAsp();

builder.Services.AddTrellisAsp(options =>
{
    options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);
});
```

Use the parameterless overload when the defaults already match your API. Use the configured overload when you want to override specific mappings.

## Default error mapping

One of the biggest wins of `Trellis.Asp` is that you do not need a custom `switch` statement in every endpoint.

| Trellis error type | Default HTTP status |
| --- | --- |
| `Error.UnprocessableContent` | `422 Unprocessable Content` |
| `Error.BadRequest` | `400 Bad Request` |
| `Error.Unauthorized` | `401 Unauthorized` |
| `Error.Forbidden` | `403 Forbidden` |
| `Error.NotFound` | `404 Not Found` |
| `Error.MethodNotAllowed` | `405 Method Not Allowed` |
| `Error.NotAcceptable` | `406 Not Acceptable` |
| `Error.Conflict` | `409 Conflict` |
| `Error.Gone` | `410 Gone` |
| `Error.PreconditionFailed` | `412 Precondition Failed` |
| `Error.ContentTooLarge` | `413 Content Too Large` |
| `Error.UnsupportedMediaType` | `415 Unsupported Media Type` |
| `Error.RangeNotSatisfiable` | `416 Range Not Satisfiable` |
| `Error.PreconditionRequired` | `428 Precondition Required` |
| `Error.TooManyRequests` | `429 Too Many Requests` |
| `Error.InternalServerError` | `500 Internal Server Error` |
| `Error.Unexpected` | `500 Internal Server Error` |
| `Error.NotImplemented` | `501 Not Implemented` |
| `Error.ServiceUnavailable` | `503 Service Unavailable` |

> [!NOTE]
> Trellis error codes default to the error kind, such as `unprocessable-content` or `not-found`. Some payload-bearing cases expose a per-instance reason code instead.

## Problem Details output

Failures are returned as Problem Details responses, so clients get a standard shape instead of ad hoc JSON.

```http
HTTP/1.1 422 Unprocessable Content
Content-Type: application/problem+json

{
  "title": "One or more validation errors occurred.",
  "status": 422,
  "code": "unprocessable-content",
  "kind": "unprocessable-content",
  "errors": {
    "email": ["Email is required"]
  }
}
```

## Scalar value validation

This solves a common pain point: value objects are great in your domain, but raw ASP.NET Core model binding does not know how to validate them the way Trellis does.

### MVC setup

For controllers, use the MVC-specific registration:

```csharp
builder.Services
    .AddControllers()
    .AddScalarValueValidation();

var app = builder.Build();
app.UseScalarValueValidation();
app.MapControllers();
```

That registration adds:

- JSON converter support for scalar values
- model binders for route/query/form values
- a validation filter that returns proper validation responses

### Minimal API setup

For Minimal APIs, register JSON support, middleware, and the endpoint filter:

```csharp
using Trellis.Primitives;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScalarValueValidationForMinimalApi();

var app = builder.Build();
app.UseScalarValueValidation();

app.MapPost("/customers", (CreateCustomerRequest request) => Results.Ok(request))
    .WithScalarValueValidation();

app.Run();

public sealed record CreateCustomerRequest(EmailAddress Email, FirstName Name);
```

### Important distinction

`AddScalarValueValidation()` also exists on `IServiceCollection`, but that convenience overload only configures shared JSON support. It does **not** replace `AddControllers().AddScalarValueValidation()` for MVC apps.

## Optional value objects with `Maybe<T>`

`Maybe<T>` is useful when “missing” is valid but “present and invalid” should still fail the request.

```csharp
using Trellis;
using Trellis.Primitives;

public sealed record UpdateCustomerRequest(
    FirstName Name,
    Maybe<PhoneNumber> Phone,
    Maybe<Url> Website);
```

With scalar value validation enabled:

- omitted or `null` optional values become `Maybe<T>.None`
- valid values become `Maybe.From(value)`
- invalid values produce a validation error instead of silently becoming `null`

## Conditional requests: ETags and concurrency

This solves the “lost update” problem and lets clients cache responses safely.

### GET with representation metadata

Use representation metadata to emit response headers such as `ETag`.

```csharp
using Trellis;
using Trellis.Asp;

app.MapGet("/products/{id:guid}", (Guid id, ProductDbContext db) =>
    db.Products
        .FirstOrDefaultResultAsync(
            p => p.Id == ProductId.Create(id),
            new Error.NotFound(new ResourceRef("Resource", id.ToString())) { Detail = "Product not found." })
        .ToHttpResponseAsync(
            ProductResponse.From,
            opts => opts
                .WithETag(product => product.ETag)
                .EvaluatePreconditions()));

public sealed record ProductResponse(Guid Id, string Name, decimal Price, string ETag)
{
    public static ProductResponse From(Product product) =>
        new(product.Id.Value, product.Name.Value, product.Price.Value, product.ETag);
}
```

If the client sends a matching `If-None-Match`, the response is automatically shortened to `304 Not Modified`.

### PUT with `If-Match`

`ETagHelper.ParseIfMatch(request)` returns `EntityTagValue[]?`, and that typed value flows directly into Trellis concurrency helpers.

```csharp
using Trellis;
using Trellis.Asp;
using Trellis.Primitives;

app.MapPut("/products/{id:guid}", (Guid id, UpdateProductRequest request, ProductDbContext db, HttpContext httpContext) =>
    db.Products
        .FirstOrDefaultResultAsync(
            p => p.Id == ProductId.Create(id),
            new Error.NotFound(new ResourceRef("Resource", id.ToString())) { Detail = "Product not found." })
        .OptionalETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
        .BindAsync(product => product.UpdatePrice(request.Price))
        .CheckAsync(_ => db.SaveChangesResultUnitAsync())
        .MapAsync(product => (WriteOutcome<Product>)new WriteOutcome<Product>.Updated(product))
        .ToHttpResponseAsync(
            ProductResponse.From,
            opts => opts
                .WithETag(product => product.ETag)
                .HonorPrefer()));

public sealed record UpdateProductRequest(MonetaryAmount Price);
public sealed record ProductResponse(Guid Id, string Name, decimal Price, string ETag)
{
    public static ProductResponse From(Product product) =>
        new(product.Id.Value, product.Name.Value, product.Price.Value, product.ETag);
}
```

Use:

- `OptionalETag(...)` when `If-Match` is optional
- `RequireETag(...)` when missing `If-Match` should fail with `428 Precondition Required`

### Create-if-absent with `If-None-Match`

For “only create if this resource does not already exist” flows, use `ParseIfNoneMatch(...)` and `EnforceIfNoneMatchPrecondition(...)`.

```csharp
var ifNoneMatch = ETagHelper.ParseIfNoneMatch(httpContext.Request); // EntityTagValue[]?
var guarded = result.EnforceIfNoneMatchPrecondition(ifNoneMatch);
```

> [!NOTE]
> `EnforceIfNoneMatchPrecondition(...)` takes `EntityTagValue[]?`, not `string[]`.

## `Prefer` header support

Sometimes a client wants the updated representation back. Sometimes it only wants confirmation that the write succeeded. Trellis supports both without forcing you to hand-roll header parsing.

```csharp
using Trellis;
using Trellis.Asp;

app.MapPut("/orders/{id:guid}", async (
    Guid id,
    UpdateOrderRequest request,
    IOrderService orders,
    CancellationToken ct) =>
    await orders.UpdateAsync(id, request, ct)
        .MapAsync(order => (WriteOutcome<Order>)new WriteOutcome<Order>.Updated(order))
        .ToHttpResponseAsync(
            OrderResponse.From,
            opts => opts
                .WithETag(order => order.ETag)
                .HonorPrefer()));
```

Behavior:

- `Prefer: return=minimal` → `204 No Content`
- `Prefer: return=representation` → `200 OK` with a body
- `Preference-Applied` is emitted when Trellis honors the preference

If you need raw access to the parsed header:

```csharp
var prefer = PreferHeader.Parse(httpContext.Request);

if (prefer.ReturnMinimal)
{
    // client asked for a minimal response
}
```

> [!NOTE]
> `PreferHeader.HasPreferences` means “at least one recognized standard preference was parsed.” Unknown tokens do not set it.

## Pagination and partial content

For paged item collections, Trellis can return `206 Partial Content` with a `Content-Range` header.

```csharp
app.MapGet("/products", async (ProductDbContext db, int? page, int? pageSize) =>
{
    var size = Math.Clamp(pageSize ?? 25, 1, 100);
    var number = Math.Max(page ?? 0, 0);
    var from = number * size;

    var total = await db.Products.CountAsync();
    var items = await db.Products
        .OrderBy(p => p.Name)
        .Skip(from)
        .Take(size)
        .Select(ProductResponse.From)
        .ToArrayAsync();

    if (items.Length == 0)
        return Results.Ok(items);

    var to = from + items.Length - 1;
    return Result.Ok(items).ToHttpResponse(opts => opts.WithRange(from, to, total));
});
```

> [!NOTE]
> `RangeRequestEvaluator` is the lower-level RFC 9110 byte-range helper. It intentionally returns `FullRepresentation` for many cases: non-`GET` requests, missing `Range`, unsupported units, empty ranges, multiple ranges, and malformed single ranges.

## When to customize the response yourself

The built-in mappers are the default choice. Reach for custom matching only when the endpoint genuinely needs a custom payload shape.

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IOrderService orders,
    CancellationToken ct) =>
    await orders.CreateAsync(request, ct).MatchAsync(
        onSuccess: order => Results.Created($"/orders/{order.Id}", order),
        onFailure: error => error switch
        {
            Error.UnprocessableContent uc => Results.UnprocessableEntity(new
            {
                message = "Validation failed",
                errors = uc.Fields.Items
                    .GroupBy(v => v.Field.Path)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.Detail ?? v.ReasonCode).ToArray())
            }),
            Error.Conflict c              => Results.Conflict(new { message = c.Detail }),
            _                              => Results.StatusCode(StatusCodes.Status500InternalServerError)
        },
        cancellationToken: ct));
```

Use this approach when you need:

- a non-Problem-Details error body
- endpoint-specific payload shapes
- extra headers or cookies beyond the standard helpers

## Best practices

1. **Convert at the API boundary only.** Keep `Result<T>` in your application layer.
2. **Use MVC-specific or Minimal-API-specific validation setup.** Do not rely on the shared convenience overload alone for MVC.
3. **Use `Result` for side-effect operations.** Trellis maps successful no-payload results to `204 No Content`.
4. **Prefer typed ETag helpers.** `ParseIfMatch(...)` and `ParseIfNoneMatch(...)` return `EntityTagValue[]?`, which matches the concurrency APIs.
5. **Use representation metadata instead of hand-writing headers** when you want `ETag`, `Last-Modified`, `Vary`, or related response metadata.

## Next steps

- Add [FluentValidation Integration](integration-fluentvalidation.md) for richer business validation
- Add [ASP.NET Core Authorization](integration-asp-authorization.md) when claims need to become an `Actor`
- Add [Entity Framework Core Integration](integration-ef.md) if your handlers persist aggregates or read models
