# API Documentation for Trellis

Welcome to the Trellis API reference. Trellis combines Railway-Oriented Programming (ROP) with Domain-Driven Design (DDD) to provide structured building blocks for enterprise software in C#.

## Core Packages

### Railway-Oriented Programming

> **New to Railway-Oriented Programming?** The concept uses a railway track analogy where operations flow along a success track or switch to an error track. For a gentle introduction to the philosophy, see [this introductory article](https://blog.logrocket.com/what-is-railway-oriented-programming/). Our documentation provides a comprehensive, production-ready C# implementation with type safety, async support, and real-world patterns.

#### [Result&lt;T&gt;](xref:Trellis.Result`1)

The `Result<T>` monad represents an operation that can either succeed with a value of type `T` or fail with an `Error`. This is the foundation of Railway-Oriented Programming, enabling explicit error handling without exceptions.

**Type Definition:**
```csharp
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public T Value { get; }          // Available when IsSuccess is true
    public Error Error { get; }      // Available when IsFailure is true
}
```

**Core Operations:**
- **`Bind`** - Chain operations that return Result (railway switching)
- **`Map`** - Transform success values while preserving errors
- **`Tap`** - Execute side effects without changing the result (logging, events, notifications)
- **`Ensure`** - Apply business rule validation
- **`Match`** - Pattern match on success/failure for final handling
- **`Combine`** - Aggregate multiple results and collect all errors (parallel validation)
- **`RecoverOnFailure`** - Provide fallback values on failure
- **`MapError`** - Transform error types
- **`When`** - Conditional execution based on predicates

**Common Usage Patterns:**

*Railway Pattern - Sequential Chaining:*
```csharp
var result = UserId.TryCreate(id)
    .Bind(repository.GetUser)
    .Map(user => new UserDto(user))
    .Tap(dto => logger.LogInformation("User retrieved: {Id}", dto.Id))
    .Match(
        onSuccess: dto => Ok(dto),
        onFailure: error => error.ToActionResult(this)
    );
```

*Combine - Parallel Validation (Collects ALL Errors):*
```csharp
var result = EmailAddress.TryCreate(email)
    .Combine(FirstName.TryCreate(firstName))
    .Combine(LastName.TryCreate(lastName))
    .Bind((email, first, last) => User.Create(email, first, last));
```

*Tap - Side Effects Without Breaking the Chain:*
```csharp
var result = order.Submit()
    .Tap(o => logger.LogInformation("Order {Id} submitted", o.Id))
    .Tap(o => eventBus.Publish(new OrderSubmittedEvent(o.Id)))
    .Tap(o => emailService.SendConfirmation(o.CustomerEmail))
    .Tap(o => analytics.Track("OrderSubmitted", o.Id));
// Result is still Result<Order> after all Taps
```

**Combine vs Bind:**
```csharp
// Combine: ALL validations run, ALL errors collected
var result = ProductName.TryCreate(name)      // ❌ Error: "Name too short"
    .Combine(Price.TryCreate(price))          // ❌ Error: "Price must be positive"
    .Combine(Quantity.TryCreate(quantity));   // ✅ Success
// Returns errors for BOTH name AND price

// Bind: Sequential - stops at first error
var result = ProductName.TryCreate(name)      // ❌ Error: "Name too short"
    .Bind(n => Price.TryCreate(price)         // 🚫 Never executed
        .Bind(p => Quantity.TryCreate(quantity)));
// Only returns the name error
```

**Factory Methods:**
- `Result.Success<T>(T value)` - Create a successful result
- `Result.Failure<T>(Error error)` - Create a failed result

#### Using Result&lt;Unit&gt; for Void Operations

When an operation doesn't return a value (like Delete or Update commands), use `Result<Unit>`:

**What is Unit?**
- `Unit` is a special type representing "no value" 
- Similar to `void` but can be used as a generic type parameter
- `Result<Unit>` indicates success/failure without a data payload

**Type:**
```csharp
Result<Unit>  // Success or failure, no value
```

**Common Use Cases:**
- Delete operations
- Update operations that don't return data
- Void commands
- Operations that modify state

**Example:**
```csharp
// Void operation that can fail
public Result<Unit> DeleteUser(UserId id) =>
    repository.Delete(id)
        .Tap(() => eventBus.Publish(new UserDeletedEvent(id)));

// In API controllers, Unit results automatically return 204 No Content
[HttpDelete("{id}")]
public ActionResult<Unit> DeleteUser(Guid id) =>
    UserId.TryCreate(id)
        .Bind(DeleteUserCommand)
        .ToActionResult(this);  // Returns 204 No Content on success
```

**Factory Methods for Unit Results:**
- `Result.Success()` - Create successful unit result
- `Result.Failure(Error error)` - Create failed unit result

#### [Maybe&lt;T&gt;](xref:Trellis.Maybe`1)

The `Maybe<T>` monad represents an optional value that may or may not exist. Use it to eliminate null reference exceptions and make optionality explicit in your type system. Think of it as a type-safe alternative to nullable references.

**Type Definition:**
```csharp
public readonly struct Maybe<T>
{
    public bool HasValue { get; }
    public bool HasNoValue { get; }
    public T Value { get; }  // Throws if HasNoValue is true
}
```

**Core Operations:**
- **`GetValueOrDefault`** - Safely extract value with fallback
- **`TryGetValue`** - Try to get the value (out parameter pattern)
- **`GetValueOrThrow`** - Get value or throw exception
- **`ToResult`** - Convert to Result (None becomes an Error)
- **`AsMaybe`** - Convert from nullable to Maybe
- **`AsNullable`** - Convert Maybe back to nullable

**Properties:**
- **`HasValue`** - Check if value is present
- **`HasNoValue`** - Check if value is absent  
- **`Value`** - Get value (throws if absent)

**Common Patterns:**
```csharp
// Repository query (null-safe)
Maybe<User> FindUserByEmail(EmailAddress email);

// Safe value extraction with fallback
var user = repository.FindUserByEmail(email);
var userName = user.HasValue 
    ? user.Value.FullName 
    : "Unknown User";

// Or using GetValueOrDefault
var userName = repository.FindUserByEmail(email)
    .GetValueOrDefault(defaultUser)
    .FullName;

// Convert to Result for further processing
var result = repository.FindUserByEmail(email)
    .ToResult(Error.NotFound("User", email.Value))
    .Bind(user => user.UpdateProfile(newData));

// Try pattern for safe access
if (repository.FindUserByEmail(email).TryGetValue(out var user))
{
    // Use user safely
    ProcessUser(user);
}
```

**Factory Methods:**
- `Maybe.From<T>(T value)` - Create Maybe from nullable value (null becomes None)
- `Maybe<T>.None` - Create an empty Maybe

**When to Use:**
- **Maybe**: When absence is a valid, expected state (e.g., optional config, search results)
- **Result**: When you need to communicate *why* something failed (validation, business rules)

#### [Error Types](xref:Trellis.Error)

A hierarchy of error types for representing different failure scenarios:
- `ValidationError` - Input validation failures with field-level details
- `NotFoundError` - Resource not found (404)
- `UnauthorizedError` - Authentication required (401)
- `ForbiddenError` - Insufficient permissions (403)
- `ConflictError` - Resource conflicts (409)
- `DomainError` - Business rule violations (422)
- `UnexpectedError` - Unexpected system errors (500)
- `ServiceUnavailableError` - Service unavailable (503)

---

## Domain-Driven Design

### [Aggregate](xref:Trellis.Aggregate`1)

A DDD aggregate is a cluster of domain objects that can be treated as a single unit. An aggregate will have one of its component objects be the aggregate root. Any references from outside the aggregate should only go to the aggregate root. The root can thus ensure the integrity of the aggregate as a whole.

**Key features:** Encapsulation, consistency boundaries, transactional boundaries

[Read more about DDD Aggregates](https://martinfowler.com/bliki/DDD_Aggregate.html)

### [Entity](xref:Trellis.Entity`1)

A domain object that has a unique identity that runs through time and different representations. Two entities with the same identity are considered the same, even if their attributes differ.

### [ValueObject](xref:Trellis.ValueObject)

A value object is an object that represents a descriptive aspect of the domain with no conceptual identity. It is a small, simple object that encapsulates a concept from your problem domain. Unlike an aggregate, a value object does not have a unique identity and is immutable. Value objects support and enrich the ubiquitous language of your domain.

**Key characteristics:** Immutability, equality by value, no identity

---

## Integration Packages

### ASP.NET Core Integration

#### [ActionResult Extensions](xref:Trellis.Asp.ActionResultExtensions)

Convert `Result<T>` to ASP.NET Core action results for MVC controllers.

**Features:** Automatic status code mapping, Problem Details (RFC 7807) format, field-level validation errors

#### [HttpResult Extensions](xref:Trellis.Asp.HttpResultExtensions)

Convert `Result<T>` to `IResult` for Minimal API endpoints.

**Features:** Fluent error matching, pagination support, Unit type handling (204 No Content)

### HTTP Client Integration

#### [HttpResponseExtensions](xref:Trellis.Http.HttpResponseExtensions)

Extension methods for `HttpResponseMessage` that enable functional HTTP communication with Result types.

**Key methods:**
- `HandleNotFound` / `HandleUnauthorized` / `HandleForbidden` / `HandleConflict` - Handle specific status codes
- `HandleClientError` / `HandleServerError` - Handle error ranges (4xx, 5xx)
- `EnsureSuccess` - Functional alternative to `EnsureSuccessStatusCode()`
- `ReadResultFromJsonAsync` - Deserialize JSON to `Result<T>`
- `ReadResultMaybeFromJsonAsync` - Deserialize JSON to `Result<Maybe<T>>`

### FluentValidation Integration

#### [FluentValidation Extensions](xref:Trellis.FluentValidation.FluentValidationResultExtensions)

Convert FluentValidation results to `Result<T>` for seamless integration with Railway-Oriented Programming.

**Features:** Automatic `ValidationError` creation, field-level error mapping, async validation support

---

## Common Value Objects

### EmailAddress

A validated email address value object. Ensures email format correctness at construction.

**Type:** `Trellis.EmailAddress`

### RequiredString&lt;TSelf&gt;

A non-null, non-empty string value object. Prevents primitive obsession for required text fields.

**Type:** `Trellis.RequiredString<TSelf>`

### RequiredGuid&lt;TSelf&gt;

A validated GUID value object that cannot be empty (Guid.Empty).

**Type:** `Trellis.RequiredGuid<TSelf>`

### ScalarValueObject&lt;TSelf, T&gt;

Base class for creating custom value objects that wrap a single primitive value.

**Type:** `Trellis.ScalarValueObject<TSelf, T>`

**Common examples:** `FirstName`, `LastName`, `OrderId`, `UserId`, `ProductCode`

---

## Observability

### OpenTelemetry Tracing

Built-in distributed tracing support for Railway-Oriented Programming operations, with primitive value object tracing generally being the lower-noise default and full ROP tracing better suited to break-glass debugging.

**Type:** `Trellis.ResultsTraceProviderBuilderExtensions`

**Features:**
- Automatic span creation for `Bind`, `Map`, `Tap`, `Ensure` operations
- Primitive value object tracing for parse/validate/create boundaries
- Error tracking and status codes
- Trace correlation across async operations
- Integration with Jaeger, Zipkin, Application Insights

---

## Next Steps

- **Get Started:** Read the [Introduction](~/articles/intro.md) guide
- **Learn Basics:** Explore [Core Concepts](~/articles/basics.md)
- **Integration:** See [ASP.NET Core](~/articles/integration-aspnet.md), [HTTP Client](~/articles/integration-http.md), and [FluentValidation](~/articles/integration-fluentvalidation.md) guides
- **Examples:** Browse [Real-World Examples](~/articles/examples.md)

---

## Package Reference

| Package | Description | NuGet |
|---------|-------------|-------|
| Trellis.Results | Core Result/Maybe types | [![NuGet](https://img.shields.io/nuget/v/Trellis.Results.svg)](https://www.nuget.org/packages/Trellis.Results) |
| Trellis.Asp | ASP.NET Core integration | [![NuGet](https://img.shields.io/nuget/v/Trellis.Asp.svg)](https://www.nuget.org/packages/Trellis.Asp) |
| Trellis.Http | HTTP client extensions | [![NuGet](https://img.shields.io/nuget/v/Trellis.Http.svg)](https://www.nuget.org/packages/Trellis.Http) |
| Trellis.FluentValidation | FluentValidation integration | [![NuGet](https://img.shields.io/nuget/v/Trellis.FluentValidation.svg)](https://www.nuget.org/packages/Trellis.FluentValidation) |
| Trellis.Primitives | Reusable value objects | [![NuGet](https://img.shields.io/nuget/v/Trellis.Primitives.svg)](https://www.nuget.org/packages/Trellis.Primitives) |
| Trellis.DomainDrivenDesign | DDD building blocks | [![NuGet](https://img.shields.io/nuget/v/Trellis.DomainDrivenDesign.svg)](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) |
| Trellis.Analyzers | Roslyn analyzers | [![NuGet](https://img.shields.io/nuget/v/Trellis.Analyzers.svg)](https://www.nuget.org/packages/Trellis.Analyzers) |
| Trellis.Testing | Test helpers & assertions | [![NuGet](https://img.shields.io/nuget/v/Trellis.Testing.svg)](https://www.nuget.org/packages/Trellis.Testing) |
