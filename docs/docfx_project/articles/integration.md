# Integration

Integrate Trellis with popular .NET frameworks and tools for building production-ready applications with Railway-Oriented Programming.

## Overview

This section provides comprehensive guides for integrating the Trellis library with:

- **ASP.NET Core** - MVC Controllers and Minimal API
- **HTTP Client** - HttpClient extensions for Result/Maybe patterns
- **FluentValidation** - Powerful validation framework
- **Entity Framework Core** - ORM and repository patterns
- **OpenTelemetry** - Distributed tracing and observability
- **Problem Details** - Standard error response format (RFC 7807)

Each integration guide includes installation instructions, configuration examples, best practices, and complete working code samples.

## Integration Guides

### 🌐 [ASP.NET Core Integration](integration-aspnet.md)
**Level:** Intermediate | **Time:** 30-45 min

Learn how to integrate Railway-Oriented Programming with ASP.NET Core:

- **MVC Controllers** - `ToActionResult` for controllers
- **Minimal API** - `ToHttpResult` for endpoints
- **Automatic Error Mapping** - Error types → HTTP status codes
- **Pagination Support** - HTTP 206 Partial Content responses
- **Custom Error Responses** - `MatchError` for fine-grained control

**Key Features:**
- ✅ Automatic status code mapping (ValidationError → 400, NotFoundError → 404, etc.)
- ✅ Problem Details (RFC 7807) format
- ✅ Field-level validation errors
- ✅ Unit type support (204 No Content)
- ✅ Full async/await with CancellationToken

---

### 🌐 [HTTP Client Integration](integration-http.md)
**Level:** Beginner | **Time:** 20-30 min

Work with HttpClient using functional patterns:

- **Status Code Handlers** - Handle 401, 403, 404, 409 errors functionally
- **Range Handlers** - Handle all 4xx or 5xx errors at once
- **JSON Deserialization** - Convert responses to `Result<T>` or `Result<Maybe<T>>`
- **Functional Error Handling** - No exceptions, just Result types
- **Railway Composition** - Chain HTTP calls with other operations

**Key Features:**
- ✅ Specific status code handling (HandleUnauthorized, HandleForbidden, HandleConflict)
- ✅ Range-based error handling (HandleClientError, HandleServerError)
- ✅ EnsureSuccess - Functional alternative to EnsureSuccessStatusCode()
- ✅ JSON deserialization with Result/Maybe
- ✅ Full async/await with CancellationToken

---

### ✅ [FluentValidation Integration](integration-fluentvalidation.md)
**Level:** Intermediate | **Time:** 30-40 min

Integrate FluentValidation for powerful, composable validation:

- **Inline Validators** - Simple validation within aggregates
- **Separate Validator Classes** - Complex validation logic
- **Async Validation** - Database uniqueness checks, external service calls
- **Dependency Injection** - Register validators with ASP.NET Core DI
- **Advanced Patterns** - Conditional validation, custom validators, cascading failures

**Key Features:**
- ✅ Converts FluentValidation results to `Result<T>`
- ✅ Rich validation rule set
- ✅ Automatic ValidationError formatting
- ✅ Async validation support
- ✅ Seamless DI integration

---

### 💾 [Entity Framework Core Integration](integration-ef.md)
**Level:** Intermediate | **Time:** 30-40 min

Build type-safe repository patterns with EF Core:

- **Repository Pattern** - Repositories that return `Result<T>`
- **Extension Methods** - Convert nullable to Result
- **Exception Handling** - Map database exceptions to error types
- **Pagination** - Paginated results with EF Core
- **Value Object Configuration** - Configure value objects in EF Core

**Key Features:**
- ✅ No more repository exceptions
- ✅ Explicit error handling
- ✅ Database exception mapping (conflicts, concurrency, etc.)
- ✅ Value object conversions
- ✅ Type-safe queries

---

### 📊 [Observability & Monitoring](integration-observability.md)
**Level:** Advanced | **Time:** 20-30 min

Enable distributed tracing and monitoring:

- **OpenTelemetry Tracing** - Primitive value object tracing for normal diagnostics, with ROP tracing available as a deeper break-glass option
- **Problem Details (RFC 7807)** - Standard error response format
- **Trace Correlation** - Link HTTP errors to distributed traces
- **Production Monitoring** - Set up observability in production

**Key Features:**
- ✅ Automatic span creation for primitive value objects and, when explicitly enabled, ROP operations
- ✅ Detailed trace attributes (error types, timings, etc.)
- ✅ Compatible with Jaeger, Zipkin, Application Insights
- ✅ Problem Details with trace IDs
- ✅ Error rate monitoring

---

## Quick Start

### 1. Install Packages

```bash
# ASP.NET Core integration
dotnet add package Trellis.Asp

# HttpClient integration
dotnet add package Trellis.Http

# FluentValidation integration
dotnet add package Trellis.FluentValidation
dotnet add package FluentValidation

# For OpenTelemetry tracing
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

### 2. Configure Services (Program.cs)

```csharp
using Trellis;
using FluentValidation;
using OpenTelemetry.Trace;
using Mediator;

var builder = WebApplication.CreateBuilder(args);

// Add controllers or minimal API
builder.Services.AddControllers();
// or
builder.Services.AddEndpointsApiExplorer();

// Register Mediator (OSS alternative to MediatR)
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

// Register FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Register EF Core
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();

// Add OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("MyApplication"))
    .WithTracing(tracerBuilder =>
    {
        tracerBuilder
            .AddPrimitiveValueObjectInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter();
    });

// AddResultsInstrumentation() is available when you need deep, temporary
// pipeline forensics, but it is intentionally omitted from the default
// example because it traces every Result<T> operation and can be noisy.

var app = builder.Build();

app.MapControllers();
app.Run();
```

### 3. Use in Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Register(
        [FromBody] RegisterUserRequest request,
        CancellationToken ct)
        => await FirstName.TryCreate(request.FirstName)
            .Combine(LastName.TryCreate(request.LastName))
            .Combine(EmailAddress.TryCreate(request.Email))
            .Bind((firstName, lastName, email) => 
                RegisterUserCommand.TryCreate(firstName, lastName, email))
            .BindAsync(command => _mediator.Send(command, ct), ct)
            .MapAsync(user => user.Adapt<UserDto>())
            .ToActionResultAsync(this);
}
```

## Complete Architecture Example

Here's how all the integrations work together:

```
┌─────────────────────────────────────────────────────────────┐
│                      API Layer (ASP.NET Core)               │
│  Controllers/Endpoints → ToActionResult/ToHttpResult        │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────┼────────────────────────────────────┐
│                   Application Layer (Mediator)              │
│  Commands/Queries → FluentValidation → Result<T>            │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────┼────────────────────────────────────┐
│                      Domain Layer                           │
│  Aggregates, Value Objects, Business Rules                  │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────┼────────────────────────────────────┐
│              Infrastructure Layer (EF Core)                 │
│  Repositories → Result<T> (no exceptions)                   │
└─────────────────────────────────────────────────────────────┘

                  All layers traced with OpenTelemetry
```

## Best Practices

### 1. Keep Result<T> Internal

Convert to HTTP responses only at the API boundary:

```csharp
// ✅ Good - Result stays in application layer
public class UserService
{
    public async Task<Result<User>> CreateUserAsync(
        CreateUserCommand command,
        CancellationToken ct) { /* ... */ }
}

[HttpPost]
public async Task<ActionResult<UserDto>> CreateUser(
    CreateUserRequest request,
    CancellationToken ct) =>
    await _userService.CreateUserAsync(request.ToCommand(), ct)
        .ToActionResultAsync(this);  // Convert at boundary
```

### 2. Validate Early

Use FluentValidation at the edge of your system:

```csharp
public async Task<Result<Order>> CreateOrderAsync(
    CreateOrderCommand command,
    CancellationToken ct)
{
    // Validate first, fail fast
    return await _validator.ValidateToResultAsync(command, ct)
        .BindAsync((validCmd, cancellationToken) => 
            ProcessOrderAsync(validCmd, cancellationToken), ct);
}
```

### 3. Repository Returns Result

No more repository exceptions:

```csharp
// ✅ Good
public async Task<Result<User>> GetByIdAsync(UserId id, CancellationToken ct)
{
    var user = await _context.Users.FindAsync(id, ct);
    return user.ToResult(Error.NotFound($"User {id} not found"));
}
```

### 4. Enable Tracing in Production

Monitor errors and performance:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerBuilder =>
    {
        tracerBuilder
            .AddPrimitiveValueObjectInstrumentation()
            .AddOtlpExporter();
    });

// AddResultsInstrumentation() can be enabled temporarily when you need
// full Result<T> pipeline forensics, but it is usually too noisy to make
// a good default production setting.
```

### 5. Always Pass CancellationToken

Support graceful cancellation:

```csharp
public async Task<Result<Order>> ProcessOrderAsync(
    CreateOrderCommand command,
    CancellationToken ct)  // ✅ Accept token
    => await _validator.ValidateToResultAsync(command, ct)  // ✅ Pass through
        .BindAsync((cmd, cancellationToken) => 
            CreateOrderAsync(cmd, cancellationToken), ct);  // ✅ Pass to all async ops
```

## Error Response Examples

### Validation Error (400)

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "email": ["Email is required"],
    "age": ["Must be 18 or older"]
  }
}
```

### Not Found Error (404)

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

### Conflict Error (409)

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflict",
  "status": 409,
  "detail": "Email already registered"
}
```

## Next Steps

1. **Start with** [ASP.NET Core Integration](integration-aspnet.md) to learn the basics
2. **Add validation** with [FluentValidation Integration](integration-fluentvalidation.md)
3. **Implement repositories** using [Entity Framework Core Integration](integration-ef.md)
4. **Enable monitoring** with [Observability & Monitoring](integration-observability.md)

For complete working examples, see:
- [Examples](examples.md) - Real-world code samples
- [Error Handling](error-handling.md) - Working with different error types
- [Debugging](debugging.md) - Tools and techniques for debugging ROP chains
