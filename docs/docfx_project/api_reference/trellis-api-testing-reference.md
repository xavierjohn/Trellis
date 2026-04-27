# Trellis.Testing — API Reference

- **Package:** `Trellis.Testing`
- **Namespace:** `Trellis.Testing`
- **Purpose:** FluentAssertions extensions, unwrap helpers, and test doubles (FakeRepository, TestActorProvider) for Trellis applications.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

> **ASP.NET Core integration test helpers** (WebApplicationFactory, DI replacement, MSAL tokens) are in a separate package: [`Trellis.Testing.AspNetCore`](#trellistestingaspnetcore--api-reference).

## Types

### Namespace `Trellis.Testing`

#### `ResultAssertionsExtensions`
```csharp
public static class ResultAssertionsExtensions
{
    public static ResultAssertions<TValue> Should<TValue>(this Result<TValue> result);
}
```

#### `ResultAssertions<TValue>`
```csharp
public class ResultAssertions<TValue> : ReferenceTypeAssertions<Result<TValue>, ResultAssertions<TValue>>
{
    public ResultAssertions(Result<TValue> result);

    public AndWhichConstraint<ResultAssertions<TValue>, TValue> BeSuccess(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<ResultAssertions<TValue>, Error> BeFailure(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<ResultAssertions<TValue>, TError> BeFailureOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error;

    public AndConstraint<ResultAssertions<TValue>> HaveValue(
        TValue expectedValue,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveValueMatching(
        Func<TValue, bool> predicate,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveValueEquivalentTo(
        TValue expectedValue,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveErrorCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveErrorDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> HaveErrorDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> Be(
        Result<TValue> expected,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ResultAssertions<TValue>> NotBe(
        Result<TValue> unexpected,
        string because = "",
        params object[] becauseArgs);
}
```

#### `NonGenericResultAssertionsExtensions`
```csharp
public static class NonGenericResultAssertionsExtensions
{
    public static NonGenericResultAssertions Should(this Result result);
}
```

#### `NonGenericResultAssertions`
```csharp
public class NonGenericResultAssertions : ReferenceTypeAssertions<Result, NonGenericResultAssertions>
{
    public NonGenericResultAssertions(Result result);

    public AndConstraint<NonGenericResultAssertions> BeSuccess(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<NonGenericResultAssertions, Error> BeFailure(
        string because = "",
        params object[] becauseArgs);

    public AndWhichConstraint<NonGenericResultAssertions, TError> BeFailureOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error;

    public AndConstraint<NonGenericResultAssertions> HaveErrorCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<NonGenericResultAssertions> HaveErrorDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<NonGenericResultAssertions> HaveErrorDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs);
}
```

#### `ResultAssertionsAsyncExtensions`
```csharp
public static class ResultAssertionsAsyncExtensions
{
    public static Task<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static Task<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static Task<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this Task<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
        where TError : Error;

    public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TValue>> BeSuccessAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, Error>> BeFailureAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs);

    public static ValueTask<AndWhichConstraint<ResultAssertions<TValue>, TError>> BeFailureOfTypeAsync<TValue, TError>(
        this ValueTask<Result<TValue>> resultTask,
        string because = "",
        params object[] becauseArgs)
        where TError : Error;
}
```

#### `MaybeAssertionsExtensions`
```csharp
public static class MaybeAssertionsExtensions
{
    public static MaybeAssertions<T> Should<T>(this Maybe<T> maybe)
        where T : notnull;
}
```

#### `MaybeAssertions<T>`
```csharp
public class MaybeAssertions<T> : ReferenceTypeAssertions<Maybe<T>, MaybeAssertions<T>>
    where T : notnull
{
    public MaybeAssertions(Maybe<T> maybe);

    public AndWhichConstraint<MaybeAssertions<T>, T> HaveValue(
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> BeNone(
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> HaveValueEqualTo(
        T expectedValue,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> HaveValueMatching(
        Func<T, bool> predicate,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<MaybeAssertions<T>> HaveValueEquivalentTo(
        T expectedValue,
        string because = "",
        params object[] becauseArgs);
}
```

#### `ErrorAssertionsExtensions`
```csharp
public static class ErrorAssertionsExtensions
{
    public static ErrorAssertions Should(this Error? error);
}
```

#### `ErrorAssertions`
```csharp
public class ErrorAssertions : ReferenceTypeAssertions<Error, ErrorAssertions>
{
    public ErrorAssertions(Error error);

    public AndConstraint<ErrorAssertions> Be(
        Error expected,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ErrorAssertions> HaveCode(
        string expectedCode,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ErrorAssertions> HaveDetail(
        string expectedDetail,
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ErrorAssertions> HaveDetailContaining(
        string substring,
        string because = "",
        params object[] becauseArgs);

    public new AndWhichConstraint<ErrorAssertions, TError> BeOfType<TError>(
        string because = "",
        params object[] becauseArgs)
        where TError : Error;
}
```

> **v2 note:** The `HaveInstance(...)` assertion was removed. `Error.Instance` is no longer part of the closed-ADT base — the ASP wire layer synthesizes `ProblemDetails.Instance` from the request URL plus any `ResourceRef` carried by the typed payload (e.g. `Error.NotFound.Resource`). Assert against `ResourceRef` directly via `BeOfType<Error.NotFound>().Which.Resource`.

#### `ValidationErrorAssertionsExtensions`
```csharp
public static class ValidationErrorAssertionsExtensions
{
    // Bound to Error.UnprocessableContent (the v2 replacement for v1's ValidationError class).
    // Method names preserved for source-compat at test sites.
    public static ValidationErrorAssertions Should(this Error.UnprocessableContent error);
}
```

#### `ValidationErrorAssertions`
```csharp
public class ValidationErrorAssertions : ReferenceTypeAssertions<Error.UnprocessableContent, ValidationErrorAssertions>
{
    public ValidationErrorAssertions(Error.UnprocessableContent error);

    public AndConstraint<ValidationErrorAssertions> HaveFieldError(
        string fieldName,                              // accepted as either "email" or "/email" — normalized via InputPointer.ForProperty
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ValidationErrorAssertions> HaveFieldErrorWithDetail(
        string fieldName,
        string expectedDetail,                         // matches FieldViolation.Detail exactly
        string because = "",
        params object[] becauseArgs);

    public AndConstraint<ValidationErrorAssertions> HaveFieldCount(
        int expectedCount,                             // counts distinct field paths
        string because = "",
        params object[] becauseArgs);
}
```

#### `UnwrapExtensions`
```csharp
public static class UnwrapExtensions
{
    public static T Unwrap<T>(this Result<T> result);

    public static Error UnwrapError<T>(this Result<T> result);

    public static Error UnwrapError(this Result result);

    public static T Unwrap<T>(this Maybe<T> maybe)
        where T : notnull;

    public static Task<T> UnwrapAsync<T>(this Task<Result<T>> resultTask);

    public static ValueTask<T> UnwrapAsync<T>(this ValueTask<Result<T>> resultTask);
}
```

#### `UnwrapFailedException`
```csharp
public sealed class UnwrapFailedException : Exception
{
    public UnwrapFailedException();
    public UnwrapFailedException(string message);
    public UnwrapFailedException(string message, Exception innerException);
}
```

#### `AggregateTestMutator`
```csharp
public static class AggregateTestMutator
{
    public static TEntity SetMaybeField<TEntity, TValue>(
        this TEntity entity,
        Expression<Func<TEntity, Maybe<TValue>>> propertySelector,
        TValue? value)
        where TEntity : class
        where TValue : notnull;

    public static TEntity ClearMaybeField<TEntity, TValue>(
        this TEntity entity,
        Expression<Func<TEntity, Maybe<TValue>>> propertySelector)
        where TEntity : class
        where TValue : notnull;
}
```

#### `FakeRepository<TAggregate, TId>`
```csharp
public class FakeRepository<TAggregate, TId>
    where TAggregate : Aggregate<TId>
    where TId : notnull
{
    public IReadOnlyList<IDomainEvent> PublishedEvents { get; }
    public int Count { get; }

    public FakeRepository<TAggregate, TId> WithUniqueConstraint(Func<TAggregate, object?> propertySelector);

    public Task<Result<TAggregate>> GetByIdAsync(TId id, CancellationToken cancellationToken = default);
    public Task<Maybe<TAggregate>> FindByIdAsync(TId id, CancellationToken cancellationToken = default);

    // Setup surface — mirrors RepositoryBase<TAggregate, TId>. Use these in handlers and
    // in test setup so the same IRepository contract works in both the EF and fake paths.
    public void Add(TAggregate aggregate);
    public void Remove(TAggregate aggregate);
    public Task<Result> RemoveByIdAsync(TId id, CancellationToken cancellationToken = default);

    // Result-shape surface — only on the fake. Reserve for tests that explicitly assert
    // on Result-of-save shape (e.g., conflict-result handling). NOT part of RepositoryBase.
    public Task<Result> SaveAsync(TAggregate aggregate, CancellationToken cancellationToken = default);
    public Task<Result> DeleteAsync(TId id, CancellationToken cancellationToken = default);

    public void Clear();
    public bool Exists(TId id);
    public TAggregate? Get(TId id);
    public IEnumerable<TAggregate> GetAll();

    public Task<Maybe<TAggregate>> FindAsync(Func<TAggregate, bool> predicate);
    public Task<IReadOnlyList<TAggregate>> WhereAsync(Func<TAggregate, bool> predicate);
    public Task<IReadOnlyList<TAggregate>> WhereAsync(Specification<TAggregate> specification);
}
```

#### `FakeSharedResourceLoader<TResource, TId>`
```csharp
public class FakeSharedResourceLoader<TResource, TId> : SharedResourceLoaderById<TResource, TId>
    where TResource : Aggregate<TId>
    where TId : notnull
{
    public FakeSharedResourceLoader(FakeRepository<TResource, TId> repository);

    public override Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken);
}
```

#### `TestActorProvider`
```csharp
public sealed class TestActorProvider : IActorProvider
{
    public TestActorProvider(Actor actor);
    public TestActorProvider(string userId, params string[] permissions);

    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default);

    public TestActorScope WithActor(Actor actor);
    public TestActorScope WithActor(string userId, params string[] permissions);
}
```

#### `TestActorScope`
```csharp
public sealed class TestActorScope : IAsyncDisposable, IDisposable
{
    public ValueTask DisposeAsync();
    public void Dispose();
}
```

## Usage notes

### Assertions

- Synchronous assertions start from `Result<T>` or `Maybe<T>`:
  - `result.Should().BeSuccess()`
  - `result.Should().BeFailureOfType<Error.UnprocessableContent>()`
  - `maybe.Should().HaveValue()`
- **Async assertions are extension methods on `Task<Result<T>>` and `ValueTask<Result<T>>`, not on `ResultAssertions<T>`.**
  - Correct: `await resultTask.BeSuccessAsync();`
  - Correct: `await valueTaskResult.BeFailureAsync();`
  - Wrong: `await result.Should().BeSuccessAsync();`

### FakeRepository

- **Setup surface** (mirrors `RepositoryBase<TAggregate, TId>` in `Trellis.EntityFrameworkCore`) — use these from handlers and test setup so the same `IRepository` contract works in both worlds:
  - `void Add(TAggregate)` — stages an insert; in the fake, immediately visible. Throws `InvalidOperationException` on unique-constraint violation (setup mistakes should fail loud at the call site).
  - `void Remove(TAggregate)` — stages a delete; no-op if the aggregate is not in the store.
  - `Task<Result> RemoveByIdAsync(TId)` — looks up by ID and removes; returns `Error.NotFound` if missing.
- **Result-shape surface** (only on the fake — `RepositoryBase` does not expose these) — use only when the test specifically asserts on the `Result` of the persistence call:
  - `Task<Result> SaveAsync(TAggregate)` — returns `Error.Conflict` on unique-constraint violation. Use to test conflict-handling code paths.
  - `Task<Result> DeleteAsync(TId)` — returns `Error.NotFound` on missing. Use to test not-found handling. (`RemoveByIdAsync` is the staging-API-named alias.)
- `WithUniqueConstraint(Func<TAggregate, object?> propertySelector)` — fluent constraint registration; checked eagerly by `Add` (throws) and at-call by `SaveAsync` (returns `Result`).
- `Clear()`, `Exists(TId id)`, `Get(TId id)`, `GetAll()`, `Count` — direct inspection helpers
- `GetByIdAsync` / `DeleteAsync` / `RemoveByIdAsync` return `Error.NotFound` details in the format:
  - `"{AggregateTypeName} with ID {id} not found"`
- Unique-constraint conflicts return:
  - `"A {AggregateTypeName} with the same value already exists."`

> See cookbook **Recipe 16 — Unit of work in handlers** for guidance on which surface to use from where, and the pitfall of accidentally calling `SaveAsync` from a production-shaped repository contract.

## Compilable examples

### Result assertions

```csharp
using FluentAssertions;
using Trellis;
using Trellis.Testing;

var success = Result.Ok(42);
success.Should().BeSuccess().Which.Should().Be(42);

var notFound = Result.Fail<int>(new Error.NotFound(new ResourceRef("Order", "123")) { Detail = "Order 123 not found" });
notFound.Should().BeFailure()
    .Which.Detail.Should().Be("Order 123 not found");
```

### Async assertions

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

Task<Result<int>> resultTask = Task.FromResult(Result.Ok(42));
ValueTask<Result<int>> valueTaskResult = ValueTask.FromResult(Result.Ok(7));

(await resultTask.BeSuccessAsync()).Which.Should().Be(42);
(await valueTaskResult.BeSuccessAsync()).Which.Should().Be(7);
```

### FakeRepository

```csharp
using System;
using FluentAssertions;
using Trellis;
using Trellis.Testing;

public sealed record OrderId(Guid Value);

public sealed class Order : Aggregate<OrderId>
{
    public Order(OrderId id) : base(id) { }
}

var repo = new FakeRepository<Order, OrderId>()
    .WithUniqueConstraint(order => order.Id);

var order = new Order(new OrderId(Guid.NewGuid()));

await repo.SaveAsync(order).BeSuccessAsync();
(await repo.GetByIdAsync(order.Id)).Should().BeSuccess().Which.Should().BeSameAs(order);
repo.Exists(order.Id).Should().BeTrue();
repo.Count.Should().Be(1);
```

---

# Trellis.Testing.AspNetCore — API Reference

- **Package:** `Trellis.Testing.AspNetCore`
- **Namespace:** `Trellis.Testing.AspNetCore`
- **Purpose:** ASP.NET Core integration test utilities — WebApplicationFactory helpers, DI service replacement, fake time providers, and MSAL token acquisition.

## Types

### Namespace `Trellis.Testing.AspNetCore`

#### `ServiceCollectionExtensions`
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection ReplaceResourceLoader<TMessage, TResource>(
        this IServiceCollection services,
        Func<IServiceProvider, IResourceLoader<TMessage, TResource>> factory);

    public static IServiceCollection ReplaceSingleton<TService>(
        this IServiceCollection services,
        TService instance)
        where TService : class;
}
```

#### `ServiceCollectionDbProviderExtensions`
```csharp
public static class ServiceCollectionDbProviderExtensions
{
    public static IServiceCollection ReplaceDbProvider<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureOptions)
        where TContext : DbContext;
}
```

#### `WebApplicationFactoryExtensions`
```csharp
public static class WebApplicationFactoryExtensions
{
    public static HttpClient CreateClientWithActor<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        string actorId,
        params string[] permissions)
        where TEntryPoint : class;

    public static HttpClient CreateClientWithActor<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        Actor actor)
        where TEntryPoint : class;

    public static Task<HttpClient> CreateClientWithEntraTokenAsync<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        MsalTestTokenProvider tokenProvider,
        string testUserName,
        CancellationToken cancellationToken = default)
        where TEntryPoint : class;
}
```

#### `WebApplicationFactoryTimeExtensions`
```csharp
public static class WebApplicationFactoryTimeExtensions
{
    public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        FakeTimeProvider fakeTimeProvider)
        where TEntryPoint : class;

    public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        out FakeTimeProvider fakeTimeProvider)
        where TEntryPoint : class;
}
```

#### `MsalTestOptions`
```csharp
public sealed class MsalTestOptions
{
    public string TenantId { get; set; }
    public string ClientId { get; set; }
    public string[] Scopes { get; set; }
    public Dictionary<string, TestUserCredentials> TestUsers { get; set; }
}
```

#### `MsalTestTokenProvider`
```csharp
public sealed class MsalTestTokenProvider
{
    public MsalTestTokenProvider(MsalTestOptions options);

    public Task<string> AcquireTokenAsync(
        string testUserName,
        CancellationToken cancellationToken = default);
}
```

#### `TestUserCredentials`
```csharp
public sealed class TestUserCredentials
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string[] ExpectedPermissions { get; set; }
}
```

## Usage notes

### WebApplicationFactory actor header

- `CreateClientWithActor(string actorId, params string[] permissions)` serializes:
  - `Id`
  - `Permissions`
  - `ForbiddenPermissions` as an empty JSON array
  - `Attributes` as an empty JSON object
- `CreateClientWithActor(Actor actor)` serializes the full actor:
  - `Id`
  - `Permissions`
  - `ForbiddenPermissions`
  - `Attributes`

### Fake time provider

- `WithFakeTimeProvider(out FakeTimeProvider fakeTimeProvider)` creates:
  - `new FakeTimeProvider(DateTimeOffset.UtcNow)`
- It then calls the overload that registers the same singleton `FakeTimeProvider` as `TimeProvider`.

## Compilable examples

### WebApplicationFactory helpers

```csharp
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
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
factory = factory.WithFakeTimeProvider(out FakeTimeProvider fakeTimeProvider);
fakeTimeProvider.SetUtcNow(DateTimeOffset.UtcNow.AddHours(1));
```

## Cross-references

- [trellis-api-core.md](trellis-api-core.md) — `Result<T>`, `Maybe<T>`, `Error`, `Unit`
- [trellis-api-core.md](trellis-api-core.md) — `Aggregate<TId>`, `Specification<T>`
- [trellis-api-authorization.md](trellis-api-authorization.md) — `Actor`, `IActorProvider`, `IResourceLoader`, `SharedResourceLoaderById`
- [trellis-api-efcore.md](trellis-api-efcore.md) — `SaveChangesResultAsync`, `SaveChangesResultUnitAsync`, `AddTrellisInterceptors`
- [trellis-api-asp.md](trellis-api-asp.md) — API/integration patterns used with `WebApplicationFactory`
