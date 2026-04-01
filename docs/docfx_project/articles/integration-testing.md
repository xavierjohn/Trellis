# Testing

**Level:** Intermediate 📚 | **Time:** 25-35 min | **Prerequisites:** [Basics](basics.md), [Mediator Pipeline](integration-mediator.md)

Test Trellis applications with purpose-built fakes, assertion extensions, and integration test helpers from the **Trellis.Testing** package. This package provides everything needed to test value objects, aggregates, handlers, authorization, and full HTTP pipelines.

> **Note:** Trellis.Testing uses [FluentAssertions](https://fluentassertions.com/) under the hood. The assertion extensions are designed to produce clear, Trellis-aware failure messages for `Result<T>`, `Maybe<T>`, and `Error` types.

## Table of Contents

- [Installation](#installation)
- [Result Assertions](#result-assertions)
- [Maybe Assertions](#maybe-assertions)
- [Error Assertions](#error-assertions)
- [FakeRepository](#fakerepository)
- [Unique Constraints](#unique-constraints)
- [TestActorProvider](#testactorprovider)
- [Integration Testing](#integration-testing)
- [Test Helpers](#test-helpers)
- [Test Patterns](#test-patterns)
- [Best Practices](#best-practices)

## Installation

```bash
dotnet add package Trellis.Testing
```

This package transitively references `FluentAssertions` — no need to install it separately.

## Result Assertions

Use `result.Should()` to assert on `Result<T>` values with Trellis-aware failure messages:

```csharp
using Trellis.Testing;

// Success assertions
result.Should().BeSuccess();
result.Should().BeSuccess()
    .Which.Name.Should().Be("Acme Corp");

result.Should().HaveValue(expectedValue);
result.Should().HaveValueMatching(v => v.Name == "test");
result.Should().HaveValueEquivalentTo(expected);

// Failure assertions
result.Should().BeFailure();
result.Should().BeFailureOfType<NotFoundError>();
result.Should().HaveErrorCode("not.found");
result.Should().HaveErrorDetail("Order not found");
result.Should().HaveErrorDetailContaining("not found");

// Async variants
await result.Should().BeSuccessAsync();
await result.Should().BeFailureAsync();
await result.Should().BeFailureOfTypeAsync<ValidationError>();
```

> **Tip:** Chain `.Which` after `BeSuccess()` to access the unwrapped value and make further assertions without triggering the `TRLS003` analyzer warning.

## Maybe Assertions

```csharp
using Trellis.Testing;

maybe.Should().HaveValue();
maybe.Should().BeNone();
maybe.Should().HaveValueEqualTo(expected);
maybe.Should().HaveValueMatching(v => v > 0);
maybe.Should().HaveValueEquivalentTo(expected);
```

🔴 **Do NOT use** `.HasValue.Should().BeTrue()` — this bypasses Trellis.Testing's assertion messages. Always use `.Should().HaveValue()` / `.Should().BeNone()`.

## Error Assertions

```csharp
using Trellis.Testing;

error.Should().Be(expectedError);
error.Should().HaveCode("validation.error");
error.Should().HaveDetail("Field is required");
error.Should().HaveDetailContaining("required");
error.Should().HaveInstance("/orders/123");
error.Should().BeOfType<ValidationError>();

// ValidationError-specific assertions
validationError.Should().HaveFieldError("email");
validationError.Should().HaveFieldErrorWithDetail("email", "Email is required");
validationError.Should().HaveFieldCount(2);
```

## FakeRepository

**Namespace:** `Trellis.Testing.Fakes`

An in-memory repository for testing application-layer handlers without a database. Stores aggregates in a dictionary, returns `Result<T>`, and captures published domain events.

### Basic Usage

```csharp
using Trellis.Testing.Fakes;

var repo = new FakeRepository<Order, OrderId>();

// Save an aggregate
var order = Order.Create(customerId, lineItems);
var saveResult = await repo.SaveAsync(order);
saveResult.Should().BeSuccess();

// Retrieve by ID — returns Result<Order>
var result = await repo.GetByIdAsync(order.Id);
result.Should().BeSuccess();

// Find by ID — returns Result<Maybe<Order>>
var maybe = await repo.FindByIdAsync(order.Id);
maybe.Should().BeSuccess()
    .Which.Should().HaveValue();

// Not found
var missing = await repo.GetByIdAsync(OrderId.NewUniqueV4());
missing.Should().BeFailureOfType<NotFoundError>();

// Delete
await repo.DeleteAsync(order.Id);
```

### Domain Event Inspection

`FakeRepository` captures domain events published by aggregates during `SaveAsync`:

```csharp
var repo = new FakeRepository<Order, OrderId>();
var order = Order.Create(customerId, lineItems);
await repo.SaveAsync(order);

repo.PublishedEvents.Should().ContainSingle()
    .Which.Should().BeOfType<OrderCreatedEvent>();
```

### Additional Methods

```csharp
repo.Exists(orderId);     // bool — check without Result wrapper
repo.Get(orderId);        // TAggregate? — get without Result wrapper
repo.GetAll();            // IEnumerable<TAggregate> — all stored aggregates
repo.Count;               // int — number of stored aggregates
repo.Clear();             // remove all aggregates and events
```

## Unique Constraints

Use `WithUniqueConstraint` to simulate database unique constraints in `FakeRepository`. When `SaveAsync` is called, the repository checks that no other aggregate (with a different ID) has the same value for the constrained property. Returns a `ConflictError` on violation.

```csharp
var repo = new FakeRepository<Customer, CustomerId>()
    .WithUniqueConstraint(c => c.Email);

var customer1 = Customer.Create("Alice", email);
await repo.SaveAsync(customer1);  // succeeds

var customer2 = Customer.Create("Bob", email);  // same email
var result = await repo.SaveAsync(customer2);
result.Should().BeFailureOfType<ConflictError>();
```

Multiple constraints can be chained:

```csharp
var repo = new FakeRepository<Customer, CustomerId>()
    .WithUniqueConstraint(c => c.Email)
    .WithUniqueConstraint(c => c.PhoneNumber);
```

Updating the same aggregate (same ID) does not trigger a conflict with itself.

## TestActorProvider

**Namespace:** `Trellis.Testing.Fakes`

A mutable `IActorProvider` for authorization testing. Uses `AsyncLocal<Actor?>` internally so parallel tests sharing a singleton provider never interfere with each other.

### Construction

```csharp
using Trellis.Testing.Fakes;

// From user ID and permissions
var actorProvider = new TestActorProvider("admin", "Orders.Read", "Orders.Write");

// From an Actor instance
var actor = Actor.Create("admin", new HashSet<string> { "Orders.Read" });
var actorProvider = new TestActorProvider(actor);
```

### Scoped Actor Switching

Use `WithActor` to temporarily switch the current actor. The previous actor is restored when the scope is disposed — no `try/finally` needed:

```csharp
var actorProvider = new TestActorProvider("admin", "Orders.Read", "Orders.Write");

// Temporarily switch to a restricted user
await using var scope = actorProvider.WithActor("user-1", "Orders.Read");
var result = await mediator.Send(new CreateOrderCommand());
result.Should().BeFailure();  // missing Orders.Write
// scope disposes → actor reverts to admin
```

### Nested Scopes

Scopes nest correctly for complex authorization scenarios:

```csharp
await using (actorProvider.WithActor("user-1", "Read"))
{
    // actor is user-1 with Read
    await using (actorProvider.WithActor("user-2", "Write"))
    {
        // actor is user-2 with Write
    }
    // actor is user-1 with Read
}
// actor is admin
```

### Handler Test Example

```csharp
[Fact]
public async Task CancelOrder_ByOwner_Succeeds()
{
    var actorProvider = new TestActorProvider("owner-1", "Orders.Cancel");
    var repo = new FakeRepository<Order, OrderId>();
    var order = Order.Create(CustomerId.NewUniqueV4());
    // assume order.CreatedByActorId == "owner-1"
    await repo.SaveAsync(order);

    var handler = new CancelOrderHandler(repo);
    var result = await handler.Handle(
        new CancelOrderCommand(order.Id), CancellationToken.None);

    result.Should().BeSuccess();
}

[Fact]
public async Task CancelOrder_ByNonOwner_ReturnsForbidden()
{
    var actorProvider = new TestActorProvider("other-user", "Orders.Cancel");
    // ... set up order with CreatedByActorId = "owner-1"
    var result = await sender.Send(new CancelOrderCommand(orderId));
    result.Should().BeFailureOfType<ForbiddenError>();
}
```

## Integration Testing

### CreateClientWithActor

**Namespace:** `Trellis.Testing`

Creates an `HttpClient` with the `X-Test-Actor` header pre-set, encoding actor identity and permissions as JSON. Requires `DevelopmentActorProvider` to be registered in the application.

```csharp
using Trellis.Testing;

// In your WebApplicationFactory integration test
var client = factory.CreateClientWithActor("user-1", "Orders.Create", "Orders.Read");

// The client sends this header on every request:
// X-Test-Actor: {"Id":"user-1","Permissions":["Orders.Create","Orders.Read"],...}

var response = await client.PostAsJsonAsync("/api/orders", createRequest);
response.StatusCode.Should().Be(HttpStatusCode.Created);
```

Test authorization at the HTTP level:

```csharp
[Fact]
public async Task CreateOrder_WithoutPermission_Returns403()
{
    var client = _factory.CreateClientWithActor("user-1");  // no permissions
    var response = await client.PostAsJsonAsync("/api/orders", request);
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task CreateOrder_WithPermission_Returns201()
{
    var client = _factory.CreateClientWithActor("user-1", "Orders.Create");
    var response = await client.PostAsJsonAsync("/api/orders", request);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

### ReplaceDbProvider

Swaps the EF Core database provider in `WebApplicationFactory` tests. Removes all EF Core internal services for the context and re-registers with the new provider.

```csharp
using Trellis.Testing;

// Custom WebApplicationFactory that swaps the DB provider
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.ConfigureServices(services =>
            services.ReplaceDbProvider<AppDbContext>(options =>
                options.UseSqlite(_connection).AddTrellisInterceptors()));
    }

    protected override void Dispose(bool disposing)
    {
        _connection.Dispose();
        base.Dispose(disposing);
    }
}

// Test class consumes the custom factory
public class OrderApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OrderApiTests(TestWebApplicationFactory factory) =>
        _factory = factory;
}
```

> **Limitation:** `ReplaceDbProvider` re-registers via `AddDbContext<TContext>`. If your application uses `AddDbContextFactory` or `AddPooledDbContextFactory`, swap providers manually.

### ReplaceResourceLoader

Replaces an `IResourceLoader<TMessage, TResource>` registration with a test implementation:

```csharp
using Trellis.Testing;

// Stateless fake — capture a pre-created instance
var fakeLoader = new FakeOrderResourceLoader(fakeRepo);
services.ReplaceResourceLoader<CancelOrderCommand, Order>(_ => fakeLoader);

// Scoped dependency — resolve from the container
services.ReplaceResourceLoader<CancelOrderCommand, Order>(
    sp => new FakeOrderResourceLoader(sp.GetRequiredService<AppDbContext>()));
```

## Test Helpers

### ResultBuilder

Factory methods for creating `Result<T>` values in tests:

```csharp
using Trellis.Testing.Builders;

ResultBuilder.Success(value);
ResultBuilder.Failure<T>(error);
ResultBuilder.NotFound<T>("Order not found");
ResultBuilder.NotFound<T>("Order", "123");       // "Order '123' not found"
ResultBuilder.Validation<T>("Invalid", "field");
ResultBuilder.Unauthorized<T>();
ResultBuilder.Forbidden<T>();
ResultBuilder.Conflict<T>("Email already exists");
ResultBuilder.Unexpected<T>();
```

### ValidationErrorBuilder

Fluent builder for creating `ValidationError` instances:

```csharp
using Trellis.Testing.Builders;

var error = ValidationErrorBuilder.Create()
    .WithFieldError("email", "Required")
    .WithFieldError("name", "Too short", "Too long")
    .Build();           // → ValidationError

var result = ValidationErrorBuilder.Create()
    .WithFieldError("email", "Required")
    .BuildFailure<T>(); // → Result<T>
```

## Test Patterns

### Domain Unit Tests

Test aggregates and value objects directly — no fakes needed:

```csharp
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
    var order = Order.Create(CustomerId.NewUniqueV4());
    var result = order.Submit();

    result.Should().BeFailure()
        .Which.Should().BeOfType<DomainError>()
        .Which.Should().HaveDetailContaining("empty");
}
```

### Application Handler Tests

Use `FakeRepository` and `TestActorProvider` to test handlers in isolation:

```csharp
[Fact]
public async Task GetOrder_NotFound_ReturnsNotFoundError()
{
    var repo = new FakeRepository<Order, OrderId>();
    var handler = new GetOrderHandler(repo);

    var result = await handler.Handle(
        new GetOrderQuery(OrderId.NewUniqueV4()), CancellationToken.None);

    result.Should().BeFailure()
        .Which.Should().BeOfType<NotFoundError>();
}
```

### Authorization Tests

Verify permission and resource-based authorization:

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

### Full Integration Tests

Combine `WebApplicationFactory`, `CreateClientWithActor`, and `ReplaceDbProvider`:

```csharp
public class OrderApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OrderApiTests(TestWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task CreateOrder_ReturnsCreated()
    {
        var client = _factory.CreateClientWithActor("user-1", "Orders.Create");
        var request = new { CustomerId = Guid.NewGuid(), Items = new[] { /* ... */ } };

        var response = await client.PostAsJsonAsync("/api/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

## Best Practices

1. **Use Trellis.Testing assertions** — Always use `result.Should().BeSuccess()` and `maybe.Should().HaveValue()` instead of raw boolean checks. They produce clear, diagnostic failure messages.

2. **Use `FakeRepository` for handler tests** — Avoid mocking `IRepository` with Moq/NSubstitute. `FakeRepository` provides realistic behavior (NotFound, domain events, unique constraints) without the fragility of mock setups.

3. **Use `WithUniqueConstraint` for conflict scenarios** — Simulates database unique indexes without requiring a real database, keeping handler tests fast.

4. **Use `TestActorProvider` over mocking `IActorProvider`** — The `AsyncLocal`-based design is safe for parallel test execution and supports scoped actor switching.

5. **Use `CreateClientWithActor` for integration tests** — Tests the full HTTP pipeline including authorization, model binding, and response mapping.

6. **Use `ReplaceDbProvider` with SQLite** — Swap to an in-memory SQLite database for fast, isolated integration tests without external dependencies.

7. **Test both success and failure paths** — Every command should have at least one success test and one test per expected failure mode (validation, authorization, not found, conflict).

## Next Steps

- [Mediator Pipeline](integration-mediator.md) — Understand the pipeline behaviors being tested
- [ASP.NET Core Authorization (Entra ID)](integration-asp-authorization.md) — Production actor provider setup
- [FluentValidation](integration-fluentvalidation.md) — Advanced validation testing patterns
- [Error Handling](error-handling.md) — Working with different error types
