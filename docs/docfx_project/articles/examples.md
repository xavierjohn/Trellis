# Examples

This article is a pattern library for real application code. Each section starts with the problem, then shows a working Trellis approach you can adapt.

If you want the big-picture tutorial first, read [Introduction](intro.md) and [Basics](basics.md) before coming back here.

## Real-world sample projects

The repository includes full examples you can browse after these snippets click:

- [`Showcase`](https://github.com/xavierjohn/Trellis/tree/main/Examples/Showcase) — one banking domain hosted twice (MVC + Minimal API) with full Result/Error walkthrough, `TimeProvider`, lifecycle state machine, and integration tests for both hosting styles.
- [`ConditionalRequestExample`](https://github.com/xavierjohn/Trellis/tree/main/Examples/ConditionalRequestExample) — RFC 9110 conditional requests (`If-Match` / `If-None-Match`) with strong ETags.
- [`SsoExample`](https://github.com/xavierjohn/Trellis/tree/main/Examples/SsoExample) — `AddDevelopmentActorProvider()` and `AddClaimsActorProvider()` wired side-by-side.
- [`EfCoreExample`](https://github.com/xavierjohn/Trellis/tree/main/Examples/EfCoreExample) — VO ID conversions, automatic timestamps, value-object composition over `DbContext`.
- [`TestingPatterns`](https://github.com/xavierjohn/Trellis/tree/main/Examples/TestingPatterns) — async, parallel, `Maybe`, `EquatableArray`, and validating-by-result patterns.

---

## Example 1: validate a registration request without losing readability

**Problem:** you want to validate multiple fields, enforce a duplicate-email rule, and only run side effects when everything succeeds.

```csharp
using Trellis;

public partial class FirstName : RequiredString<FirstName> { }
public partial class LastName : RequiredString<LastName> { }
public partial class CustomerEmail : RequiredString<CustomerEmail> { }

public sealed record RegisterUserRequest(string FirstName, string LastName, string Email);

public sealed record User(FirstName FirstName, LastName LastName, CustomerEmail Email)
{
    public static Result<User> TryCreate(FirstName firstName, LastName lastName, CustomerEmail email) =>
        Result.Ok(new User(firstName, lastName, email));
}

public static Result<User> RegisterUser(
    RegisterUserRequest request,
    Func<CustomerEmail, bool> emailExists,
    Action<User> saveUser,
    Action<CustomerEmail> sendWelcomeEmail)
{
    return FirstName.TryCreate(request.FirstName)
        .Combine(LastName.TryCreate(request.LastName))
        .Combine(CustomerEmail.TryCreate(request.Email, fieldName: "email"))
        .Bind((firstName, lastName, email) => User.TryCreate(firstName, lastName, email))
        .Ensure(user => !emailExists(user.Email), new Error.Conflict(null, "conflict") { Detail = "Email already registered." })
        .Tap(saveUser)
        .Tap(user => sendWelcomeEmail(user.Email));
}
```

Why this works well:

- `Combine` keeps independent validation together
- `Bind` moves from validated inputs to a domain object
- `Ensure` adds a business rule
- `Tap` keeps side effects on the success path

---

## Example 2: return the right HTTP response from MVC without a giant switch statement

**Problem:** controllers should be thin, but hand-mapping every result to an HTTP response gets repetitive fast.

This example is based on the sample MVC application in the repository.

```csharp
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

[ApiController]
[Route("users")]
public sealed class UsersController : ControllerBase
{
    [HttpPost]
    public ActionResult<UserResponse> Register([FromBody] RegisterUserRequest request)
    {
        Result<UserResponse> result = RegisterCore(request);
        return result.ToHttpResponse().AsActionResult<UserResponse>();
    }

    private static Result<UserResponse> RegisterCore(RegisterUserRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Email)
            ? new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required." }))
            : Result.Ok(new UserResponse(request.Email));
    }
}

public sealed record RegisterUserRequest(string Email);
public sealed record UserResponse(string Email);
```

`ToHttpResponse().AsActionResult<T>()` handles the common success/failure mapping while preserving a typed MVC return signature.

For the full integration guide, see [ASP.NET Core Integration](integration-aspnet.md).

---

## Example 3: do the same thing in Minimal APIs

**Problem:** Minimal APIs are concise, but you still want Trellis errors to map cleanly to HTTP results.

```csharp
using Microsoft.AspNetCore.Builder;
using Trellis;
using Trellis.Asp;

public static class UserRoutes
{
    public static void MapUserRoutes(this WebApplication app)
    {
        app.MapPost("/users", (RegisterUserRequest request) =>
            RegisterCore(request).ToHttpResponse());
    }

    private static Result<UserResponse> RegisterCore(RegisterUserRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Email)
            ? new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Email is required." }))
            : Result.Ok(new UserResponse(request.Email));
    }
}

public sealed record RegisterUserRequest(string Email);
public sealed record UserResponse(string Email);
```

When your endpoint is already expressed as a `Result<T>`, `ToHttpResponse()` keeps the web layer very small.

---

## Example 4: fetch independent dashboard data in parallel

**Problem:** dashboards often need several unrelated queries, and doing them sequentially makes the endpoint slower for no good reason.

This example mirrors the sample dashboard endpoint in the repo.

```csharp
using Trellis;

public sealed record DashboardResponse(int ProductCount, int OrderCount, decimal TotalRevenue);

static Task<Result<int>> GetProductCountAsync() => Task.FromResult(Result.Ok(42));
static Task<Result<int>> GetOrderCountAsync() => Task.FromResult(Result.Ok(12));
static Task<Result<decimal>> GetRevenueAsync() => Task.FromResult(Result.Ok(1850.50m));

Result<DashboardResponse> result = await Result.ParallelAsync(
        () => GetProductCountAsync(),
        () => GetOrderCountAsync(),
        () => GetRevenueAsync())
    .WhenAllAsync()
    .MapAsync((productCount, orderCount, totalRevenue) =>
        new DashboardResponse(productCount, orderCount, totalRevenue));
```

Use this pattern when:

- the work is independent
- latency matters
- you still want one combined `Result<T>` at the end

> [!TIP]
> `ParallelAsync` starts the operations together. `WhenAllAsync()` waits for them and gives you a single result to continue the pipeline.

---

## Example 5: reuse FluentValidation instead of duplicating rules

**Problem:** teams often end up validating once in the domain and again in the API. That is tedious and easy to drift out of sync.

```csharp
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public sealed record CreateCustomer(string Email);

var validator = new InlineValidator<CreateCustomer>();
validator.RuleFor(x => x.Email).NotEmpty().EmailAddress();

Result<CreateCustomer> result = validator.ValidateToResult(new CreateCustomer("user@example.com"));
```

If validation fails, the result becomes a `Error.UnprocessableContent` with field-level details. That makes it easy to carry the same rule set from your application layer to HTTP responses.

See [FluentValidation Integration](integration-fluentvalidation.md) for a full walkthrough.

---

## Example 6: recover from a failure when a fallback is acceptable

**Problem:** sometimes failure should not be the end of the story. A cache miss can fall back to the database, or a secondary provider can take over.

```csharp
using Trellis;

public sealed record PricingSnapshot(string Source);

Result<PricingSnapshot> cacheResult = new Error.NotFound(ResourceRef.For("Price")) { Detail = "Price not found in cache." };
Result<PricingSnapshot> databaseResult = Result.Ok(new PricingSnapshot("database"));

Result<PricingSnapshot> finalResult = cacheResult.RecoverOnFailure(
    predicate: error => error is Error.NotFound,
    func: _ => databaseResult);
```

This keeps the fallback logic explicit and local to the workflow.

---

## Example 7: handle async side effects and notifications cleanly

**Problem:** async application code becomes noisy when loading data, checking rules, mutating state, and notifying another system all happen together.

```csharp
using Trellis;

public sealed class Customer
{
    public Customer(string email, bool canBePromoted)
    {
        Email = email;
        CanBePromoted = canBePromoted;
    }

    public string Email { get; }
    public bool CanBePromoted { get; }
    public Task PromoteAsync() => Task.CompletedTask;
}

static Task<Customer?> GetCustomerByIdAsync(long id) =>
    Task.FromResult(id == 1 ? new Customer("customer@example.com", true) : null);

static Task<Result> SendPromotionNotificationAsync(string email) =>
    Task.FromResult(Result.Ok(new Unit()));

string message = await GetCustomerByIdAsync(1)
    .ToResultAsync(new Error.NotFound(ResourceRef.For("Customer", 1)) { Detail = "Customer not found." })
    .EnsureAsync(customer => customer.CanBePromoted, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Customer cannot be promoted." })
    .TapAsync(customer => customer.PromoteAsync())
    .BindAsync(customer => SendPromotionNotificationAsync(customer.Email))
    .MatchAsync(
        onSuccess: _ => "Promotion completed.",
        onFailure: error => error.Detail);
```

The shape is the same as the synchronous version. That is one of the nicest parts of Trellis.

---

## Example 8: branch on specific error types when the caller cares

**Problem:** some endpoints need different behavior for validation failures, missing data, and unexpected faults.

```csharp
using Microsoft.AspNetCore.Http;
using Trellis;

Result<string> result = new Error.NotFound(ResourceRef.For("Order")) { Detail = "Order not found." };

IResult httpResult = result.Match(
    onSuccess: order => Results.Ok(order),
    onFailure: error => error switch
    {
        Error.UnprocessableContent uc => Results.UnprocessableEntity(uc.Fields.Items),
        Error.NotFound nf             => Results.NotFound(nf.Detail),
        Error.Conflict c              => Results.Conflict(c.Detail),
        _                              => Results.StatusCode(StatusCodes.Status500InternalServerError)
    });
```

This is especially handy when you want to shape the HTTP response yourself instead of delegating to `ToHttpResponse()`.

---

## A few patterns worth memorizing

| Need | Pattern |
| --- | --- |
| Validate several incoming fields | `A.TryCreate(...).Combine(B.TryCreate(...))` |
| Move from validated input to domain logic | `.Bind(...)` |
| Add a business rule | `.Ensure(..., new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = ... })` |
| Save or notify on success | `.Tap(...)` / `.TapAsync(...)` |
| Fallback on acceptable failures | `.RecoverOnFailure(...)` |
| Return HTTP from MVC or Minimal API | `.ToHttpResponse()` / `.ToHttpResponse().AsActionResult<T>()` |
| Branch by concrete error type | `result.Match(_, e => e switch { Error.X => ..., ... })` |

## Where to go next

- Need the operator-by-operator tutorial? Read [Basics](basics.md)
- Want the reasoning behind the framework? Read [Introduction](intro.md)
- Building APIs? Read [ASP.NET Core Integration](integration-aspnet.md)
- Need exact signatures? Use the [API reference](../api/index.md)
