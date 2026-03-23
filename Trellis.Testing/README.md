# Trellis.Testing — Testing Utilities

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Testing.svg)](https://www.nuget.org/packages/Trellis.Testing)

Testing utilities and FluentAssertions extensions for **Trellis** — write expressive, maintainable tests for Railway Oriented Programming patterns.

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [API Reference](#api-reference)
- [Benefits](#benefits)
- [Best Practices](#best-practices)
- [Related Packages](#related-packages)
- [License](#license)

## Installation

```bash
dotnet add package Trellis.Testing
```

## Features

- **FluentAssertions Extensions** - Expressive assertions for `Result<T>`, `Maybe<T>`, and `Error` types
- **Test Builders** - Fluent builders for creating test data
- **Fake Implementations** - In-memory fakes for repositories and infrastructure
- **Readable Tests** - Write tests that read like specifications
- **IntelliSense Support** - Discover test utilities through IntelliSense

## Quick Start

### Result Assertions

```csharp
using Trellis.Testing;

[Fact]
public void Should_Return_Success()
{
    var result = EmailAddress.TryCreate("user@example.com");

    result.Should()
        .BeSuccess()
        .Which.Value.Should().Contain("@");
}

[Fact]
public void Should_Return_NotFound_Error()
{
    var result = _repository.GetByIdAsync(userId, ct);

    result.Should()
        .BeFailureOfType<NotFoundError>()
        .Which.Should()
        .HaveDetail("User not found");
}
```

### Validation Error Assertions

```csharp
[Fact]
public void Should_Have_Multiple_Validation_Errors()
{
    var result = CreateUser("", "invalid-email", 15);

    result.Should()
        .BeFailureOfType<ValidationError>()
        .Which.Should()
        .HaveFieldCount(3)
        .And.HaveFieldError("firstName")
        .And.HaveFieldError("email")
        .And.HaveFieldErrorWithDetail("age", "Must be 18 or older");
}
```

### Maybe Assertions

```csharp
[Fact]
public void Should_Have_Value()
{
    var maybe = Maybe.From("hello");

    maybe.Should()
        .HaveValue()
        .Which.Should().Be("hello");
}

[Fact]
public void Should_Be_None()
{
    var maybe = Maybe<string>.None;

    maybe.Should().BeNone();
}
```

### Test Builders

```csharp
[Fact]
public void Should_Handle_NotFound()
{
    var result = ResultBuilder.NotFound<User>("User not found");

    result.Should()
        .BeFailureOfType<NotFoundError>();
}

[Fact]
public void Should_Build_Complex_Validation_Error()
{
    var error = ValidationErrorBuilder.Create()
        .WithFieldError("email", "Email is required")
        .WithFieldError("email", "Invalid email format")
        .WithFieldError("age", "Must be 18 or older")
        .Build();

    error.Should()
        .HaveFieldCount(2)
        .And.HaveFieldErrorWithDetail("email", "Email is required");
}
```

### Fake Repository

```csharp
public class UserServiceTests
{
    private readonly FakeRepository<User, UserId> _fakeRepository;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _fakeRepository = new FakeRepository<User, UserId>();
        _sut = new UserService(_fakeRepository);
    }

    [Fact]
    public async Task Should_Save_User_And_Publish_Event()
    {
        // Arrange
        var command = new CreateUserCommand("John", "Doe", "john@example.com");

        // Act
        var result = await _sut.CreateUserAsync(command, CancellationToken.None);

        // Assert
        result.Should().BeSuccess();
        
        _fakeRepository.Exists(result.Value.Id).Should().BeTrue();
        _fakeRepository.PublishedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserCreatedEvent>();
    }
}
```

### TestActorProvider — Scoped Actor Switching

Eliminates `try/finally/ResetToAdmin()` boilerplate in authorization tests. `WithActor` temporarily replaces the current actor and restores it on dispose.

```csharp
using Trellis.Testing.Fakes;

public class OrderAuthorizationTests
{
    private readonly TestActorProvider _actorProvider = new("admin", "Orders.Read", "Orders.Write");

    [Fact]
    public async Task CreateOrder_WithoutWritePermission_ReturnsForbidden()
    {
        await using var scope = _actorProvider.WithActor("user-1", "Orders.Read");

        var result = await _mediator.Send(new CreateOrderCommand());

        result.Should().BeFailure();
        // scope auto-restores admin actor on dispose
    }

    [Fact]
    public async Task CreateOrder_WithWritePermission_ReturnsSuccess()
    {
        // admin actor already has Orders.Write — no scope needed
        var result = await _mediator.Send(new CreateOrderCommand());

        result.Should().BeSuccess();
    }
}
```

### ReplaceResourceLoader — DI Registration Replacement

When the ACL layer registers `IResourceLoader<TCommand, TResource>` and the test fixture calls `AddMockAntiCorruptionLayer()`, you get duplicate DI registrations. `ReplaceResourceLoader` handles removal and re-registration in a single call. Registered as scoped, matching the production lifetime.

```csharp
using Trellis.Testing;

// Stateless fake — capture a pre-created instance
var fakeLoader = new FakeOrderResourceLoader(fakeRepo);
builder.ConfigureServices(services =>
{
    services.ReplaceResourceLoader<CancelOrderCommand, Order>(_ => fakeLoader);
});

// Scoped dependency — resolve from the container
builder.ConfigureServices(services =>
{
    services.ReplaceResourceLoader<CancelOrderCommand, Order>(
        sp => new FakeOrderResourceLoader(sp.GetRequiredService<AppDbContext>()));
});
```

### CreateClientWithActor — Integration Test HttpClient

Eliminates the copy-pasted helper that creates an `HttpClient` with an `X-Test-Actor` header for authorization integration tests.

```csharp
using Trellis.Testing;

public class OrderEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrderEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory;

    [Fact]
    public async Task CreateOrder_WithPermission_ReturnsCreated()
    {
        var client = _factory.CreateClientWithActor("user-1", "Orders.Create", "Orders.Read");

        var response = await client.PostAsync("/api/orders", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

## API Reference

### Result Assertions

| Method | Description |
|--------|-------------|
| `BeSuccess()` | Asserts the result is a success |
| `BeFailure()` | Asserts the result is a failure |
| `BeFailureOfType<TError>()` | Asserts the result failed with a specific error type |
| `HaveValue(expected)` | Asserts the success value equals the expected value |
| `HaveValueMatching(predicate)` | Asserts the success value satisfies a predicate |
| `HaveValueEquivalentTo(expected)` | Asserts the success value is structurally equivalent |
| `HaveErrorCode(code)` | Asserts the failure has a specific error code |
| `HaveErrorDetail(detail)` | Asserts the failure has a specific error detail |
| `HaveErrorDetailContaining(substring)` | Asserts the failure error detail contains a substring |

### Async Result Assertions

| Method | Description |
|--------|-------------|
| `BeSuccessAsync()` | Asserts the async `Task<Result<T>>` or `ValueTask<Result<T>>` is a success |
| `BeFailureAsync()` | Asserts the async result is a failure |
| `BeFailureOfTypeAsync<TError>()` | Asserts the async result failed with a specific error type |

### Error Assertions

| Method | Description |
|--------|-------------|
| `Be(expected)` | Asserts the error equals the expected error (by code) |
| `HaveCode(code)` | Asserts the error has the specified code |
| `HaveDetail(detail)` | Asserts the error has the specified detail message |
| `HaveDetailContaining(substring)` | Asserts the error detail contains a substring |
| `HaveInstance(instance)` | Asserts the error has the specified instance identifier |
| `BeOfType<TError>()` | Asserts the error is of a specific type |

### ValidationError Assertions

| Method | Description |
|--------|-------------|
| `HaveFieldError(fieldName)` | Asserts the validation error contains a field error |
| `HaveFieldErrorWithDetail(field, detail)` | Asserts a field has a specific error detail |
| `HaveFieldCount(count)` | Asserts the number of field errors |

### Maybe Assertions

| Method | Description |
|--------|-------------|
| `HaveValue()` | Asserts the Maybe has a value |
| `BeNone()` | Asserts the Maybe has no value |
| `HaveValueEqualTo(expected)` | Asserts the value equals the expected value |
| `HaveValueMatching(predicate)` | Asserts the value satisfies a predicate |
| `HaveValueEquivalentTo(expected)` | Asserts the value is structurally equivalent |

### TestActorProvider (`Trellis.Testing.Fakes`)

| Method | Description |
|--------|-------------|
| `new TestActorProvider(actor)` | Creates a provider with the specified `Actor` |
| `new TestActorProvider(userId, permissions)` | Creates a provider from user ID and permission strings |
| `WithActor(actor)` | Temporarily switches to the given `Actor`; returns `TestActorScope` |
| `WithActor(userId, permissions)` | Temporarily switches actor; returns `TestActorScope` |
| `GetCurrentActor()` | Returns the current actor (`IActorProvider` implementation) |

`TestActorScope` implements both `IAsyncDisposable` and `IDisposable`. Nested scopes restore the correct actor at each level.

### ServiceCollection Extensions

| Method | Description |
|--------|-------------|
| `ReplaceResourceLoader<TMessage, TResource>(factory)` | Removes all existing `IResourceLoader<TMessage, TResource>` registrations and re-registers using a scoped factory (matching production lifetime) |

### WebApplicationFactory Extensions

| Method | Description |
|--------|-------------|
| `CreateClientWithActor(actorId, permissions)` | Creates an `HttpClient` with the `X-Test-Actor` header set to a JSON payload containing the actor ID and permissions |

## Benefits

| Before | After |
|--------|-------|
| `result.IsSuccess.Should().BeTrue()` | `result.Should().BeSuccess()` |
| `result.Error.Should().BeOfType<NotFoundError>()` | `result.Should().BeFailureOfType<NotFoundError>()` |
| `maybe.HasValue.Should().BeTrue()` | `maybe.Should().HaveValue()` |
| Manual repository setup with mocks | `new FakeRepository<User, UserId>()` |

**Advantages:**
- **Less Code** - More concise assertions
- **Better Error Messages** - Detailed failure descriptions
- **IntelliSense Guided** - Discover test patterns through IDE
- **Readable Tests** - Tests read like specifications
- **Type Safe** - Compiler-enforced correctness

## Best Practices

1. **Prefer specific assertions** - Use `BeFailureOfType<NotFoundError>()` instead of `BeFailure()` followed by manual casting
2. **Use builders for complex test data** - `ValidationErrorBuilder` and `ResultBuilder` produce consistent, readable setups
3. **Assert error details** - Use `HaveErrorDetail()` or `HaveFieldErrorWithDetail()` to verify error messages, not just error types
4. **Use `FakeRepository` over mocks** - Provides realistic in-memory behavior without mock configuration boilerplate
5. **Verify published domain events** - Check `FakeRepository.PublishedEvents` to confirm side effects
6. **Test both success and failure paths** - Every `Result<T>` operation should have tests for both tracks

## Related Packages

- [Trellis.Results](https://www.nuget.org/packages/Trellis.Results) — Core `Result<T>` and `Maybe<T>` types
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — DDD building blocks
- [Trellis.Authorization](https://www.nuget.org/packages/Trellis.Authorization) — `Actor`, `IActorProvider`, authorization primitives

## License

MIT — see [LICENSE](../LICENSE) for details.
