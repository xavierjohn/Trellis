# Testing

**Level:** Intermediate 📘 | **Time:** 20-30 min | **Prerequisites:** [Basics](basics.md), [Mediator Pipeline](integration-mediator.md)

Good Trellis tests read like the production code they protect: success and failure are values, authorization is explicit, and integration tests exercise the real pipeline.

`Trellis.Testing` gives you purpose-built assertions, fakes, and test helpers for that style.

## Installation

```bash
dotnet add package Trellis.Testing
```

## What problem does this solve?

The package helps you avoid common test friction:

- noisy assertions against `Result<T>` and `Maybe<T>`
- brittle repository mocks
- hand-built actor headers for integration tests
- repeated boilerplate for test errors and validation failures

## Start here: result assertions

When a handler returns `Result<T>`, assert on the result directly instead of unpacking booleans yourself.

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var success = Result.Ok(42);
success.Should().BeSuccess().Which.Should().Be(42);

var failure = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", "123")) { Detail = "Order 123 not found" });
failure.Should().BeFailureOfType<Error.NotFound>();
failure.Should().HaveErrorCode("not.found.error");
failure.Should().HaveErrorDetail("Order 123 not found");
```

### Async assertions

This detail matters: async assertions are extensions on `Task<Result<T>>` and `ValueTask<Result<T>>` directly.

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

Task<Result<int>> taskResult = Task.FromResult(Result.Ok(42));
ValueTask<Result<int>> valueTaskResult = ValueTask.FromResult(Result.Ok(7));

(await taskResult.BeSuccessAsync()).Which.Should().Be(42);
(await valueTaskResult.BeSuccessAsync()).Which.Should().Be(7);
```

> [!WARNING]
> Do **not** write `await result.Should().BeSuccessAsync()`. The async helpers are not extensions on `ResultAssertions<T>`.

## `Maybe<T>` and `Error` assertions

Use the Trellis-specific assertions so failures explain the intent of the test.

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var maybeName = Maybe.From("Ada");
maybeName.Should().HaveValue().Which.Should().Be("Ada");

var none = Maybe<string>.None;
none.Should().BeNone();

var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required." }));
error.Should().HaveCode("validation.error");
error.Should().HaveDetailContaining("required");
```

## `FakeRepository`: fast handler tests without mocks

If you are testing application handlers, `FakeRepository<TAggregate, TId>` is usually better than mocking repository calls by hand.

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

public sealed record OrderId(Guid Value);

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id, string email) : base(id) => Email = email;

    public string Email { get; }
}

var repo = new FakeRepository<Order, OrderId>()
    .WithUniqueConstraint(order => order.Email);

var order = new Order(new OrderId(Guid.NewGuid()), "ada@example.com");

await repo.SaveAsync(order).BeSuccessAsync();
(await repo.GetByIdAsync(order.Id)).Should().BeSuccess().Which.Should().BeSameAs(order);
repo.Exists(order.Id).Should().BeTrue();
repo.Count.Should().Be(1);
```

Important details from the current API:

- `SaveAsync` returns `Task<Result<Unit>>`
- `DeleteAsync` returns `Task<Result<Unit>>`
- unique constraint conflicts return `Error.Conflict`
- missing aggregates use details like `"{AggregateTypeName} with ID {id} not found"`

## `TestActorProvider`: authorization tests without plumbing

When you want to test permission checks or resource ownership, use `TestActorProvider`.

```csharp
using FluentAssertions;
using Trellis.Authorization;
using Trellis.Testing;

var actorProvider = new TestActorProvider("admin", "Orders.Read", "Orders.Write");

await using var scope = actorProvider.WithActor("user-1", "Orders.Read");
var actor = await actorProvider.GetCurrentActorAsync();

actor.Id.Should().Be("user-1");
actor.HasPermission("Orders.Read").Should().BeTrue();
actor.HasPermission("Orders.Write").Should().BeFalse();
```

There is also an overload that accepts a full `Actor`, which is useful when you need:

- `ForbiddenPermissions`
- `Attributes`

## Building failures for tests

Use the standard `Result` and `Error` factory methods directly — no special test builder needed.

```csharp
using Trellis;

var success = Result.Ok(42);
var notFound = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", "123")) { Detail = "Order 123 not found" });
var forbidden = Result.Fail<int>(new Error.Forbidden("policy.id") { Detail = "Not allowed." });
```

`new Error.NotFound(new ResourceRef("Resource", "123")) { Detail = "Order 123 not found" }` produces:

- detail: `Order 123 not found`
- instance: `123`

## HTTP integration tests with actor headers

When your app uses `DevelopmentActorProvider`, `CreateClientWithActor` is the easiest way to exercise authorization through the HTTP pipeline.

### Convenience overload

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Testing.AspNetCore;

public sealed class Program
{
}

WebApplicationFactory<Program> factory = default!;

var client = factory.CreateClientWithActor("user-1", "Orders.Create", "Orders.Read");
```

### Full `Actor` overload

Use this when the test needs more than id + granted permissions.

```csharp
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Authorization;
using Trellis.Testing.AspNetCore;

public sealed class Program
{
}

WebApplicationFactory<Program> factory = default!;

var actor = new Actor(
    id: "user-1",
    permissions: new HashSet<string> { "Orders.Read" },
    forbiddenPermissions: new HashSet<string> { "Orders.Delete" },
    attributes: new Dictionary<string, string> { ["tenant"] = "acme" });

var client = factory.CreateClientWithActor(actor);
```

> [!NOTE]
> The `X-Test-Actor` header includes `Id`, `Permissions`, `ForbiddenPermissions`, and `Attributes`. That matches what `DevelopmentActorProvider` expects.

## Replacing infrastructure in integration tests

Two helpers are especially useful in `WebApplicationFactory`-based tests.

### Swap the EF Core provider

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Testing.AspNetCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}

public sealed class Program
{
}

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureServices(services =>
            services.ReplaceDbProvider<AppDbContext>(options => options.UseSqlite(_connection)));
    }

    protected override void Dispose(bool disposing)
    {
        _connection.Dispose();
        base.Dispose(disposing);
    }
}
```

> [!WARNING]
> `ReplaceDbProvider<TContext>` re-registers the context via `AddDbContext<TContext>`. If your app uses `AddDbContextFactory` or `AddPooledDbContextFactory`, replace those registrations directly instead.

### Replace a resource loader

```csharp
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Testing.AspNetCore;

public sealed record GetOrderQuery(string OrderId);
public sealed record OrderResource(string Id);

public sealed class FakeOrderLoader : IResourceLoader<GetOrderQuery, OrderResource>
{
    public Task<Result<OrderResource>> LoadAsync(
        GetOrderQuery message,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Ok(new OrderResource(message.OrderId)));
}

var services = new ServiceCollection();
services.ReplaceResourceLoader<GetOrderQuery, OrderResource>(_ => new FakeOrderLoader());
```

## Practical guidance

### Prefer fakes over mocks for repositories

`FakeRepository` behaves more like the real thing:

- not-found behavior
- unique constraints
- domain event capture

### Assert on error codes when it matters

Trellis default error codes end in `.error`, which makes them good assertion targets.

### Use the actor overload when authorization rules are richer

If your policy uses forbidden permissions or attributes, pass a real `Actor` to `CreateClientWithActor`.

### Keep Entra token tests separate

Use `CreateClientWithActor` for fast local and CI tests. Use real tokens only when you need to validate the full authentication pipeline.

## Next steps

- [Testing with Azure Entra ID Tokens](integration-entra-testing.md)
- [Mediator Pipeline](integration-mediator.md)
- [trellis-api-testing-reference.md](../api_reference/trellis-api-testing-reference.md)
