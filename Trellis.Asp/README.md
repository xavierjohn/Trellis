# Trellis.Asp — ASP.NET Core Extensions

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.svg)](https://www.nuget.org/packages/Trellis.Asp)

Comprehensive ASP.NET Core integration for functional domain-driven design, providing:

1. **Automatic Scalar Value Validation** - Property-aware error messages with comprehensive error collection
2. **Result-to-HTTP Conversion** - Seamless `Result<T>` to HTTP response mapping
3. **Model Binding** - Automatic binding from route/query/form/headers
4. **Optional Value Objects** - `Maybe<T>` support for JSON, model binding, and MVC validation
5. **Native AOT Support** - Optional source generator for zero-reflection overhead

## Table of Contents

- [Installation](#installation)
- [Scalar Value Validation](#scalar-value-validation)
- [Quick Start](#quick-start)
  - [MVC Controllers](#mvc-controllers)
  - [Minimal APIs](#minimal-apis)
  - [Model Binding](#model-binding)
  - [Optional Value Objects with Maybe<T>](#optional-value-objects-with-maybet)
  - [Native AOT](#native-aot-support)
- [Result Conversion](#result-conversion)
  - [MVC Controllers](#result-conversion-mvc)
  - [Minimal APIs](#result-conversion-minimal-api)
- [Advanced Topics](#advanced-topics)
- [Best Practices](#best-practices)
- [Examples](#examples)
- [Related Packages](#related-packages)
- [License](#license)

## Installation

```bash
dotnet add package Trellis.Asp
```

## Scalar Value Validation

Automatically validate types implementing `IScalarValue<TSelf, TPrimitive>` during JSON deserialization and model binding with property-aware error messages.

> **Note:** This includes DDD value objects (like `ScalarValueObject<T>`) as well as any custom implementations of `IScalarValue`.

### Quick Start

**1. Define Value Objects**

Value objects that implement `IScalarValue` get automatic validation:

```csharp
public class EmailAddress : ScalarValueObject<EmailAddress, string>,
                            IScalarValue<EmailAddress, string>
{
    private EmailAddress(string value) : base(value) { }

    public static Result<EmailAddress> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "email";
        if (string.IsNullOrWhiteSpace(value))
            return Error.Validation("Email is required.", field);
        if (!value.Contains('@'))
            return Error.Validation("Email must contain @.", field);
        return new EmailAddress(value);
    }
}
```

**2. Use in DTOs**

```csharp
public record RegisterUserDto
{
    public EmailAddress Email { get; init; } = null!;
    public FirstName FirstName { get; init; } = null!;
    public string Password { get; init; } = null!;
}
```

**3. Setup Validation**

```csharp
var builder = WebApplication.CreateBuilder(args);

// For MVC Controllers
builder.Services
    .AddControllers()
    .AddScalarValueValidation();

// For Minimal APIs
builder.Services.AddScalarValueValidationForMinimalApi();

var app = builder.Build();
app.UseScalarValueValidation();  // Required middleware
app.Run();
```

**4. Automatic Validation**

```csharp
[HttpPost]
public IActionResult Register(RegisterUserDto dto)
{
    // If we reach here, dto is fully validated!
    return Ok(User.Create(dto.Email, dto.FirstName, dto.Password));
}
```

**Request:**
```json
{
  "email": "invalid",
  "firstName": "",
  "password": "test"
}
```

**Response (400 Bad Request):**
```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Email": ["Email must contain @."],
    "FirstName": ["Name cannot be empty."]
  }
}
```

### MVC Controllers

Full integration with MVC model binding and validation:

```csharp
builder.Services
    .AddControllers()
    .AddScalarValueValidation();  // Adds JSON validation + model binding

var app = builder.Build();
app.UseScalarValueValidation();  // Middleware
app.MapControllers();
app.Run();
```

**Features:**
- JSON deserialization with validation
- Model binding from route/query/form/headers
- Automatic 400 responses via `ScalarValueValidationFilter`
- Integrates with `[ApiController]` attribute

### Minimal APIs

Endpoint-specific validation with filters:

```csharp
builder.Services.AddScalarValueValidationForMinimalApi();

var app = builder.Build();
app.UseScalarValueValidation();

app.MapPost("/users", (RegisterUserDto dto) => ...)
   .WithScalarValueValidation();  // Add filter to each endpoint

app.Run();
```

**Features:**
- JSON deserialization with validation
- Endpoint filter for automatic 400 responses
- No automatic model binding (use JSON body)

### Model Binding

Value objects automatically bind from various sources in MVC:

```csharp
// Route parameters
[HttpGet("{userId}")]
public IActionResult GetUser(UserId userId) => Ok(user);

// Query parameters
[HttpGet]
public IActionResult Search([FromQuery] EmailAddress email) => Ok(results);

// Form data
[HttpPost]
public IActionResult Login([FromForm] EmailAddress email, [FromForm] string password) => Ok();

// Headers
[HttpGet]
public IActionResult GetProfile([FromHeader(Name = "X-User-Id")] UserId userId) => Ok();
```

### Native AOT Support

For Native AOT applications, add the source generator:

**1. Add Generator Reference**

```xml
<ItemGroup>
  <ProjectReference Include="path/to/AspSourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**2. Mark Your JsonSerializerContext**

```csharp
[GenerateScalarValueConverters]  // ← Add this
[JsonSerializable(typeof(RegisterUserDto))]
[JsonSerializable(typeof(User))]
public partial class AppJsonSerializerContext : JsonSerializerContext { }
```

**3. Configure**

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
```

The generator automatically:
- Detects all types implementing `IScalarValue`
- Generates AOT-compatible converters
- Adds `[JsonSerializable]` attributes
- Enables Native AOT with `<PublishAot>true</PublishAot>`

**Note:** The source generator is **optional**. Without it, the library uses reflection (works for standard .NET). See [docs/REFLECTION-FALLBACK.md](docs/REFLECTION-FALLBACK.md) for details.

### Optional Value Objects with Maybe<T>

Use `Maybe<T>` for optional value object properties in DTOs. No additional setup is needed — `AddScalarValueValidation()` automatically registers the JSON converter, model binder, and validation suppression for `Maybe<T>` properties.

#### JSON Deserialization

| JSON Value | Result |
|-----------|--------|
| `null` or absent | `Maybe.None` (no error) |
| Valid value | `Maybe.From(validated)` |
| Invalid value | Validation error collected |

```csharp
public record RegisterUserDto
{
    public FirstName FirstName { get; init; } = null!;   // Required
    public EmailAddress Email { get; init; } = null!;     // Required
    public Maybe<Url> Website { get; init; }              // Optional
}
```

**Request with optional value:**
```json
{ "firstName": "Jane", "email": "jane@example.com", "website": "https://jane.dev" }
```
→ `Website` = `Maybe.From(Url)`

**Request without optional value:**
```json
{ "firstName": "Jane", "email": "jane@example.com", "website": null }
```
→ `Website` = `Maybe.None`

**Request with invalid optional value:**
```json
{ "firstName": "Jane", "email": "jane@example.com", "website": "not-a-url" }
```
→ 400 with `"Website": ["Url is not valid."]`

#### MVC Model Binding

`Maybe<T>` properties bind from route, query, form, and header sources:

| Source Value | Result |
|-------------|--------|
| Absent / empty | `Maybe.None` (success) |
| Valid | `Maybe.From(result)` |
| Invalid | ModelState error |

```csharp
[HttpPut("{orderId}")]
public IActionResult Update(OrderId orderId, [FromBody] UpdateOrderDto dto)
{
    // dto.AssignedTo is Maybe<FirstName>
    // Absent → Maybe.None, valid → Maybe.From(name), invalid → 400
    return Ok();
}
```

#### MVC Validation Suppression

`MaybeSuppressChildValidationMetadataProvider` prevents MVC from requiring child properties on `Maybe<T>` structs. This is registered automatically by `AddScalarValueValidation()`.

Without it, MVC's `HasValidators` check would throw a `NullReferenceException` when encountering a `Maybe<T>` with no value, because `Maybe<T>` is a struct whose `Value` property returns `default` when `HasNoValue` is true.

## Result Conversion

Convert Railway Oriented Programming `Result<T>` types to HTTP responses.

### Result Conversion: MVC

Use `ToActionResult` to convert `Result<T>` to `ActionResult<T>`:

```csharp
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public ActionResult<User> Register([FromBody] RegisterRequest request) =>
        FirstName.TryCreate(request.FirstName)
            .Combine(LastName.TryCreate(request.LastName))
            .Combine(EmailAddress.TryCreate(request.Email))
            .Bind((firstName, lastName, email) =>
                User.TryCreate(firstName, lastName, email, request.Password))
            .ToCreatedAtActionResult(this,
                actionName: nameof(GetUserAsync),
                routeValues: user => new { id = user.Id });

    [HttpGet("{id}")]
    public async Task<ActionResult<User>> GetUserAsync(
        string id,
        CancellationToken cancellationToken) =>
        await _userRepository.GetByIdAsync(id, cancellationToken)
            .ToResultAsync(Error.NotFound($"User {id} not found"))
            .ToActionResultAsync(this);
}
```

### Result Conversion: Minimal API

Use `ToHttpResult` to convert `Result<T>` to `IResult`:

```csharp
var userApi = app.MapGroup("/api/users");

userApi.MapPost("/register", (RegisterUserRequest request) =>
    FirstName.TryCreate(request.FirstName)
        .Combine(LastName.TryCreate(request.LastName))
        .Combine(EmailAddress.TryCreate(request.Email))
        .Bind((firstName, lastName, email) =>
            User.TryCreate(firstName, lastName, email, request.Password))
        .ToCreatedAtRouteHttpResult(
            routeName: "GetUser",
            routeValues: user => new RouteValueDictionary(new { id = user.Id })));

userApi.MapGet("/{id}", async (
    string id,
    UserRepository repository,
    CancellationToken cancellationToken) =>
    await repository.GetByIdAsync(id, cancellationToken)
        .ToResultAsync(Error.NotFound($"User {id} not found"))
        .ToHttpResultAsync())
    .WithName("GetUser");
```

### HTTP Status Mapping

| Result Type | HTTP Status | Description |
|------------|-------------|-------------|
| Success | 200 OK | Success with content |
| Success (Created) | 201 Created | Resource created with Location header |
| Success (Unit) | 204 No Content | Success without content |
| ValidationError | 400 Bad Request | Validation errors with details |
| BadRequestError | 400 Bad Request | Invalid request |
| UnauthorizedError | 401 Unauthorized | Authentication required |
| ForbiddenError | 403 Forbidden | Access denied |
| NotFoundError | 404 Not Found | Resource not found |
| ConflictError | 409 Conflict | Resource conflict |
| DomainError | 422 Unprocessable Entity | Domain rule violation |
| RateLimitError | 429 Too Many Requests | Rate limit exceeded |
| UnexpectedError | 500 Internal Server Error | Unexpected error |
| ServiceUnavailableError | 503 Service Unavailable | Service unavailable |

### RFC 7240 — Prefer Header

The `WriteOutcomeExtensions` support the RFC 7240 `Prefer` request header, allowing clients to influence response behavior on write operations:

```csharp
// In a controller — pass the request to enable Prefer header support
var actionResult = outcome.ToActionResult(this, Request, dto => new ItemDto(dto));
```

When a client sends `Prefer: return=minimal`, an `Updated` outcome returns 204 No Content instead of 200 OK with body. The `Preference-Applied` response header confirms which preference was honored.

```
PUT /api/items/1 HTTP/1.1
Prefer: return=minimal
If-Match: "abc123"

HTTP/1.1 204 No Content
ETag: "def456"
Preference-Applied: return=minimal
```

Use `PreferHeader.Parse(request)` to read preferences directly for custom logic.

## Advanced Topics

### Property-Aware Error Messages

When the same value object type is used for multiple properties:

```csharp
public record PersonDto
{
    public Name FirstName { get; init; }  // ← Same type
    public Name LastName { get; init; }   // ← Same type
}
```

Errors correctly show property names:

```json
{
  "errors": {
    "FirstName": ["Name cannot be empty."],
    "LastName": ["Name cannot be empty."]
  }
}
```

Not type names! This requires the `fieldName` parameter in `TryCreate`:

```csharp
public static Result<Name> TryCreate(string? value, string? fieldName = null)
{
    var field = fieldName ?? "name";  // ← Use fieldName
    if (string.IsNullOrWhiteSpace(value))
        return Error.Validation("Name cannot be empty.", field);
    return new Name(value);
}
```

### Combining Validation Approaches

You can use **both** automatic validation and manual Result chaining:

```csharp
[HttpPost]
public ActionResult<User> Register(RegisterUserDto dto)
{
    // dto.Email and dto.FirstName are already validated!
    // Now validate business rules:
    return UserService.CheckEmailNotTaken(dto.Email)
        .Bind(() => User.TryCreate(dto.Email, dto.FirstName, dto.Password))
        .ToActionResult(this);
}
```

This combines:
1. **Automatic validation** - DTO properties validated on deserialization
2. **Manual validation** - Business rules in domain layer

### Custom Validation Responses

For Minimal APIs, customize the response:

```csharp
app.MapPost("/users", (RegisterUserDto dto, HttpContext httpContext) =>
{
    var validationError = ValidationErrorsContext.GetValidationError();
    if (validationError is not null)
    {
        return Results.Json(
            new { success = false, errors = validationError.ToDictionary() },
            statusCode: 422);  // Custom status
    }

    var user = userService.Create(dto);
    return Results.Ok(user);
});
```

### Reflection vs Source Generator

| Feature | Reflection | Source Generator |
|---------|-----------|------------------|
| **Setup** | Simple (no generator) | Requires analyzer reference |
| **Performance** | ~50μs overhead at startup | Zero overhead |
| **AOT Support** | No | Yes |
| **Trimming** | May break | Safe |
| **Use Case** | Prototyping, standard .NET | Production, Native AOT |

See [docs/REFLECTION-FALLBACK.md](docs/REFLECTION-FALLBACK.md) for comprehensive comparison.

## Best Practices

### Scalar Value Validation

1. **Always use `fieldName` parameter** - Enables property-aware errors
2. **Call validation setup in `Program.cs`** - Required for automatic validation
3. **Add `UseScalarValueValidation()` middleware** - Creates validation scope
4. **Use `[ApiController]` in MVC** - Enables automatic validation responses

### Result Conversion

1. **Always pass `this` to `ToActionResult`** - Required for HTTP context
2. **Use async variants for async operations** - `ToActionResultAsync`, `ToHttpResultAsync`
3. **Always provide CancellationToken** - Enables proper cancellation
4. **Use domain-specific errors** - `Error.NotFound()`, not exceptions
5. **Keep domain logic in domain layer** - Controllers orchestrate, not implement

### General

1. **Combine approaches wisely** - Automatic validation for DTOs, manual for business rules
2. **Use source generator for production** - Better performance, AOT support
3. **Test validation thoroughly** - Unit test value objects, integration test endpoints

## Examples

- **[SampleMinimalApi](../Examples/SampleWeb/SampleMinimalApi/)** — Minimal API with Native AOT and source generator
- **[SampleMinimalApiNoAot](../Examples/SampleWeb/SampleMinimalApiNoAot/)** — Minimal API with reflection fallback (no source generator) **← Start here!**
- **[SampleWebApplication](../Examples/SampleWeb/SampleWebApplication/)** — MVC controllers with validation
- **[SampleUserLibrary](../Examples/SampleWeb/SampleUserLibrary/)** — Shared value objects

## Related Packages

- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` type
- [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) — Base value object types
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — Entity and aggregate patterns
- [Trellis.AspSourceGenerator](https://www.nuget.org/packages/Trellis.AspSourceGenerator) — AOT source generator

## License

MIT — see [LICENSE](../LICENSE) for details.
