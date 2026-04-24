# Trellis Cross-Package Cookbook

- **Audience:** AI coding agents (and humans) writing Trellis code from documentation alone.
- **Purpose:** End-to-end recipes that cross package boundaries — DDD, Mediator, FluentValidation, EF Core, ASP.NET Core, Authorization, State Machine, Testing, Analyzers — using the *exact* public surface listed in the per-package API references.
- **Companion docs:**
  - [trellis-api-core.md](trellis-api-core.md) — `Result<T>`, `Maybe<T>`, errors, primitives, pagination
  - [trellis-api-primitives.md](trellis-api-primitives.md) — `RequiredString`, `RequiredGuid`, `[Range]`, `[StringLength]`
  - [trellis-api-mediator.md](trellis-api-mediator.md) — `ICommand<T>`, `IQuery<T>`, `IPipelineBehavior<,>`, `AddTrellisBehaviors`
  - [trellis-api-fluentvalidation.md](trellis-api-fluentvalidation.md) — `AddTrellisFluentValidation`
  - [trellis-api-efcore.md](trellis-api-efcore.md) — `SaveChangesResultAsync`, `MaybePropertyMapping`, `EfRepositoryBase<>`
  - [trellis-api-asp.md](trellis-api-asp.md) — `ToHttpResponse`, `HttpResponseOptionsBuilder<T>`, `AddTrellisAsp`, `AsActionResult`
  - [trellis-api-http.md](trellis-api-http.md) — `RangeOutcome`, range parser
  - [trellis-api-authorization.md](trellis-api-authorization.md) — `IActorProvider`, `IAuthorize`, `IAuthorizeResource<>`
  - [trellis-api-statemachine.md](trellis-api-statemachine.md) — `FireResult`, `LazyStateMachine<,>`
  - [trellis-api-testing-reference.md](trellis-api-testing-reference.md) — `Should().Be(...)`, `UnwrapError()`
  - [trellis-api-analyzers.md](trellis-api-analyzers.md) — `TRLS001`–`TRLS019`, `TrellisDiagnosticIds`

## How to read these recipes

Every recipe follows the same shape:

1. **Problem statement** — what the consumer is trying to accomplish.
2. **Solution code** — copy-pasteable C# that compiles against the documented public surface only. No invented APIs.
3. **What it shows** — the cross-cutting concept being demonstrated.
4. **Anti-pattern → fix** *(when applicable)* — the wrong way and which Trellis analyzer catches it.

Conventions used throughout:

- All Trellis types live in the `Trellis` namespace except where called out (`Trellis.Asp`, `Trellis.Asp.Authorization`, `Trellis.EntityFrameworkCore`, `Trellis.Analyzers`).
- Snippets use C# 12+ features (file-scoped namespaces, primary constructors, collection expressions) — Trellis targets `net10.0`.
- `Result.Ok` / `Result.Fail` are *the* construction APIs. `default(Result<T>)` is a typed failure; do not rely on it as success.
- Every async pipeline uses `*Async` extensions; mixing sync chain methods with `Task<Result<T>>` triggers `TRLS009`.
- Examples reference an `OrderId : RequiredGuid<OrderId>` value object and an `Order` aggregate. Substitute your own types without changing the structure.

---

## Recipe 1 — CRUD aggregate (DDD value objects + entity + repository contract)

**Problem.** Model an `Order` aggregate with a typed identifier, a value-object money type, and a repository contract that returns `Result<T>` for not-found.

```csharp
using Trellis;

// Strongly-typed ID: source-generated factory, equality, parsing, JSON converter.
public sealed partial class OrderId : RequiredGuid<OrderId>;

// Value object backed by a 3-letter ISO 4217 currency code.
[StringLength(3, MinimumLength = 3)]
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// Composite value object — must be a class (records can't inherit ValueObject).
public sealed class Money : ValueObject
{
    public Money(decimal amount, CurrencyCode currency) { Amount = amount; Currency = currency; }
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }
    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency.Value;
    }
}

// Aggregate root.
public sealed class Order : Aggregate<OrderId>
{
    public Money Total { get; private set; } = default!;
    public OrderStatus Status { get; private set; }

    private Order(OrderId id) : base(id) { }   // EF Core ctor

    public static Result<Order> Create(OrderId id, Money total) =>
        Result.Ok(new Order(id) { Total = total, Status = OrderStatus.Draft });
}

// Trellis convention: model finite domain states as RequiredEnum<TSelf>
// (NOT C# enums). The partial keyword triggers the source generator.
public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft     = new();
    public static readonly OrderStatus Submitted = new();
    public static readonly OrderStatus Cancelled = new();
}

// Repository contract — uses Maybe<T> for "may legitimately find nothing"
// (per ADR-002); reserve Result<T> for failures the caller can act on.
public interface IOrderRepository
{
    Task<Maybe<Order>> FindAsync(OrderId id, CancellationToken ct);
    void Add(Order order);
}
```

**What it shows.** `RequiredGuid<TSelf>` and `RequiredString<TSelf>` deliver a complete strongly-typed primitive (parsing, equality, JSON, EF) once you mark the partial class. `[StringLength]` and `[Range]` come from the **`Trellis` namespace** and are placed on the **class declaration** — using `System.ComponentModel.DataAnnotations` versions silently compiles but is ignored by the Trellis source generator (`TRLS017`).

**Anti-pattern → fix (TRLS017).**

```csharp
// WRONG — using System.ComponentModel.DataAnnotations.StringLength
using System.ComponentModel.DataAnnotations;     // ← wrong namespace
[StringLength(3, MinimumLength = 3)]             // TRLS017
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// FIX
using Trellis;                                   // ← Trellis attributes
[StringLength(3, MinimumLength = 3)]             // generator now picks it up
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;
```

---

## Recipe 2 — Command + handler + FluentValidation + EF persistence

**Problem.** Wire a `PlaceOrderCommand` end-to-end: validation via FluentValidation, mediator handler that uses an EF repository, transactional commit on success.

```csharp
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.FluentValidation;
using Trellis.Mediator;

public sealed record PlaceOrderCommand(Guid OrderId, decimal Amount, string Currency)
    : ICommand<Result<OrderId>>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3);
    }
}

public sealed class PlaceOrderHandler(IOrderRepository repo)
    : ICommandHandler<PlaceOrderCommand, Result<OrderId>>
{
    public ValueTask<Result<OrderId>> Handle(PlaceOrderCommand cmd, CancellationToken cancellationToken) =>
        new(OrderId.TryCreate(cmd.OrderId)
            .BindZip(_ => CurrencyCode.TryCreate(cmd.Currency).Map(c => new Money(cmd.Amount, c)))
            .Bind(t => Order.Create(t.Item1, t.Item2))
            .Tap(repo.Add)
            .Map(o => o.Id));
}

// Composition root
public static class OrdersDi
{
    public static IServiceCollection AddOrdersFeature(this IServiceCollection services) =>
        services
            .AddTrellisBehaviors()                              // Validation + logging + tracing
            .AddTrellisFluentValidation(typeof(PlaceOrderValidator).Assembly)
            .AddTrellisUnitOfWork<AppDbContext>()               // Innermost: commits on success
            .AddScoped<IOrderRepository, EfOrderRepository>();
}
```

**What it shows.** The mediator pipeline already runs `ValidationBehavior<TMessage, TResponse>` before the handler — `AddTrellisFluentValidation` plugs every `IValidator<T>` into it via the open-generic `IMessageValidator<T>` adapter. `AddTrellisUnitOfWork<TContext>` registers `TransactionalCommandBehavior<,>` *after* the others, so it lands innermost and commits only when the handler returns success. The handler itself is pure: no `try`/`catch`, no `await db.SaveChangesAsync()` — that's the unit of work's job.

**Anti-pattern → fix (TRLS010).**

```csharp
// WRONG — sync-over-async (.Result deadlocks) + throwing inside the Result chain.
.Bind(id => repo.FindAsync(id, ct).Result is { HasValue: true }
    ? throw new InvalidOperationException("already exists")  // TRLS010 + TRLS005
    : Result.Ok(id))

// FIX — MatchAsync awaits the Maybe carrier and dispatches without leaving the Result chain.
.BindAsync(id => repo.FindAsync(id, ct)
    .MatchAsync(
        some: _  => Result.Fail<OrderId>(new Error.Conflict(new ResourceRef("Order", id.Value.ToString()), "already_exists")),
        none: () => Result.Ok(id)))
```

---

## Recipe 3 — Query handler returning `Page<T>` (paginated list with cursor)

**Problem.** Expose a list endpoint that paginates `Order` rows by cursor, exposes the requested vs. applied limit, and projects a DTO.

```csharp
using Trellis;

public sealed record ListOrdersQuery(string? Cursor, int Limit) : IQuery<Result<Page<OrderListItem>>>;

public sealed record OrderListItem(Guid Id, decimal Amount, string Currency);

public sealed class ListOrdersHandler(AppDbContext db)
    : IQueryHandler<ListOrdersQuery, Result<Page<OrderListItem>>>
{
    private const int MaxLimit = 100;

    public async ValueTask<Result<Page<OrderListItem>>> Handle(ListOrdersQuery q, CancellationToken ct)
    {
        var requested = q.Limit;
        var applied   = Math.Clamp(requested, 1, MaxLimit);

        var query = db.Orders.AsNoTracking().OrderBy(o => o.Id);
        if (q.Cursor is not null)
            query = query.Where(o => o.Id.Value > Guid.Parse(q.Cursor));

        var rows = await query.Take(applied + 1).ToListAsync(ct);
        var hasNext = rows.Count > applied;
        var items   = rows.Take(applied)
                          .Select(o => new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value))
                          .ToList();

        return Result.Ok(new Page<OrderListItem>(
            Items: items,
            Next: hasNext ? new Cursor(items[^1].Id.ToString("N")) : null,
            Previous: q.Cursor is null ? null : new Cursor(q.Cursor),
            RequestedLimit: requested,
            AppliedLimit: applied));
    }
}
```

**What it shows.** `Page<T>` is a `readonly record struct`; instances always carry positive limits and a non-null `Items`. `WasCapped` becomes `true` automatically when the server clamped the limit. Use `Page.Empty<T>(req, app)` for the empty case rather than `default(Page<T>)`.

---

## Recipe 4 — Minimal-API endpoint wiring `Result<T>` → `HttpResponseOptionsBuilder` → `ToHttpResponse`

**Problem.** Map a `Result<Order>` to a fully-conformant HTTP response: `200` with strong ETag and `Last-Modified`, `404`/`422` Problem Details on failure, `304` on `If-None-Match` match.

```csharp
using Microsoft.AspNetCore.Builder;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellisAsp();          // error → status mapping + scalar-value validation
builder.Services.AddOrdersFeature();       // from Recipe 2

var app = builder.Build();

app.MapGet("/orders/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    Result<Order> result = await mediator.Send(new GetOrderQuery(id), ct);

    return result.ToHttpResponse(opts => opts
        .WithETag(o => o.ETag)                         // strong ETag from aggregate
        .WithLastModified(o => o.LastModified)         // RFC 1123
        .Vary("Accept", "Accept-Language")
        .EvaluatePreconditions());                     // 304 / 412 handling
});

app.Run();
```

**What it shows.** `ToHttpResponse` returns `Microsoft.AspNetCore.Http.IResult` and is the **only** supported response verb. The fluent `HttpResponseOptionsBuilder<TDomain>` configures protocol semantics (`WithETag`, `WithLastModified`, `Vary`, `EvaluatePreconditions`) without leaking HTTP into the handler. Failures (`Error.NotFound`, `Error.UnprocessableContent`, …) round-trip through Problem Details using the `TrellisAspOptions` mapping registered by `AddTrellisAsp`.

---

## Recipe 5 — MVC controller using `AsActionResult`

**Problem.** Same payload as Recipe 4 but with a typed MVC `ActionResult<OrderDto>`.

```csharp
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

[ApiController]
[Route("orders")]
public sealed class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
    {
        Result<Order> result = await mediator.Send(new GetOrderQuery(id), ct);

        return result
            .ToHttpResponse(
                body: o => new OrderDto(o.Id.Value, o.Total.Amount, o.Total.Currency.Value),
                configure: opts => opts.WithETag(o => o.ETag).EvaluatePreconditions())
            .AsActionResult<OrderDto>();
    }
}

public sealed record OrderDto(Guid Id, decimal Amount, string Currency);
```

**What it shows.** `.AsActionResult<TBody>()` projects an `IResult` into a typed `ActionResult<TBody>`, so MVC clients still get OpenAPI/Swagger-friendly typed responses while the response itself executes through the same `IResult` pipeline as Minimal API.

---

## Recipe 6 — Conditional GET with `EntityTagValue` and byte-range with `RangeOutcome`

**Problem.** Serve a binary blob with strong-ETag conditional GET *and* RFC 9110 byte-range support.

```csharp
using Microsoft.AspNetCore.Http;
using Trellis;
using Trellis.Asp;

app.MapGet("/blobs/{id:guid}", async (Guid id, HttpRequest req, IBlobRepository repo, CancellationToken ct) =>
{
    Result<BlobContent> result = await repo.FindAsync(new BlobId(id), ct);

    return result.ToHttpResponse(opts => opts
        .WithETag(b => EntityTagValue.Strong(b.Sha256Hex))
        .WithLastModified(b => b.UploadedAt)
        .Vary("Range")
        .WithAcceptRanges("bytes")
        .WithRange(b =>
        {
            var outcome = RangeRequestEvaluator.Evaluate(req, b.Length);
            return outcome switch
            {
                RangeOutcome.PartialContent pc => new System.Net.Http.Headers.ContentRangeHeaderValue(pc.From, pc.To, pc.CompleteLength),
                _                              => new System.Net.Http.Headers.ContentRangeHeaderValue(b.Length),
            };
        })
        .EvaluatePreconditions());
});
```

**What it shows.** `EntityTagValue.Strong(...)` and `EntityTagValue.Weak(...)` build typed ETags; `WithETag` accepts either a `string` (always strong) or an `EntityTagValue`. `RangeRequestEvaluator.Evaluate(...)` (in `Trellis.Asp`) returns the closed-ADT `RangeOutcome`: `FullRepresentation`, `PartialContent(From, To, CompleteLength)`, or `NotSatisfiable(CompleteLength)`. `.EvaluatePreconditions()` honors `If-Match`/`If-None-Match`/`If-Modified-Since`/`If-Unmodified-Since` against the configured ETag and `Last-Modified` selectors.

---

## Recipe 7 — Authorization: `IActorProvider` + `IAuthorize` + resource-based auth

**Problem.** Static (permission) authorization on a delete command, plus resource-based ownership check on an update command — all via the mediator pipeline.

```csharp
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

public sealed record DeleteOrderCommand(OrderId OrderId) : ICommand<Result>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:delete"];
}

public sealed record UpdateOrderCommand(OrderId OrderId, decimal NewAmount)
    : ICommand<Result>, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    // Typed VO carried straight through — no parse, no throw.
    // ASP.NET model binding (via IScalarValue<OrderId, string>) handles the
    // string→OrderId conversion at the API edge.
    public OrderId GetResourceId() => OrderId;

    public Trellis.IResult Authorize(Actor actor, Order resource) =>
        resource.OwnerId == actor.Id || actor.Permissions.Contains("orders:write")
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden(PolicyId: "orders.owner", Resource: new ResourceRef("Order", OrderId.Value.ToString())));
}

// DI wiring
services.AddTrellisBehaviors();
services.AddClaimsActorProvider();               // ClaimsActorProvider for ASP.NET Core
services.AddResourceAuthorization(typeof(UpdateOrderCommand).Assembly);
```

**What it shows.** `IAuthorize` enforces an AND-permission gate via `AuthorizationBehavior<,>`. `IAuthorizeResource<TResource>` runs *after* `IResourceLoader<TMessage, TResource>` produces the loaded resource, then calls `Authorize(actor, resource)`. Combining `IAuthorizeResource<TResource>` with `IIdentifyResource<TResource, TId>` lets the framework reuse the shared `SharedResourceLoaderById<TResource, TId>` instead of requiring a per-command loader.

---

## Recipe 8 — EF Core: `MaybePropertyMapping` for nullable value objects

**Problem.** Persist a `Maybe<EmailAddress>` property with the EF Core `MaybeConvention`, then verify the generated mapping in a startup diagnostics check.

```csharp
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed partial class EmailAddress : RequiredString<EmailAddress>;

public sealed partial class Customer : Aggregate<CustomerId>
{
    public Customer(CustomerId id) : base(id) { }

    public partial Maybe<EmailAddress> Email { get; set; }   // TRLS035 if not 'partial'
}

// Configure
public sealed class AppDbContext : DbContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(AppDbContext).Assembly);
}

// Diagnostics — print the generated storage members for every Maybe<T> in the model
public static class ModelDiagnostics
{
    public static void DumpMaybeMappings(DbContext db)
    {
        IReadOnlyList<MaybePropertyMapping> mappings = db.GetMaybePropertyMappings();
        foreach (var m in mappings)
            Console.WriteLine($"{m.EntityTypeName}.{m.PropertyName} → {m.MappedBackingFieldName} ({m.StoreType.Name})");
    }
}
```

**What it shows.** `Maybe<T>` properties are routed through `MaybeConvention`, which generates a backing field (`_email` for `Email`) that EF Core maps to a nullable column. The CLR property remains `Maybe<EmailAddress>` everywhere in the domain. `MaybePropertyMapping` is the diagnostic record that exposes both names — useful for `HasIndex` on the storage member.

> For **composite** value objects (multi-field `[OwnedEntity]` types like `ShippingAddress`) — and for `Maybe<T>` where `T` is composite — see [Recipe 13](#recipe-13--ef-core-composite-owned-value-object-ownedentity--ownsone-not-needed). `Recipe 8` covers scalar `Maybe<T>` only.

**Anti-pattern → fix (TRLS016).**

```csharp
// WRONG — HasIndex against the CLR Maybe<T> property silently fails
modelBuilder.Entity<Customer>().HasIndex(c => c.Email);   // TRLS016

// FIX 1 — strongly-typed Trellis index helper
modelBuilder.Entity<Customer>().HasTrellisIndex(c => new { c.Status, c.Email });

// FIX 2 — string-based HasIndex against the storage member
modelBuilder.Entity<Customer>().HasIndex("Status", "_email");
```

---

## Recipe 9 — State machine: `CanFire` + `Fire` pattern with `FireResult`

**Problem.** Drive an order through `Draft → Submitted → Shipped` using Stateless, but expose every transition as `Result<TState>` so the mediator pipeline composes naturally.

```csharp
using Stateless;
using Trellis;

// States and triggers as RequiredEnum value objects (Trellis convention) —
// equality is symbolic, so Stateless's TState/TTrigger generic constraints are satisfied.
public partial class DocumentState : RequiredEnum<DocumentState>
{
    public static readonly DocumentState Draft     = new();
    public static readonly DocumentState Submitted = new();
    public static readonly DocumentState Approved  = new();
}

public partial class DocumentTrigger : RequiredEnum<DocumentTrigger>
{
    public static readonly DocumentTrigger Submit  = new();
    public static readonly DocumentTrigger Approve = new();
    public static readonly DocumentTrigger Reject  = new();
}

public sealed class DocumentService
{
    public Result<DocumentState> Submit(Document doc)
    {
        var machine = new StateMachine<DocumentState, DocumentTrigger>(doc.State);
        machine.Configure(DocumentState.Draft).Permit(DocumentTrigger.Submit, DocumentState.Submitted);
        machine.Configure(DocumentState.Submitted)
               .Permit(DocumentTrigger.Approve, DocumentState.Approved)
               .Permit(DocumentTrigger.Reject,  DocumentState.Draft);

        // FireResult pre-checks CanFire and converts invalid transitions to
        // Error.Conflict("state.machine.invalid.transition").
        Result<DocumentState> result = machine.FireResult(DocumentTrigger.Submit);
        return result.Tap(newState => doc.State = newState);
    }
}
```

**What it shows.** `StateMachineExtensions.FireResult(...)` honors `PermitIf`/`IgnoreIf` guards via `CanFire(...)` rather than parsing exception messages, so it survives Stateless library upgrades. For aggregates whose state lives in a backing field (e.g., loaded from EF), use `LazyStateMachine<TState, TTrigger>` to defer machine creation until the first `FireResult` call.

---

## Recipe 10 — Test: handler test using `Trellis.Testing` `Should().Be(...)` / `UnwrapError()`

**Problem.** Unit-test the `PlaceOrderHandler` from Recipe 2 using FluentAssertions extensions from `Trellis.Testing`.

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Testing;
using Xunit;

public class PlaceOrderHandlerTests
{
    [Fact]
    public async Task PlaceOrder_returns_id_on_success()
    {
        var repo = new InMemoryOrderRepository();
        var sut  = new PlaceOrderHandler(repo);

        var result = await sut.Handle(
            new PlaceOrderCommand(Guid.NewGuid(), 100m, "USD"),
            CancellationToken.None);

        result.Should().BeSuccess();
        result.Should().HaveValue(repo.Last().Id);                  // structural equality on Result<T>
    }

    [Fact]
    public async Task PlaceOrder_fails_with_validation_when_currency_invalid()
    {
        var sut = new PlaceOrderHandler(new InMemoryOrderRepository());

        var result = await sut.Handle(
            new PlaceOrderCommand(Guid.NewGuid(), 100m, "US"),       // 2 chars, not 3
            CancellationToken.None);

        result.Should().BeFailureOfType<Error.UnprocessableContent>()
            .Which.Should().HaveFieldError("currency");
    }
}
```

**What it shows.** `ResultAssertions<TValue>.HaveValue(...)` does structural comparison; `UnwrapError()` is the safe accessor that *only* returns the error and is intended for use after `Should().BeFailure...`. Calling `.Should()` on an `Error.UnprocessableContent` returns the specialized `ValidationErrorAssertions` (with `HaveFieldError`, `HaveFieldErrorWithDetail`, `HaveFieldCount`). Async pipelines should be awaited *first* and asserted after — `await result.Should().BeSuccessAsync()` is wrong because `BeSuccess()` is sync; the awaited `Result<T>` is what you assert on.

---

## Recipe 11 — Anti-pattern → fix gallery (the analyzers in action)

A condensed atlas showing each common analyzer trigger and its idiomatic Trellis fix.

### TRLS001 — Result return value not handled

```csharp
// WRONG — Result<T> dropped on the floor
PlaceOrder(cmd);                                   // TRLS001

// FIX — handle the value or assign it
var _ = PlaceOrder(cmd).Match(_ => 0, e => throw new("..."));
```

### TRLS003 — Unsafe `Maybe.Value`

```csharp
// WRONG
string city = customer.Email.Value;                // TRLS003

// FIX 1 — guard
if (customer.Email.HasValue) { var v = customer.Email.Value; }

// FIX 2 — convert to Result
Result<EmailAddress> r = customer.Email.ToResult(new Error.NotFound(new ResourceRef("Email", customer.Id.ToString())));
```

### TRLS010 — Throwing in a Result chain

```csharp
// WRONG
.Bind(o => throw new InvalidOperationException("bad"))   // TRLS010

// FIX
.Bind(o => Result.Fail<Order>(new Error.Conflict(new ResourceRef("Order", o.Id.ToString()), "invalid_state")))
```

### TRLS016 — `HasIndex` on a `Maybe<T>` property

```csharp
// WRONG
b.HasIndex(c => c.Email);                          // TRLS016 — silently no-op

// FIX
b.HasTrellisIndex(c => new { c.Email });
```

### TRLS017 — Wrong attribute namespace on a value object

```csharp
// WRONG — System.ComponentModel.DataAnnotations
[System.ComponentModel.DataAnnotations.StringLength(10)]    // TRLS017 — generator ignores it
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// FIX
[Trellis.StringLength(10)]
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;
```

### TRLS018 — Unsafe `Result<T>` deconstruction

```csharp
// WRONG
var (ok, value, err) = result;
SendEmail(value);                                  // TRLS018 — value is default on failure

// FIX
var (ok, value, err) = result;
if (!ok) return err.ToHttpResponse();
SendEmail(value);                                  // gated by !ok early-return
```

### TRLS019 — `default(Result)` / `default(Maybe<T>)`

```csharp
// WRONG
return default;                                    // TRLS019 — typed FAILURE, not success
return default(Maybe<Email>);                      // TRLS019 — equivalent to .None but obscure

// FIX
return Result.Ok();
return Maybe<Email>.None;
```

---

## Recipe 12 — DI wiring playbook: `AddTrellis*` extension methods across all packages

**Problem.** Compose every `AddTrellis*` registration in the correct order so behaviors stack properly.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Authorization;
using Trellis.Asp.Routing;
using Trellis.EntityFrameworkCore;

public static class CompositionRoot
{
    public static IServiceCollection AddApp(this IServiceCollection services, string connectionString)
    {
        // 1. Mediator pipeline (outermost behaviors first).
        services.AddTrellisBehaviors();

        // 2. FluentValidation plug-in. Idempotent; safe to call after AddTrellisBehaviors.
        services.AddTrellisFluentValidation(typeof(PlaceOrderValidator).Assembly);

        // 3. ASP layer: Problem Details mapping + scalar-value validation pipeline.
        services.AddTrellisAsp();

        // 4. ASP authorization actor providers.
        services.AddClaimsActorProvider();
        services.AddResourceAuthorization(typeof(UpdateOrderCommand).Assembly);

        // 5. EF Core context with Trellis interceptors + conventions.
        services.AddDbContext<AppDbContext>(opts => opts
            .UseSqlServer(connectionString)
            .AddTrellisInterceptors());

        // 6. EF unit of work LAST so TransactionalCommandBehavior lands innermost.
        services.AddTrellisUnitOfWork<AppDbContext>();

        // 7. Optional: route constraints for value-object IDs (reflection-based).
        services.AddTrellisRouteConstraints(typeof(OrderId).Assembly);

        // 8. Application services.
        services.AddScoped<IOrderRepository, EfOrderRepository>();

        return services;
    }
}
```

**Composition order, summarized.**

| Step | Call | Why this position |
| ---- | ---- | ----------------- |
| 1 | `AddTrellisBehaviors()` | Registers tracing → telemetry → validation → exception → logging behaviors. Must come before any extension that piggybacks on the open-generic behavior list. |
| 2 | `AddTrellisFluentValidation(...)` | Plugs `IValidator<T>` into the existing `ValidationBehavior<,>`. Idempotent; safe in any order after step 1. |
| 3 | `AddTrellisAsp()` | Registers `TrellisAspOptions` (error → status mapping) and chains `AddScalarValueValidation()` for JSON pipeline integration. Add early so MVC/Minimal API JSON conventions are wired before endpoint registration. |
| 4 | `AddClaimsActorProvider()` + `AddResourceAuthorization(...)` | `IActorProvider` + permission/resource-based behavior (not in `AddTrellisBehaviors()`). |
| 5 | `AddDbContext(... .AddTrellisInterceptors())` | Wires `MaybeQueryInterceptor`, `ScalarValueQueryInterceptor`, ETag and timestamp interceptors. |
| 6 | `AddTrellisUnitOfWork<TContext>()` | **Must be last** behavior registration so `TransactionalCommandBehavior<,>` lands innermost (closest to the handler) and commit failures stay visible to outer logging/tracing behaviors. |
| 7 | `AddTrellisRouteConstraints(...)` / `AddTrellisRouteConstraint<T>(...)` | Optional; the reflection-based overload is **not** AOT-safe — the typed overload is. |

---

## Recipe 13 — EF Core: composite owned value object (`[OwnedEntity]` + `OwnsOne` not needed)

**Problem.** Persist a multi-field value object (`ShippingAddress` with street/city/state/postalCode/country) as part of a `Customer` aggregate. Every field is required, the VO must validate at construction, and the JSON wire format must reuse the same validation as the domain TryCreate.

The unobvious bits this recipe pins down:

- `ApplyTrellisConventions` already configures `[OwnedEntity]` types as owned navigations — **you do not need `builder.OwnsOne(...)` in your `IEntityTypeConfiguration`** (the `CompositeValueObjectConvention` discovers them by attribute when the assembly is passed to `ApplyTrellisConventions`).
- The class **must** be `partial` (`TRLS036`), inherit `ValueObject` (`TRLS038`), and have **no** parameterless constructor (`TRLS037`) — the source generator emits one for EF Core's materialization path.
- `[JsonConverter(typeof(CompositeValueObjectJsonConverter<TSelf>))]` routes JSON deserialization through the public `TryCreate`, so the API surface and the domain agree on what's valid. Without it, model binding produces a default-constructed VO that bypasses `TryCreate`.

```csharp
using System.Text.Json.Serialization;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

[OwnedEntity]                                                        // TRLS036 if not partial; TRLS037 if you add a parameterless ctor; TRLS038 if not ValueObject
[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]
public partial class ShippingAddress : ValueObject
{
    public string Street     { get; private set; } = null!;
    public string City       { get; private set; } = null!;
    public string State      { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string Country    { get; private set; } = null!;

    private ShippingAddress(string street, string city, string state, string postalCode, string country)
    {
        Street = street; City = city; State = state; PostalCode = postalCode; Country = country;
    }

    public static Result<ShippingAddress> TryCreate(
        string street, string city, string state, string postalCode, string country, string? fieldName = null)
    {
        var violations = new List<FieldViolation>(5);
        AddIfBlank(violations, street,     fieldName, nameof(Street));
        AddIfBlank(violations, city,       fieldName, nameof(City));
        AddIfBlank(violations, state,      fieldName, nameof(State));
        AddIfBlank(violations, postalCode, fieldName, nameof(PostalCode));
        AddIfBlank(violations, country,    fieldName, nameof(Country));
        return violations.Count > 0
            ? Result.Fail<ShippingAddress>(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())))
            : Result.Ok(new ShippingAddress(street.Trim(), city.Trim(), state.Trim(), postalCode.Trim(), country.Trim()));
    }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street; yield return City; yield return State; yield return PostalCode; yield return Country;
    }

    private static void AddIfBlank(List<FieldViolation> v, string value, string? owner, string part)
    {
        if (!string.IsNullOrWhiteSpace(value)) return;
        var leaf = char.ToLowerInvariant(part[0]) + part[1..];
        var pointer = string.IsNullOrWhiteSpace(owner)
            ? InputPointer.ForProperty(leaf)
            : new InputPointer($"/{owner}/{leaf}");
        v.Add(new FieldViolation(pointer, "required") { Detail = $"{part} is required." });
    }
}

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed partial class Customer : Aggregate<CustomerId>
{
    public string Name { get; private set; } = null!;
    public ShippingAddress ShippingAddress { get; private set; } = null!;     // required composite owned VO
    public partial Maybe<ShippingAddress> BillingAddress { get; set; }        // optional composite owned VO

    private Customer(CustomerId id, string name, ShippingAddress shipping) : base(id)
    {
        Name = name; ShippingAddress = shipping;
    }

    public static Result<Customer> Create(CustomerId id, string name, ShippingAddress shipping) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Fail<Customer>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("name"), "required") { Detail = "Name is required." })))
            : Result.Ok(new Customer(id, name, shipping));
}

// CONFIGURATION — note the absence of OwnsOne(c => c.ShippingAddress).
// CompositeValueObjectConvention picks up [OwnedEntity] types automatically
// from the assemblies passed to ApplyTrellisConventions.
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired();
        // No builder.OwnsOne(c => c.ShippingAddress) — the convention does this for you.
        // No HasConversion(...) on the inner string fields — they are mapped by EF Core directly.
    }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventions(typeof(Customer).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

**What it shows.**

- `[OwnedEntity]` + `partial` + `ValueObject` + private ctor is the contract. The three diagnostics (`TRLS036`/`037`/`038`) catch each violation at compile time.
- `CompositeValueObjectJsonConverter<T>` makes JSON deserialization round-trip through `TryCreate`, so an API request body with a missing `state` produces the same `Error.UnprocessableContent` shape the domain emits.
- `ApplyTrellisConventions` removes the boilerplate `OwnsOne` call. You only need `OwnsOne` when you want to **override** the convention (custom column names, table splitting, indexes on inner properties).

**Storage shape.**

| Aggregate property | Storage |
|---|---|
| Required `ShippingAddress` (non-nullable) | Table-split: 5 columns on the `Customers` table — `ShippingAddress_Street`, `ShippingAddress_City`, `ShippingAddress_State`, `ShippingAddress_PostalCode`, `ShippingAddress_Country` (all `NOT NULL`). |
| Optional `Maybe<ShippingAddress>` | Because the inner properties are non-nullable, `CompositeValueObjectConvention` switches to a **separate table** named `{Owner}_{Property}` (e.g., `Customer_BillingAddress`) with a `1:0..1` FK back to `Customers`. See the storage rules in [trellis-api-efcore.md](trellis-api-efcore.md) for the full decision matrix. |

**Anti-pattern → fix.**

```csharp
// WRONG — manual OwnsOne after ApplyTrellisConventions duplicates the convention's work
// and silently overrides any annotations the convention set.
builder.OwnsOne(c => c.ShippingAddress, owned => { /* … */ });

// FIX — let the convention own the registration. Use OwnsOne only to override
// (e.g., to rename columns or add an index on an inner property):
builder.OwnsOne(c => c.ShippingAddress, owned =>
{
    owned.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
    owned.HasIndex(a => a.Country);
});
```

```csharp
// WRONG — non-partial class (TRLS036) so the generator can't emit the parameterless ctor.
[OwnedEntity]
public class ShippingAddress : ValueObject { /* … */ }

// WRONG — declared parameterless ctor (TRLS037) shadows the generator's emitted one.
[OwnedEntity]
public partial class ShippingAddress : ValueObject { public ShippingAddress() { } }

// WRONG — not a ValueObject (TRLS038), so equality and convention-based mapping break.
[OwnedEntity]
public partial class ShippingAddress { /* … */ }
```

---

## Recipe 14 — Optional fields in request DTOs: `Maybe<TScalar>` vs nullable transport

**Problem.** A request body has an optional field — say `phoneNumber` on `CreateCustomerRequest`. The domain models it as `Maybe<PhoneNumber>` (the canonical Trellis pattern). What does the DTO declare it as?

The answer depends on whether the inner type is a **scalar** (single-primitive) value object or a **composite** owned value object. Trellis ships a JSON converter + model binder for the scalar case but not the composite case.

| Inner type | Pattern | Why |
|---|---|---|
| `Maybe<TScalar>` where `TScalar : IScalarValue<TScalar, TPrimitive>` (e.g., `Maybe<EmailAddress>`, `Maybe<PhoneNumber>`) | **Use `Maybe<T>` directly on the DTO.** | `AddTrellisAsp()` registers `MaybeScalarValueJsonConverterFactory` (JSON), `MaybeModelBinder<T,P>` (route/query/header), and `MaybeSuppressChildValidationMetadataProvider` (stops `ValidationVisitor` from touching `.Value` when `None`). `null`/missing → `None`; valid → `Maybe.From(validated)`; invalid → ProblemDetails with the same field path the domain emits. |
| `Maybe<TComposite>` where `TComposite : ValueObject` with multiple fields (e.g., `Maybe<ShippingAddress>`) | **Use a nullable transport (`TComposite?`) and adapt at the controller seam.** | No `MaybeCompositeValueObjectJsonConverterFactory` ships today — System.Text.Json would default-construct the inner type, bypassing `TryCreate`. Wrap with `Maybe.From(...)` inside the controller. |

### Pattern A — scalar `Maybe<T>` directly on the DTO

```csharp
using Trellis;
using Trellis.Primitives;

public sealed partial class EmailAddress : RequiredString<EmailAddress>;
public sealed partial class PhoneNumber  : RequiredString<PhoneNumber>;

public sealed record CreateCustomerRequest(
    EmailAddress         Email,           // required
    Maybe<PhoneNumber>   PhoneNumber);    // optional — null/missing JSON → Maybe.None

[ApiController]
[Route("customers")]
public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct) =>
        sender.Send(new CreateCustomerCommand(request.Email, request.PhoneNumber), ct)
              .ToHttpResponseAsync(CustomerResponse.From, /* … */)
              .AsActionResultAsync<CustomerResponse>();
}
```

`AddTrellisAsp()` is the only wiring required:

```csharp
services.AddTrellisAsp();      // MaybeScalarValueJsonConverterFactory + MaybeModelBinder + ValidationVisitor patch
services.AddControllers();
```

Send `{"email":"a@b.com","phoneNumber":null}` (or omit `phoneNumber` entirely) → handler receives `Maybe<PhoneNumber>.None`. Send `{"email":"a@b.com","phoneNumber":"not a phone"}` → 422 with field path `/phoneNumber` and the validation message produced by `PhoneNumber.Create`.

### Pattern B — composite owned VO, nullable transport + controller-seam adapter

```csharp
public sealed record CreateCustomerRequest(
    EmailAddress       Email,
    ShippingAddress?   ShippingAddress);   // nullable transport — NOT Maybe<ShippingAddress>

public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var shipping = request.ShippingAddress is null
            ? Maybe<ShippingAddress>.None
            : Maybe.From(request.ShippingAddress);

        return sender.Send(new CreateCustomerCommand(request.Email, shipping), ct)
                     .ToHttpResponseAsync(CustomerResponse.From, /* … */)
                     .AsActionResultAsync<CustomerResponse>();
    }
}
```

The composite VO must still carry `[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]` (see Recipe 13) so its inner fields round-trip through `TryCreate`. The seam adapter only handles the optionality.

**Why not just declare `Maybe<ShippingAddress>` on the DTO?** `MaybeScalarValueJsonConverterFactory.CanConvert` checks for `IScalarValue<,>` on the inner type. Composite VOs do not implement `IScalarValue`, so the factory returns false, and `Maybe<ShippingAddress>` falls back to default System.Text.Json serialization — which produces a default-constructed `ShippingAddress` (`{}`) wrapped in `Maybe.From`, silently bypassing `TryCreate`. That's a correctness bug, not just an ergonomics one.

### Anti-pattern → fix

```csharp
// WRONG — composite Maybe<T> on DTO. Compiles, deserializes to Maybe.From(default(ShippingAddress)),
// silently skips TryCreate. Discovered only when the persisted entity has empty strings.
public sealed record CreateCustomerRequest(EmailAddress Email, Maybe<ShippingAddress> ShippingAddress);

// FIX — nullable transport + controller-seam adapter (Pattern B above).
public sealed record CreateCustomerRequest(EmailAddress Email, ShippingAddress? ShippingAddress);

// WRONG — bypassing AddTrellisAsp() (e.g., raw services.AddControllers().AddJsonOptions(...) in isolation)
// drops the Maybe converters AND the SuppressChildValidationMetadataProvider, so MVC's ValidationVisitor
// will throw InvalidOperationException("Maybe has no value.") the moment a None reaches model validation.
services.AddControllers();   // missing AddTrellisAsp()

// FIX — call AddTrellisAsp() before AddControllers(); it is idempotent and configures both pipelines.
services.AddTrellisAsp();
services.AddControllers();
```

> A `MaybeCompositeValueObjectJsonConverterFactory` to make Pattern B unnecessary is tracked in `BACKLOG.md` under *Open — Framework Features*.

---

## Cross-cutting tips

- **Run analyzers in CI.** `Trellis.Analyzers` ships in the framework and runs as part of every `dotnet build`. Treat warnings as errors for `TRLS00x` once your codebase is clean.
- **Do not mix sync chain methods with async lambdas.** `result.Map(async v => …)` triggers `TRLS009`; use `MapAsync`. The fix provider can apply this rewrite automatically.
- **Construct errors via the closed ADT.** `new Error.NotFound(new ResourceRef("Order", id))` — never `new Error("not_found", "...")` (which won't compile against the abstract base; analyzer `TRLS006` catches inferred attempts).
- **Use `Result.Combine` (or `EnsureAll`) for accumulating validation.** Manual `IsSuccess` checks across multiple results trigger `TRLS008`.
- **`InputPointer.Root` for whole-body violations.** Use `InputPointer.ForProperty(name)` for field-level violations and `InputPointer.Root` when the rule is object-level.

---

## Cross-references

- [trellis-api-core.md](trellis-api-core.md#extension-class-catalog-full-signatures) — every `Result*Extensions(Async)` family with full signatures.
- [trellis-api-core.md](trellis-api-core.md#pagination) — `Cursor`, `Page<T>`, `Page.Empty<T>`.
- [trellis-api-asp.md](trellis-api-asp.md) — `HttpResponseOptionsBuilder<TDomain>` member-by-member.
- [trellis-api-mediator.md](trellis-api-mediator.md#pipeline-order) — exact behavior ordering.
- [trellis-api-analyzers.md](trellis-api-analyzers.md#constants--trellisdiagnosticids) — every `TrellisDiagnosticIds` constant + emitting analyzer.
