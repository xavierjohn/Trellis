# Trellis — AI Testing API Reference

> This document covers the `Trellis.Testing` package: FluentAssertions extensions, test builders,
> fake repositories, actor providers, and testing patterns for Trellis applications.
> For the core framework API, see the per-library `trellis-api-*.md` files (e.g., `trellis-api-results.md`, `trellis-api-ddd.md`).

---

## Result Assertions

```csharp
result.Should().BeSuccess()                              // returns AndWhichConstraint with value
result.Should().BeFailure()                              // returns AndWhichConstraint with Error
result.Should().BeFailureOfType<NotFoundError>()
result.Should().HaveValue(expected)
result.Should().HaveValueMatching(v => v.Name == "test")
result.Should().HaveValueEquivalentTo(expected)
result.Should().HaveErrorCode("not.found")
result.Should().HaveErrorDetail("Order not found")
result.Should().HaveErrorDetailContaining("not found")

// Async
await result.Should().BeSuccessAsync()
await result.Should().BeFailureAsync()
await result.Should().BeFailureOfTypeAsync<ValidationError>()
```

## Maybe Assertions

```csharp
maybe.Should().HaveValue()
maybe.Should().BeNone()
maybe.Should().HaveValueEqualTo(expected)
maybe.Should().HaveValueMatching(v => v > 0)
maybe.Should().HaveValueEquivalentTo(expected)
```

🔴 **Do NOT use** `.HasValue.Should().BeTrue()` or `.HasNoValue.Should().BeTrue()` — these bypass
Trellis.Testing's assertion messages. Always use `.Should().HaveValue()` / `.Should().BeNone()`.

## Unwrap — Test Value Extraction

Extracts values from `Result<T>` or `Maybe<T>` in test code without triggering TRLS003. Throws `UnwrapFailedException` with descriptive error details on failure/none.

```csharp
// Result<T>
T Unwrap<T>(this Result<T>)
Task<T> UnwrapAsync<T>(this Task<Result<T>>)
ValueTask<T> UnwrapAsync<T>(this ValueTask<Result<T>>)

// Maybe<T>
T Unwrap<T>(this Maybe<T>)
```

**Usage — typical test pattern:**
```csharp
result.Should().BeSuccess();
var value = result.Unwrap(); // No TRLS003 warning

// Or in async tests:
var value = await resultTask.UnwrapAsync();
```

**Exception on failure:**
```csharp
var result = Result.Failure<int>(Error.NotFound("Item not found."));
result.Unwrap(); // throws UnwrapFailedException:
                 // "Called Unwrap() on a failed Result<Int32>. Error: [not.found.error] Item not found."
```

🔴 **Do NOT use in production code.** Use `Match`, `GetValueOrDefault`, or ROP operations instead.

## Error Assertions

```csharp
error.Should().Be(expectedError)
error.Should().HaveCode("validation.error")
error.Should().HaveDetail("Field is required")
error.Should().HaveDetailContaining("required")
error.Should().HaveInstance("/orders/123")
error.Should().BeOfType<ValidationError>()
```

## ValidationError Assertions

```csharp
validationError.Should().HaveFieldError("email")
validationError.Should().HaveFieldErrorWithDetail("email", "Email is required")
validationError.Should().HaveFieldCount(2)
```

## Test Builders

```csharp
// ResultBuilder
ResultBuilder.Success(value)
ResultBuilder.Failure<T>(error)
ResultBuilder.NotFound<T>("Order not found")
ResultBuilder.NotFound<T>("Order", "123")      // "Order '123' not found"
ResultBuilder.Validation<T>("Invalid", "field")
ResultBuilder.Unauthorized<T>()
ResultBuilder.Forbidden<T>()
// ... Conflict, Unexpected, Domain, RateLimit, BadRequest, ServiceUnavailable

// ValidationErrorBuilder
ValidationErrorBuilder.Create()
    .WithFieldError("email", "Required")
    .WithFieldError("name", "Too short", "Too long")
    .Build()           // → ValidationError
    .BuildFailure<T>() // → Result<T>
```

## FakeRepository

**Namespace: `Trellis.Testing.Fakes`**

In-memory repository for Application-layer handler tests. Stores entities in a dictionary, returns
`Result<T>` (NotFound if missing), and captures published domain events.

```csharp
// Construction
var repo = new FakeRepository<Order, OrderId>();

// CRUD operations
await repo.SaveAsync(order);
var result = await repo.GetByIdAsync(orderId);        // Result<Order> (NotFound if missing)
var maybe = await repo.FindByIdAsync(orderId);        // Maybe<Order>
await repo.DeleteAsync(orderId);

// Query helpers — use in test repository adapters for custom query methods
var customer = await repo.FindAsync(c => c.Email == email);          // Maybe<T> (first match or None)
var orders = await repo.WhereAsync(o => o.CustomerId == customerId); // IReadOnlyList<T>
var overdue = await repo.WhereAsync(new OverdueOrderSpec(cutoff));   // IReadOnlyList<T> via Specification

// Seeding test data
var order = Order.Create(...);
await repo.SaveAsync(order);                           // Now GetByIdAsync will return it

// Domain event inspection
repo.PublishedEvents                                   // IReadOnlyList<IDomainEvent>
```

## FakeSharedResourceLoader

**Namespace: `Trellis.Testing.Fakes`**

In-memory fake implementation of `SharedResourceLoaderById<TResource, TId>` that delegates to a `FakeRepository`. Eliminates the need to hand-write a per-command resource loader in tests when using `IAuthorizeResource<T>` with `SharedResourceLoaderById`.

```csharp
// Construction — pass the FakeRepository
var repo = new FakeRepository<Order, OrderId>();
var loader = new FakeSharedResourceLoader<Order, OrderId>(repo);

// DI registration in test Startup:
services.AddScoped<FakeRepository<Order, OrderId>>();
services.AddScoped<SharedResourceLoaderById<Order, OrderId>, FakeSharedResourceLoader<Order, OrderId>>();
```

## AggregateTestMutator — Time-Travel for Maybe\<T\> Properties

**Namespace: `Trellis.Testing`**

Reflection-based helpers to set source-generated `Maybe<T>` backing fields in tests. Works with and without EF Core — usable in both unit tests (with `FakeRepository`) and integration tests (with `WebApplicationFactory`). Fluent chaining supported.

```csharp
// Backdate a Maybe<DateTime> property — no raw SQL
order.SetMaybeField(o => o.SubmittedAt, DateTime.UtcNow.AddDays(-8))
     .SetMaybeField(o => o.ShippedAt, DateTime.UtcNow.AddDays(-5));

// Clear a Maybe<T> to None
order.ClearMaybeField(o => o.SubmittedAt);
```

For integration tests, load the entity from a scoped `DbContext`, mutate, then save:
```csharp
using var scope = factory.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var order = await db.Orders.FindAsync(orderId);
order!.SetMaybeField(o => o.SubmittedAt, DateTime.UtcNow.AddDays(-8));
await db.SaveChangesAsync(ct);
```

## WithFakeTimeProvider — Full Time Control in Integration Tests

**Namespace: `Trellis.Testing`**

`WebApplicationFactory` extension that registers a `FakeTimeProvider` as the `TimeProvider` singleton. Tests rewind/advance time to control timestamps set by domain logic and `EntityTimestampInterceptor`.

```csharp
var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
_factory = _factory.WithFakeTimeProvider(fakeTime);

// Or with out parameter:
_factory = _factory.WithFakeTimeProvider(out var fakeTime);

// Rewind time, then exercise the endpoint
fakeTime.SetUtcNow(DateTimeOffset.UtcNow.AddDays(-8));
await client.PostAsync("/api/orders/1/submission", null, ct);
fakeTime.SetUtcNow(DateTimeOffset.UtcNow);
var response = await client.GetAsync("/api/orders/overdue", ct);
```

## ReplaceSingleton — Generic DI Helper

**Namespace: `Trellis.Testing`**

Replaces all registrations of a service type with a singleton instance. Common use: swapping `TimeProvider` in integration tests.

```csharp
services.ReplaceSingleton<TimeProvider>(fakeTime);
```

## TestActorProvider and TestActorScope

**Namespace: `Trellis.Testing.Fakes`**

Mutable `IActorProvider` for authorization testing. Uses `AsyncLocal<Actor?>` internally so parallel tests sharing a singleton provider never interfere. `WithActor` returns a scope that restores the previous actor on dispose, eliminating `try/finally` boilerplate.

Implements `IActorProvider` with `GetCurrentActorAsync()` returning `Task.FromResult()`. Register as `IActorProvider` in DI.

### Construction

```csharp
var actorProvider = new TestActorProvider("admin", "Orders.Read", "Orders.Write");
var actorFromInstance = new TestActorProvider(actor);               // from Actor instance
```

### Scoped Actor Switching

```csharp
// Temporarily switch actor — restored on dispose
await using var scope1 = actorProvider.WithActor("user-1", "Orders.Read");
await using var scope2 = actorProvider.WithActor(actor);           // from Actor instance

// Synchronous dispose also supported
using var scope3 = actorProvider.WithActor("user-1", "Orders.Read");
```

### Nested Scopes

```csharp
await using (actorProvider.WithActor("user-1", "Read"))
{
    await using (actorProvider.WithActor("user-2", "Write"))
    {
        // actor is user-2
    }
    // actor is user-1
}
// actor is admin
```

## ServiceCollection Extensions

Replaces existing `IResourceLoader<TMessage, TResource>` DI registrations with a test implementation. Registered as scoped, matching the production lifetime.

```csharp
// Stateless fake — capture a pre-created instance
var fakeLoader = new FakeOrderResourceLoader(fakeRepo);
services.ReplaceResourceLoader<CancelOrderCommand, Order>(_ => fakeLoader);

// Scoped dependency — resolve from the container
services.ReplaceResourceLoader<CancelOrderCommand, Order>(
    sp => new FakeOrderResourceLoader(sp.GetRequiredService<AppDbContext>()));
// Internally: RemoveAll + AddScoped
```

## WebApplicationFactory Extensions

Creates an `HttpClient` with the `X-Test-Actor` header pre-set, encoding actor identity and permissions as JSON.

```csharp
// Extension on WebApplicationFactory<TEntryPoint>
var client = factory.CreateClientWithActor("user-1", "Orders.Create", "Orders.Read");
// Sets header: X-Test-Actor: {"Id":"user-1","Permissions":["Orders.Create","Orders.Read"]}
```

---

## ReplaceDbProvider

Cleanly swaps the EF Core database provider in `WebApplicationFactory` tests. Removes all EF Core internal services for the context (including `IDbContextOptionsConfiguration<TContext>` in EF Core 10) and re-registers with the new provider.

```csharp
// In TestWebApplicationFactoryFixture.ConfigureWebHost
builder.ConfigureServices(services =>
    services.ReplaceDbProvider<AppDbContext>(options =>
        options.UseSqlite(_connection).AddTrellisInterceptors()));
```

> **Limitation:** Always re-registers via `AddDbContext<TContext>`. If the application uses `AddDbContextFactory` or `AddPooledDbContextFactory`, swap providers manually instead of using this helper.

---

## ClaimsActorProvider and CachingActorProvider in Tests

For integration tests using `WebApplicationFactory`, the `DevelopmentActorProvider` (via `X-Test-Actor` header) is the standard approach. `ClaimsActorProvider` and `CachingActorProvider` are production providers — they read from `HttpContext.User` which requires real authentication middleware.

If your tests need to exercise `ClaimsActorProvider` directly, construct a `ClaimsPrincipal` and set it on `HttpContext.User`:

```csharp
var identity = new ClaimsIdentity(new[]
{
    new Claim("sub", "user-1"),
    new Claim("permissions", "orders:read"),
}, "Bearer");
var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
```

For `CachingActorProvider`, register via DI using `AddCachingActorProvider<T>()`. For unit tests, construct with an `IHttpContextAccessor`:

```csharp
var inner = new TestActorProvider("user-1", "orders:read");
var accessor = new HttpContextAccessor(); // no HttpContext in unit tests — uses CancellationToken.None
var caching = new CachingActorProvider(inner, accessor);
var actor = await caching.GetCurrentActorAsync(ct);  // calls inner once, caches
```

---

## Test Patterns

### Testing Result<T> with TRLS003 Analyzer

The `TRLS003` analyzer warns when accessing `result.Value` without checking `IsSuccess`. Since
`TreatWarningsAsErrors` is typically enabled, use FluentAssertions to access values safely:

```csharp
// ✅ Correct — chain off .Which after asserting success
var result = Customer.TryCreate(firstName, lastName, email, phone, address);
result.Should().BeSuccess()
    .Which.Email.Should().Be(email);

// ✅ Also correct — assert then use Unwrap() to extract value without TRLS003
var result = Order.TryCreate(customerId, lineItems);
result.Should().BeSuccess();
var order = result.Unwrap();    // safe after assertion, no TRLS003 warning

// ✅ Correct — failure assertions
var result = order.Submit();
result.Should().BeFailure()
    .Which.Should().BeOfType<ValidationError>();

// ❌ Wrong — TRLS003 compile error
var customer = Customer.TryCreate(...).Value;  // Accessing .Value without guard
```

### Domain Unit Tests

```csharp
using Trellis.Testing;

[Fact]
public void CreateOrder_ValidInput_ReturnsSuccess()
{
    var customerId = CustomerId.NewUniqueV4();
    var result = Order.TryCreate(customerId);

    result.Should().BeSuccess()
        .Which.CustomerId.Should().Be(customerId);
}

[Fact]
public void CreateOrder_EmptySubmit_ReturnsFailure()
{
    var orderResult = Order.TryCreate(CustomerId.NewUniqueV4());
    orderResult.Should().BeSuccess();

    var order = orderResult.Unwrap();
    var result = order.Submit();

    result.Should().BeFailure()
        .Which.Should().BeOfType<DomainError>()
        .Which.Should().HaveDetailContaining("empty");
}
```

### Application Handler Tests with FakeRepository

```csharp
[Fact]
public async Task GetOrder_NotFound_ReturnsNotFoundError()
{
    var repo = new FakeRepository<Order, OrderId>();
    var result = await repo.GetByIdAsync(OrderId.NewUniqueV4());

    result.Should().BeFailure()
        .Which.Should().BeOfType<NotFoundError>();
}
```

### Maybe<T> Assertions in Tests

```csharp
// ✅ Correct — Trellis.Testing assertions
customer.PhoneNumber.Should().HaveValue();
customer.PhoneNumber.Should().BeNone();
order.SubmittedAt.Should().HaveValue();
order.SubmittedAt.Should().HaveValueMatching(d => d > DateTime.UtcNow.AddMinutes(-1));

// ❌ Wrong — bypasses Trellis.Testing, poor error messages
customer.PhoneNumber.HasValue.Should().BeTrue();
customer.PhoneNumber.HasNoValue.Should().BeTrue();
```

### Authorization Tests

```csharp
[Fact]
public async Task Cancel_ByOwner_Succeeds()
{
    var actorProvider = new TestActorProvider("owner-1", Permissions.OrdersCancel);
    // ... set up order with CreatedByActorId = "owner-1"
    var result = await sender.Send(new CancelOrderCommand(orderId));
    result.Should().BeSuccess();
}

[Fact]
public async Task Cancel_ByNonOwner_ReturnsForbidden()
{
    var actorProvider = new TestActorProvider("other-user", Permissions.OrdersCancel);
    // ... set up order with CreatedByActorId = "owner-1"
    var result = await sender.Send(new CancelOrderCommand(orderId));
    result.Should().BeFailureOfType<ForbiddenError>();
}
```
