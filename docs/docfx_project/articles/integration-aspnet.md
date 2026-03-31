# ASP.NET Core Integration

**Level:** Intermediate | **Time:** 20-30 min | **Prerequisites:** [Basics](basics.md)

Integrate Railway-Oriented Programming with ASP.NET Core using the **Trellis.Asp** package. This package provides extension methods to convert `Result<T>` to HTTP responses with automatic error-to-status-code mapping and Problem Details (RFC 7807) support.

> **Note:** This guide focuses on **ASP.NET Core integration only**. For validation, see [FluentValidation Integration](integration-fluentvalidation.md). For data access, see [Entity Framework Core Integration](integration-ef.md).

## Table of Contents

- [Installation](#installation)
- [What the Package Provides](#what-the-package-provides)
- [Scalar Value Auto-Validation](#scalar-value-auto-validation)
- [Optional Value Objects with Maybe](#optional-value-objects-with-maybe)
- [MVC Controllers](#mvc-controllers)
- [Minimal API](#minimal-api)
- [Automatic Error Mapping](#automatic-error-mapping)
- [Custom Error Responses](#custom-error-responses)
- [Pagination Support](#pagination-support)
- [Best Practices](#best-practices)

## Installation

```bash
dotnet add package Trellis.Asp
```

## Configuration

### Default Setup

Register Trellis ASP.NET Core services with default error-to-status-code mappings:

```csharp
builder.Services.AddTrellisAsp();
```

### Custom Error Mappings

Override any default mapping:

```csharp
builder.Services.AddTrellisAsp(options =>
{
    options.MapError<DomainError>(StatusCodes.Status400BadRequest); // override 422 → 400
    options.MapError<ConflictError>(StatusCodes.Status422UnprocessableEntity); // override 409 → 422
});
```

Teams that accept the defaults don't need to configure anything — `AddTrellisAsp()` with no options uses the standard mappings.

## What the Package Provides

The **Trellis.Asp** package provides extension methods to convert `Result<T>` to HTTP responses:

### Core Extension Methods

**For MVC Controllers:**
```csharp
ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller);
Task<ActionResult<T>> ToActionResultAsync<T>(this Task<Result<T>> resultTask, ControllerBase controller);

// Map domain type to DTO inline
ActionResult<TOut> ToActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controller, Func<TIn, TOut> map);
Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, TOut> map);

// 201 Created with Location header
ActionResult<T> ToCreatedAtActionResult<T>(this Result<T> result, ControllerBase controller,
    string actionName, Func<T, object?> routeValues, string? controllerName = null);

// Pagination support
ActionResult<T> ToActionResult<T>(
    this Result<T> result, 
    ControllerBase controller,
    long from, 
    long to, 
    long totalCount);
```

**For Minimal API:**
```csharp
IResult ToHttpResult<T>(this Result<T> result);
Task<IResult> ToHttpResultAsync<T>(this Task<Result<T>> resultTask);

// 201 Created with Location header (AOT-compatible)
IResult ToCreatedAtRouteHttpResult<T>(this Result<T> result,
    string routeName, Func<T, RouteValueDictionary> routeValues);
```

**What happens:**
- ✅ **Success**: Returns appropriate HTTP status (200 OK, 201 Created, 204 No Content)
- ❌ **Failure**: Converts error types to HTTP status codes with Problem Details format
- 📄 **Pagination**: Returns 206 Partial Content with Content-Range headers

## Scalar Value Auto-Validation

The **Trellis.Asp** package provides automatic validation for any type implementing `IScalarValue<TSelf, TPrimitive>`. This includes DDD value objects (like `ScalarValueObject<T>`) as well as custom implementations. This eliminates the need for manual `Result.Combine()` calls in controllers and works seamlessly with ASP.NET Core's model binding.

### Setup

Enable auto-validation by calling `AddScalarValueValidation()` in your `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddScalarValueValidation(); // Enable automatic validation!

var app = builder.Build();
app.MapControllers();
app.Run();
```

### How It Works

Types implementing `IScalarValue` are automatically validated during model binding:

```csharp
using Trellis;

// Define custom value objects (source generator adds IScalarValue automatically)
public partial class FirstName : RequiredString<FirstName> { }
public partial class CustomerId : RequiredGuid<CustomerId> { }

// Use in DTOs with both custom and built-in value objects
public record CreateUserDto
{
    public FirstName FirstName { get; init; } = null!;
    public EmailAddress Email { get; init; } = null!;
    public PhoneNumber Phone { get; init; } = null!;
    public Url? Website { get; init; }
    public Age Age { get; init; } = null!;
    public CountryCode Country { get; init; } = null!;
}

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create(CreateUserDto dto)
    {
        // If we reach here, dto is FULLY validated!
        // All 6 value objects were automatically validated:
        // - FirstName: non-empty string
        // - Email: RFC 5322 format
        // - Phone: E.164 format
        // - Website: valid HTTP/HTTPS URL (optional)
        // - Age: 0-150 range
        // - Country: ISO 3166-1 alpha-2 code
        
        var user = new User(dto.FirstName, dto.Email, dto.Phone, dto.Age, dto.Country);
        return Ok(user);
    }

    [HttpGet("{id}")]
    public IActionResult Get(CustomerId id) // Route parameter validated automatically!
    {
        var user = _repository.GetById(id);
        return Ok(user);
    }
}
```

### Validation Sources

Auto-validation works with all ASP.NET Core binding sources:

| Source | Example | Notes |
|--------|---------|-------|
| **Route Parameters** | `/users/{id}` | `CustomerId id` |
| **Query Strings** | `?country=US` | `CountryCode country` |
| **JSON Bodies** | `{ "email": "user@example.com" }` | `EmailAddress email` in DTO |
| **Form Data** | Form POST | Value objects in model |

### Error Responses

Invalid value objects automatically return 400 Bad Request with standard Problem Details:

**Request:**
```http
POST /api/users HTTP/1.1
Content-Type: application/json

{
  "firstName": "John",
  "email": "not-an-email",
  "phone": "555-1234",
  "website": "not-a-url",
  "age": 200,
  "country": "USA"
}
```

**Response:**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "email": ["Email address is not valid."],
    "phone": ["Phone number must be in E.164 format (e.g., +14155551234)."],
    "website": ["URL must be a valid absolute HTTP or HTTPS URL."],
    "age": ["Age is unrealistically high."],
    "country": ["Country code must be an ISO 3166-1 alpha-2 code."]
  }
}
```

### Benefits

- ✅ **No manual Result.Combine()** in controllers
- ✅ **Works with route parameters, query strings, form data, and JSON bodies**
- ✅ **Validation errors flow into ModelState automatically**
- ✅ **Standard ASP.NET Core validation infrastructure**
- ✅ **Compatible with [ApiController] attribute for automatic 400 responses**

### Combining with FluentValidation

Auto-validation for value objects works alongside FluentValidation for complex scenarios:

```csharp
public record CreateProductRequest
{
    public ProductName Name { get; init; } = null!;           // Auto-validated
    public Percentage DiscountRate { get; init; } = null!;     // Auto-validated (0-100)
    public Url? ProductUrl { get; init; }                       // Auto-validated (optional)
    public List<string> Tags { get; init; } = new();
}

// FluentValidation for business rules that span multiple fields
public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        // Value objects already validated by auto-validation
        
        // Add business rules
        RuleFor(x => x.Tags)
            .Must(tags => tags.Count <= 10)
            .WithMessage("Product cannot have more than 10 tags");
            
        RuleFor(x => x.DiscountRate)
            .Must(discount => discount.Value <= 50)
            .When(x => x.ProductUrl == null)
            .WithMessage("Discount cannot exceed 50% for products without a URL");
    }
}
```

See [FluentValidation Integration](integration-fluentvalidation.md) for more details.

## Optional Value Objects with Maybe

Value objects are non-null by design, but some properties are optional — a customer's phone number, a secondary email, a website URL. `Maybe<T>` expresses this optionality, and `AddScalarValueValidation()` automatically registers the components that make it work with JSON, model binding, and validation.

For background on why `Maybe<T>` exists and when to use it vs. `T?`, see [Why Maybe?](maybe-type.md).

### Using Maybe in DTOs

Declare optional value object properties as `Maybe<T>`:

```csharp
public record UpdateCustomerRequest(
    FirstName Name,
    Maybe<PhoneNumber> Phone,
    Maybe<Url> Website
);
```

### Three-State JSON Handling

`MaybeScalarValueJsonConverter` handles deserialization automatically:

| JSON Input | Deserialized As | HTTP Response |
|-----------|----------------|---------------|
| `"phone": "555-1234"` | `Maybe.From(PhoneNumber)` | Request continues |
| `"phone": null` or field absent | `Maybe<PhoneNumber>.None` | Request continues |
| `"phone": "invalid!"` | Validation error collected | 400 Bad Request |

This solves a problem that `T?` cannot: distinguishing "absent" from "invalid." With `PhoneNumber?`, a `null` result tells you nothing — was the field missing, or did it fail validation? With `Maybe<PhoneNumber>`, absence is `None` and invalid input is a validation error.

**Request with optional fields:**
```http
POST /api/customers HTTP/1.1
Content-Type: application/json

{
  "name": "John",
  "phone": null,
  "website": "https://example.com"
}
```

**Deserialized result:**
```csharp
// name = FirstName("John")
// phone = Maybe<PhoneNumber>.None   — null was intentional, not an error
// website = Maybe.From(Url("https://example.com"))
```

**Request with invalid optional field:**
```http
POST /api/customers HTTP/1.1
Content-Type: application/json

{
  "name": "John",
  "phone": "not-a-phone",
  "website": "not-a-url"
}
```

**Response:**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "phone": ["Phone number must be in E.164 format (e.g., +14155551234)."],
    "website": ["URL must be a valid absolute HTTP or HTTPS URL."]
  }
}
```

### Model Binding for Route and Query Parameters

`MaybeModelBinder` handles `Maybe<T>` in route parameters, query strings, form data, and headers:

```csharp
[HttpGet("search")]
public async Task<ActionResult<IEnumerable<CustomerDto>>> Search(
    [FromQuery] Maybe<PhoneNumber> phone,
    [FromQuery] Maybe<CountryCode> country,
    CancellationToken ct)
{
    // phone.HasNoValue → parameter was absent or empty
    // phone.HasValue   → parameter was present and valid
    return await _service.SearchAsync(phone, country, ct)
        .ToActionResultAsync(this);
}
```

| Query String | Binding Result |
|-------------|----------------|
| `/search` | `phone = None, country = None` |
| `/search?phone=+14155551234` | `phone = Maybe.From(PhoneNumber), country = None` |
| `/search?phone=invalid` | 400 Bad Request with validation error |

### Validation Safety

`MaybeSuppressChildValidationMetadataProvider` prevents ASP.NET Core's validation visitor from recursing into `Maybe<T>` properties. Without this, the validator would attempt to access `Maybe<T>.Value` via reflection, which throws `InvalidOperationException` when `HasNoValue` is true. This is registered automatically — no configuration needed.

### What Gets Registered

A single call to `AddScalarValueValidation()` registers all three components:

| Component | Purpose |
|-----------|--------|
| `MaybeScalarValueJsonConverter` | JSON: `null` → `None`, valid → `From`, invalid → error |
| `MaybeModelBinder` | Route/query/form: absent → `None`, valid → `From`, invalid → error |
| `MaybeSuppressChildValidationMetadataProvider` | Prevents validation crash on `Maybe<T>.None` |

No additional configuration is needed beyond what you've already set up for scalar value auto-validation.

## MVC Controllers

Use `ToActionResult` to convert `Result<T>` to `ActionResult<T>`:

### Simple Example

```csharp
using Trellis;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken ct)
        => await _userService.CreateUserAsync(request, ct)
            .MapAsync(user => new UserDto(user))
            .ToCreatedAtActionResultAsync(this,
                actionName: nameof(GetUser),
                routeValues: dto => new { id = dto.Id });
            // Success: 201 Created with Location header

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(
        string id,
        CancellationToken ct)
        => await _userService.GetUserByIdAsync(id, ct)
            .MapAsync(user => new UserDto(user))
            .ToActionResultAsync(this);

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDto>> UpdateUser(
        string id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
        => await _userService.UpdateUserAsync(id, request, ct)
            .MapAsync(user => new UserDto(user))
            .ToActionResultAsync(this);

    [HttpDelete("{id}")]
    public async Task<ActionResult<Unit>> DeleteUser(
        string id,
        CancellationToken ct)
        => await _userService.DeleteUserAsync(id, ct)
            .ToActionResultAsync(this);  // Returns 204 No Content on success
}
```

**Key Points:**
- Controller accepts requests and calls service layer
- Service returns `Result<T>` (success or failure)
- `ToActionResultAsync` converts `Result<T>` → `ActionResult<T>` at the API boundary
- Automatic error-to-HTTP status mapping (see [Automatic Error Mapping](#automatic-error-mapping))

> **Note:** The service layer (`IUserService`) can use any architecture you prefer. See [Examples](examples.md) for complete application examples with different architectural patterns.

## Minimal API

Use `ToHttpResult` for Minimal API endpoints:

```csharp
using Trellis;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

var app = builder.Build();

var userApi = app.MapGroup("/api/users")
    .WithTags("Users")
    .WithOpenApi();

userApi.MapPost("/", async (
    CreateUserRequest request,
    IUserService userService,
    CancellationToken ct) =>
    await userService.CreateUserAsync(request, ct)
        .MapAsync(user => new UserDto(user))
        .ToCreatedAtRouteHttpResultAsync(
            routeName: "GetUser",
            routeValues: dto => new RouteValueDictionary(new { id = dto.Id })))
    // Success: 201 Created with Location header
    .WithName("CreateUser")
    .Produces<UserDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status409Conflict);

userApi.MapGet("/{id}", async (
    string id,
    IUserService userService,
    CancellationToken ct) =>
    await userService.GetUserByIdAsync(id, ct)
        .MapAsync(user => new UserDto(user))
        .ToHttpResultAsync())
    .WithName("GetUser")
    .Produces<UserDto>()
    .ProducesProblem(StatusCodes.Status404NotFound);

userApi.MapPut("/{id}", async (
    string id,
    UpdateUserRequest request,
    IUserService userService,
    CancellationToken ct) =>
    await userService.UpdateUserAsync(id, request, ct)
        .MapAsync(user => new UserDto(user))
        .ToHttpResultAsync())
    .WithName("UpdateUser")
    .Produces<UserDto>()
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound);

userApi.MapDelete("/{id}", async (
    string id,
    IUserService userService,
    CancellationToken ct) =>
    await userService.DeleteUserAsync(id, ct)
        .ToHttpResultAsync())  // Returns 204 No Content on success
    .WithName("DeleteUser")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.Run();
```

## Automatic Error Mapping

The package automatically maps error types to HTTP status codes:

| Error Type | HTTP Status | Example Use Case |
|------------|-------------|------------------|
| (Success) | 200 OK | Resource retrieved or updated |
| (Success - Created) | 201 Created | Resource created with Location header |
| (Success - Unit) | 204 No Content | Delete or side-effect operation |
| `ValidationError` | 400 Bad Request | Invalid email format, required field missing |
| `BadRequestError` | 400 Bad Request | Malformed request, invalid query parameters |
| `UnauthorizedError` | 401 Unauthorized | Missing authentication token |
| `ForbiddenError` | 403 Forbidden | Insufficient permissions for action |
| `NotFoundError` | 404 Not Found | User not found, resource doesn't exist |
| `ConflictError` | 409 Conflict | Duplicate email, concurrent modification |
| `PreconditionFailedError` | 412 Precondition Failed | ETag mismatch on conditional request |
| `PreconditionRequiredError` | 428 Precondition Required | Missing required If-Match header |
| `DomainError` | 422 Unprocessable Entity | Business rule violation |
| `RateLimitError` | 429 Too Many Requests | API rate limit exceeded |
| `UnexpectedError` | 500 Internal Server Error | Database connection failed |
| `ServiceUnavailableError` | 503 Service Unavailable | Service under maintenance |
| `AggregateError` | Varies | Multiple errors (uses first error's status) |

**Key Features:**
- ✅ **Automatic Status Codes** - No manual mapping required
- ✅ **Problem Details (RFC 9457)** - Standard error response format
- ✅ **Validation Error Formatting** - Field-level errors
- ✅ **Unit Type Support** - `Result<Unit>` returns 204 No Content
- ✅ **Async Support** - Full async/await with `CancellationToken`
- ✅ **RFC 9110 ETag Support** - Conditional requests via `If-Match`/`If-None-Match`

### RFC 9110 — ETag Conditional Requests

Trellis provides `ToETagActionResult` overloads that set the `ETag` response header, and handle `If-None-Match` (304). Use `OptionalETag`/`RequireETag` in handlers to validate `If-Match` → 412 before saving.

**GET with ETag and If-None-Match (304):**
```csharp
[HttpGet("{id}")]
public async ValueTask<ActionResult<OrderResponse>> GetById(OrderId id, CancellationToken ct) =>
    await _sender.Send(new GetOrderByIdQuery(id), ct)
        .ToETagActionResultAsync(this, order => order.ETag, OrderResponse.From);
    // Sets ETag response header: ETag: "a1b2c3d4..."
    // If client sends If-None-Match: "a1b2c3d4..." → returns 304 Not Modified
```

**PUT with If-Match validation:**
```csharp
[HttpPut("{id}")]
public async ValueTask<ActionResult<OrderResponse>> Update(
    OrderId id,
    [FromBody] UpdateOrderRequest request,
    CancellationToken ct)
{
    var ifMatchETags = ETagHelper.ParseIfMatch(Request); // RFC 9110-compliant parsing
    return await UpdateOrderCommand.TryCreate(id, request.Amount, ifMatchETags)
        .BindAsync(command => _sender.Send(command, ct))
        .ToETagActionResultAsync(this, order => order.ETag, OrderResponse.From);
    // If ETag mismatch → 412 Precondition Failed
    // If race condition (DbUpdateConcurrencyException) → 409 Conflict
}
```

**Handler with OptionalETag (If-Match optional):**
```csharp
public async ValueTask<Result<Order>> Handle(UpdateOrderCommand command, CancellationToken ct) =>
    await _repo.GetByIdAsync(command.OrderId, ct)
        .OptionalETag(command.IfMatchETags)     // 412 if stale, skipped if null
        .BindAsync(order => order.Update(command.Amount))
        .CheckAsync(order => _repo.SaveAsync(order, ct));
```

**Handler with RequireETag (If-Match required — 428 if missing):**
```csharp
public async ValueTask<Result<Order>> Handle(UpdateOrderCommand command, CancellationToken ct) =>
    await _repo.GetByIdAsync(command.OrderId, ct)
        .RequireETag(command.IfMatchETags)      // 428 if null, 412 if stale
        .BindAsync(order => order.Update(command.Amount))
        .CheckAsync(order => _repo.SaveAsync(order, ct));
```

**POST with ETag on 201 Created:**
```csharp
[HttpPost]
public async ValueTask<ActionResult<OrderResponse>> Create(
    [FromBody] CreateOrderRequest request,
    CancellationToken ct) =>
    await _sender.Send(new CreateOrderCommand(request.CustomerId, request.Amount), ct)
        .ToCreatedAtETagActionResultAsync(this, nameof(GetById),
            r => new { id = r.Id }, r => r.ETag, OrderResponse.From);
    // Returns 201 Created + Location header + ETag header
```

### Example: Validation Error Response

**Request:**
```http
POST /api/users HTTP/1.1
Content-Type: application/json

{
  "email": "",
  "firstName": "John",
  "lastName": "",
  "age": 15
}
```

**Response:**
```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "User registration validation failed",
  "instance": "/api/users",
  "errors": {
    "email": ["Email is required"],
    "lastName": ["Last name is required"],
    "age": ["Must be 18 or older"]
  }
}
```

### Example: Not Found Error Response

**Request:**
```http
GET /api/users/12345 HTTP/1.1
```

**Response:**
```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "User 12345 not found",
  "instance": "/api/users/12345"
}
```

## Custom Error Responses

Use `MatchError` for custom error handling when you need more control than automatic mapping provides:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IOrderService orderService,
    CancellationToken ct) =>
{
    return await orderService.ProcessOrderAsync(request, ct)
        .MatchErrorAsync(
            onValidation: err => Results.BadRequest(new 
            { 
                message = "Validation failed",
                errors = err.FieldErrors
                    .ToDictionary(
                        f => f.FieldName, 
                        f => f.Details.ToArray())
            }),
            onNotFound: err => 
                Results.NotFound(new { message = err.Detail }),
            onConflict: err => 
                Results.Conflict(new { message = err.Detail }),
            onDomain: err =>
                Results.Problem(
                    detail: err.Detail,
                    statusCode: StatusCodes.Status422UnprocessableEntity),
            onSuccess: order => 
                Results.Created($"/orders/{order.Id}", order),
            cancellationToken: ct
        );
});
```

**Use `MatchError` when:**
- You need custom response payloads
- You want different error handling per endpoint
- You need to add custom headers or cookies
- Default Problem Details format doesn't fit your needs

**Use `ToActionResult`/`ToHttpResult` when:**
- Standard Problem Details format is sufficient
- You want consistent error responses across your API
- You don't need custom error logic per endpoint

## Pagination Support

The package provides built-in support for HTTP 206 Partial Content responses with Content-Range headers:

### Basic Pagination

```csharp
[HttpGet]
public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersAsync(
    [FromQuery] int page = 0,
    [FromQuery] int pageSize = 25,
    CancellationToken ct)
{
    var from = page * pageSize;
    var to = from + pageSize - 1;
    
    var result = await _userService.GetPagedUsersAsync(from, pageSize, ct);
    
    // Automatically returns:
    // - 200 OK if all items fit in one page (to >= totalCount - 1)
    // - 206 Partial Content with Content-Range header if partial results
    return result
        .Map(pagedData => (pagedData.Items, pagedData.TotalCount))
        .Map(x => x.Items.Select(u => new UserDto(u)))
        .ToActionResult(this, from, to, result.Value.TotalCount);
}
```

**Response (partial content):**
```http
HTTP/1.1 206 Partial Content
Content-Range: items 0-24/100
Content-Type: application/json

[
  { "id": "1", "email": "user1@example.com", ... },
  { "id": "2", "email": "user2@example.com", ... },
  ...
]
```

**Response (complete):**
```http
HTTP/1.1 200 OK
Content-Type: application/json

[
  { "id": "1", "email": "user1@example.com", ... },
  ...
]
```

### Advanced Pagination with Custom Range Extraction

```csharp
public record PagedResult<T>(
    IEnumerable<T> Items, 
    long From, 
    long To, 
    long TotalCount);

[HttpGet]
public ActionResult<IEnumerable<UserDto>> GetUsers(
    [FromQuery] int page = 0,
    [FromQuery] int pageSize = 25)
{
    return _userService
        .GetPagedUsers(page, pageSize)
        .ToActionResult(
            this,
            funcRange: pagedResult => new ContentRangeHeaderValue(
                pagedResult.From,
                pagedResult.To,
                pagedResult.TotalCount)
            {
                Unit = "items"
            },
            funcValue: pagedResult => pagedResult.Items.Select(u => new UserDto(u))
        );
}
```

## Best Practices

### 1. Convert at API Boundaries Only

Keep `Result<T>` types internal to your application. Convert to HTTP responses only at the controller/endpoint level.

```csharp
// ✅ Good - Result stays in application/domain layer
public class UserService
{
    public async Task<Result<User>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken ct)
    {
        return await EmailAddress.TryCreate(request.Email)
            .Combine(FirstName.TryCreate(request.FirstName))
            .BindAsync(async (email, first) => 
                await User.CreateAsync(email, first, ct), ct);
    }
}

[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(
    CreateUserRequest request,
    CancellationToken ct) =>
    await _userService.CreateUserAsync(request, ct)
        .MapAsync(user => new UserDto(user))
        .ToCreatedAtActionResultAsync(this,
            actionName: nameof(GetUser),
            routeValues: dto => new { id = dto.Id });  // ← Convert at boundary

// ❌ Bad - exposing Result in controller return type
public async Task<Result<User>> CreateUser(...)
```

### 2. Always Pass CancellationToken

Support graceful cancellation in async operations:

```csharp
[HttpPost]
public async Task<ActionResult<Order>> ProcessOrder(
    CreateOrderRequest request,
    CancellationToken ct)  // ← Accept CancellationToken
    => await _orderService.ProcessOrderAsync(request, ct)
        .ToActionResultAsync(this);
```

### 3. Use Unit for Side-Effect Operations

Operations that don't return data should return `Result<Unit>`:

```csharp
[HttpDelete("{id}")]
public async Task<ActionResult<Unit>> DeleteUser(
    string id,
    CancellationToken ct) =>
    await _userService.DeleteUserAsync(id, ct)
        .ToActionResultAsync(this);
// ✅ Automatically returns 204 No Content on success
```

### 4. Use Consistent Error Messages

Structure error messages with context for better Problem Details responses:

```csharp
// ✅ Good - includes context
Error.NotFound($"User {userId} not found", userId.ToString())
Error.Validation("Email format is invalid", "email")
Error.Conflict("Email already in use", $"email:{email}")

// ❌ Bad - generic, no context
Error.NotFound("Not found")
Error.Validation("Invalid")
```

### 5. Prefer Automatic Mapping Over Custom Logic

Use `ToActionResult`/`ToHttpResult` for consistent error responses, and `ToCreatedAtActionResult`/`ToCreatedAtRouteHttpResult` for POST endpoints that return 201 Created. Only use `MatchError` when you need custom logic:

```csharp
// ✅ Good - 201 Created with Location header
[HttpPost]
public async Task<ActionResult<User>> CreateUser(CreateUserRequest request, CancellationToken ct)
    => await _userService.CreateUserAsync(request, ct)
        .ToCreatedAtActionResultAsync(this,
            actionName: nameof(GetUser),
            routeValues: user => new { id = user.Id });

// ✅ Good - 200 OK for non-create operations
[HttpGet("{id}")]
public async Task<ActionResult<User>> GetUser(string id, CancellationToken ct)
    => await _userService.GetUserAsync(id, ct)
        .ToActionResultAsync(this);

// ⚠️ Use only when necessary - custom error handling
app.MapPost("/special-endpoint", async (request, service, ct) =>
    await service.ProcessAsync(request, ct)
        .MatchErrorAsync(
            onValidation: err => CustomValidationResponse(err),
            onSuccess: result => CustomSuccessResponse(result),
            cancellationToken: ct));
```

## Next Steps

- Learn about [FluentValidation Integration](integration-fluentvalidation.md) for validation before HTTP conversion
- See [Entity Framework Core Integration](integration-ef.md) for repository patterns that return `Result<T>`
- Review [Observability](integration-observability.md) for OpenTelemetry tracing and Problem Details correlation
- Check [Error Handling](error-handling.md) for working with different error types
- See [Examples](examples.md) for complete working applications
