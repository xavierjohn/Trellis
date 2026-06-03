---
package: Trellis.Analyzers (applied form)
namespaces: [Trellis, Trellis.Analyzers]
types: [TRLS001, TRLS003, TRLS010, TRLS013, TRLS015, TRLS016, TRLS017, TRLS018, TRLS019, TRLS020, TRLS035, TRLS036, TRLS037, TRLS038, TRLS039, TRLS054, TRLS055, TRLS056]
related_docs: [trellis-api-analyzers.md, trellis-api-cookbook.md]
version: v4
last_verified: 2026-06-03
audience: [llm]
---
# Trellis Anti-Pattern → Fix Gallery

> A condensed atlas mapping each common Trellis analyzer trigger to its idiomatic fix. **Read this file alongside `trellis-api-cookbook.md` whenever you are touching a Trellis pipeline.** Each section's WRONG/FIX pair captures the canonical control-flow shape the analyzer expects — preserve that shape and adapt identifiers, types, and error values to your caller. The snippets are pattern examples, not drop-in replacements.

This file is the canonical reference for analyzer-triggered anti-patterns. It used to live as Recipe 11 in `trellis-api-cookbook.md` and was extracted so that:

1. It can be loaded independently when you are debugging an analyzer warning.
2. The reference list in `copilot-instructions.md` can name it directly, so AI sessions are more likely to load it.
3. The cookbook's Patterns Index can route by symptom into this file when the symptom is "I am getting `TRLSxxx`."

The analyzer rules themselves are documented in `trellis-api-analyzers.md` (severity, when they fire, suppression guidance). This file is the *applied* form — the snippets you adapt.

## TRLS001 — Result return value not handled

```csharp
// WRONG — Result<T> dropped on the floor
PlaceOrder(cmd);                                   // TRLS001

// FIX 1 — propagate up the ROP chain (preferred when the caller is itself in a Result pipeline).
return PlaceOrder(cmd).Map(_ => Unit.Value);

// FIX 2 — terminal side-effect via Switch (void-returning; for fire-and-forget log/metric).
PlaceOrder(cmd).Switch(
    onSuccess: _       => logger.LogInformation("Order placed."),
    onFailure: failure => logger.LogWarning("Order rejected: {Code}", failure.Code));

// FIX 3 — terminal projection via Match (both branches return a value; use when the
// caller needs an int/IActionResult/string back, not just a side effect).
int statusCode = PlaceOrder(cmd).Match(
    onSuccess: _       => 200,
    onFailure: failure => 422);
```

> Don't throw from inside `Match` / `Switch` to "handle" failure — it defeats the point of `Result<T>`. Use `Switch` for void side-effects and propagate the `Result` up the chain instead. (Note: TRLS010 only fires inside chain methods like `Bind`/`Map`/`Tap`/`Ensure` — not `Match` or `Switch` — so the analyzer won't catch this; it's a Result-discipline guideline, not an analyzer rule.)

## TRLS003 — Unsafe `Maybe.Value`

```csharp
// WRONG
string city = customer.Email.Value;                // TRLS003

// FIX 1 — guard
if (customer.Email.HasValue) { var v = customer.Email.Value; }

// FIX 2 — convert to Result
Result<EmailAddress> r = customer.Email.ToResult(new Error.NotFound(ResourceRef.For("Email", customer.Id)));
```

## TRLS010 — Throwing in a Result chain

```csharp
// WRONG
.Bind(o => throw new InvalidOperationException("bad"))   // TRLS010

// FIX
.Bind(o => Result.Fail<Order>(new Error.Conflict(ResourceRef.For<Order>(o.Id), "invalid_state")))
```

## TRLS016 — `HasIndex` on a `Maybe<T>` property

```csharp
// WRONG
b.HasIndex(c => c.Email);                          // TRLS016 — silently no-op

// FIX
b.HasTrellisIndex(c => new { c.Email });
```

## TRLS017 — Wrong attribute namespace on a value object

```csharp
// WRONG — System.ComponentModel.DataAnnotations
[System.ComponentModel.DataAnnotations.StringLength(10)]    // TRLS017 — generator ignores it
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;

// FIX
[Trellis.StringLength(10)]
public sealed partial class CurrencyCode : RequiredString<CurrencyCode>;
```

## TRLS018 — Unsafe `Result<T>` deconstruction

```csharp
// WRONG
var (ok, value, err) = result;
SendEmail(value);                                  // TRLS018 — value is default on failure

// FIX
var (ok, value, err) = result;
if (!ok) return err.ToHttpResponse();
SendEmail(value);                                  // gated by !ok early-return
```

## TRLS019 — `default(Result)` / `default(Maybe<T>)`

```csharp
// WRONG
return default;                                    // TRLS019 — typed FAILURE, not success
return default(Maybe<Email>);                      // TRLS019 — equivalent to .None but obscure

// FIX
return Result.Ok();
return Maybe<Email>.None;
```

## TRLS013 — Unsafe `Maybe<T>.Value` in LINQ projection

Direct `.Value` access on `Maybe<T>` inside Select-family LINQ projections throws for `None` elements unless an earlier `.Where(...)` lambda mentions `HasValue`.

Pick FIX 1 for in-memory or analyzer-clean projection pipelines: filter first, then project.

```csharp
// WRONG — projection reads Maybe<T>.Value before proving every element has a value
IEnumerable<int> numbers = values.Select(m => m.Value);

// FIX 1 — prior Where lambda mentions HasValue before the Value projection
IEnumerable<int> numbers = values
    .Where(m => m.HasValue)
    .Select(m => m.Value);
```

Pick FIX 2 for EF Core query composition over mapped `Maybe<T>` properties: register the interceptor and use the typed query helpers for predicates when they match the query.

```csharp
// FIX 2 — EF Core path: enable Trellis query rewriting and prefer typed Maybe predicates
optionsBuilder.AddTrellisInterceptors();

IQueryable<Order> submitted = db.Orders.WhereHasValue(o => o.SubmittedAt);
```

> TRLS013 suppression is keyword-presence based: the prior `.Where(...)` body only has to mention `HasValue`, so predicate-shape verification (for example, distinguishing `m => m.HasValue` from `m => !m.HasValue`) is a known limitation. The analyzer recognizes prior `.Where(...)` chains for projections; `MaybeQueryableExtensions` are the EF translation path, not a general-purpose TRLS013 suppression mechanism.

## TRLS054 — `Maybe<T>.Equals` in an `IQueryable` expression

`MaybeExpressionRewriter` translates natural `==` / `!=` operator comparisons, not opaque `.Equals(...)` calls.

```csharp
// WRONG — EF Core sees an opaque Maybe<T>.Equals call
IQueryable<Order> overdue = db.Orders
    .Where(o => o.SubmittedAt.Equals(Maybe.From(cutoff)));   // TRLS054

// WRONG — object.Equals is equally opaque to the rewriter
IQueryable<Order> missing = db.Orders
    .Where(o => object.Equals(o.SubmittedAt, Maybe<DateTime>.None)); // TRLS054

// FIX 1 — natural-form operators are the supported expression-tree shape
IQueryable<Order> overdue = db.Orders
    .Where(o => o.SubmittedAt == Maybe.From(cutoff));

// FIX 2 — for ad-hoc EF queries, prefer the typed helper when it matches
IQueryable<Order> overdue = db.Orders
    .WhereEquals(o => o.SubmittedAt, cutoff);
```

> TRLS054 is scoped to `IQueryable` / `System.Linq.Queryable` lambdas. In-memory `IEnumerable<T>` comparisons are allowed because no EF translation is involved.

## TRLS055 — Non-inline `HasValueWhere` in an `IQueryable` expression

`HasValueWhere` is translatable only when the predicate body is visible as an inline lambda inside the query expression tree.

```csharp
Func<DateTime, bool> isOverdue = submittedAt => submittedAt < cutoff;

// WRONG — captured delegate variables are opaque to MaybeExpressionRewriter
IQueryable<Order> overdue = db.Orders
    .Where(o => o.SubmittedAt.HasValueWhere(isOverdue));      // TRLS055

// FIX 1 — inline the predicate so the rewriter can substitute the storage member
IQueryable<Order> overdue = db.Orders
    .Where(o => o.SubmittedAt.HasValueWhere(submittedAt => submittedAt < cutoff));

// FIX 2 — materialize first when the delegate must remain a runtime value
IEnumerable<Order> overdueInMemory = db.Orders
    .AsEnumerable()
    .Where(o => o.SubmittedAt.HasValueWhere(isOverdue));
```

> Method groups and member-held delegates have the same limitation as local `Func<T, bool>` variables: EF Core cannot translate a delegate body it cannot see.

## TRLS015 — Use `SaveChangesResultAsync` instead of `SaveChangesAsync`

Direct `SaveChanges`/`SaveChangesAsync` calls bypass the Result pipeline and turn database errors into unhandled exceptions.

Pick FIX 1 when the non-UoW caller discards the affected-row count.

```csharp
// WRONG — raw EF save bypasses Result error handling
await db.SaveChangesAsync(ct);

// FIX 1 — preserve Result pipeline semantics when the count is not needed
await db.SaveChangesResultUnitAsync(ct);
```

Pick FIX 2 when the non-UoW caller needs the affected-row count.

```csharp
// WRONG — raw EF save returns an int by throwing on database failures
int count = await db.SaveChangesAsync(ct);

// FIX 2 — keep the affected-row count inside Result<int>
Result<int> result = await db.SaveChangesResultAsync(ct);
```

> Under `AddTrellisUnitOfWork<TContext>`, repositories should stage changes only and not call `SaveChanges`/`SaveChangesAsync` at all. `TransactionalCommandBehavior` owns commit.

## TRLS020 — Composite value object DTO property is not safely deserializable

Composite value objects exposed through request/response DTOs need a supported JSON transport so binding round-trips through `TryCreate`. A bare composite DTO property must use `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]` on the value-object type. A `Maybe<TComposite>` DTO property is not analyzer-clean even when the inner composite type has that converter; use a nullable transport (`TComposite?`) plus `Maybe.From(...)` at the endpoint/API seam instead. The analyzer only inspects DTOs that are visible through a controller `[FromBody]` parameter or response type, a minimal API endpoint handler parameter, or a Mediator message type — the DTO type alone is not enough to trip the rule.

```csharp
// WRONG — bare composite [OwnedEntity] value object exposed as a [FromBody] DTO property without the converter
[OwnedEntity]
public sealed partial class Money : ValueObject
{
    public string Currency { get; }
    public decimal Amount { get; }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Currency;
        yield return Amount;
    }
}

public sealed record CreateInvoiceRequest(Money Total);

[ApiController]
[Route("invoices")]
public sealed class InvoicesController : ControllerBase
{
    [HttpPost]
    public IActionResult Create([FromBody] CreateInvoiceRequest request) => Ok();
}

// FIX 1 — put the converter on the composite value object type
[JsonConverter(typeof(CompositeValueObjectJsonConverter<Money>))]
[OwnedEntity]
public sealed partial class Money : ValueObject
{
    // ...same body as above...
}
```

> The current TRLS020 analyzer checks bare composite value-object DTO properties by looking for the converter on the composite **type**, not the DTO property. It also flags `Maybe<TComposite>` DTO properties because Trellis does not provide a `MaybeCompositeValueObjectJsonConverterFactory`; use `TComposite?` on the wire and convert to/from `Maybe<TComposite>` at the API seam.

## TRLS035 — `Maybe<T>` property should be `partial`

Severity: Warning.

A non-partial `Maybe<T>` auto-property on a `partial` entity type prevents the EF Core source generator from emitting the nullable backing field that Trellis conventions map.

```csharp
// WRONG — generator cannot emit the mapped backing field for a non-partial property
public partial class Customer
{
    public Maybe<PhoneNumber> Phone { get; set; } // TRLS035
}

// FIX — make the property partial so the generator can provide the implementation
public partial class Customer
{
    public partial Maybe<PhoneNumber> Phone { get; set; }
}
```

## TRLS036 — `[OwnedEntity]` type must be `partial`

Type is decorated with `[OwnedEntity]` but is not declared `partial`, so the source generator cannot emit the private parameterless constructor.

```csharp
// WRONG — generator cannot add the EF constructor to a non-partial type
[OwnedEntity]
public sealed class Address : ValueObject
{
}

// FIX 1 — make the owned value object partial so generation can extend it
[OwnedEntity]
public sealed partial class Address : ValueObject
{
}
```

> Severity: Error. TRLS038 is reported first when the type also fails to inherit from `ValueObject`, so a non-partial non-`ValueObject` type emits TRLS038 rather than both diagnostics.

## TRLS037 — `[OwnedEntity]` type already declares a parameterless constructor

Type already has a parameterless constructor; remove it to let the `[OwnedEntity]` source generator emit one, or remove the `[OwnedEntity]` attribute.

```csharp
// WRONG — hand-written parameterless constructor suppresses generator emission
[OwnedEntity]
public sealed partial class Address : ValueObject
{
    public Address() { }
}

// FIX 1 — delete the hand-written parameterless constructor and let the generator own it
[OwnedEntity]
public sealed partial class Address : ValueObject
{
}
```

> Severity: Warning. Any explicit parameterless constructor suppresses the generated one. Default guidance is to delete it; keep a private one only with a documented reason and the understanding that generator emission is intentionally suppressed.

## TRLS038 — `[OwnedEntity]` type must inherit from `ValueObject`

Type is decorated with `[OwnedEntity]` but does not inherit from `Trellis.ValueObject`; `[OwnedEntity]` is only supported on `ValueObject`-derived types.

```csharp
// WRONG — [OwnedEntity] is applied to a plain class
[OwnedEntity]
public sealed partial class Address
{
}

// FIX 1 — make the owned entity a Trellis ValueObject
[OwnedEntity]
public sealed partial class Address : ValueObject
{
}
```

> Severity: Error. When TRLS038 fires, the generator skips source generation for that type.

## TRLS039 — Unsupported scalar value primitive for AOT-safe JSON converter

Severity: Warning.

The ASP source generator can emit reflection-free JSON converters only for its supported primitive set. When a scalar value object wraps another primitive, the generator skips converter emission so AOT builds do not inherit reflection-based `JsonSerializer` calls.

```csharp
// WRONG — TimeSpan is outside the AOT-safe primitive set, so no converter is generated
public sealed class Duration : ScalarValueObject<Duration, TimeSpan>, IScalarValue<Duration, TimeSpan>
{
    // TryCreate implementation omitted for brevity. // TRLS039
}

// FIX 1 — model the value with a supported primitive so the generator can emit a converter
public sealed partial class DurationTicks : RequiredLong<DurationTicks>;

// FIX 2 — keep the unsupported primitive only with an explicit custom JsonConverter<T>
// and a local suppression documenting that the custom converter owns serialization.
```

## TRLS056 — Required value object redeclares a generated member

`Required*<TSelf>` partial classes get their factory, parse, conversion, and GUID helper surface from `RequiredPartialClassGenerator`. Do not redeclare those members in the user partial.

```csharp
// WRONG — TryParse is generated for RequiredString<TSelf>
public sealed partial class CustomerCode : RequiredString<CustomerCode>
{
    public static bool TryParse(string? s, IFormatProvider? provider, out CustomerCode result) // TRLS056
    {
        result = default!;
        return false;
    }
}

// FIX — remove the redundant declaration and rely on the generated TryParse
public sealed partial class CustomerCode : RequiredString<CustomerCode>;
```

> Severity: Error. The generator reports at the user member and skips emitting the conflicting generated member, so the diagnostic points at the redundant declaration instead of surfacing as a generic `CS0111` / `CS0102` duplicate-member error from generated source.

## (No analyzer) — `Result.FailAfterCommit` composed with aggregating operators

Not an analyzer-flagged rule (no diagnostic ID), but a recurring shape that the FailAfterCommit XML doc cautions against. `Result.FailAfterCommit<TValue>(error)` is a **leaf** worker-handler operation: it converts a single aggregate's transient external rejection into a persisted `permanently_failed` state and returns. Threading that result through `Combine` / `TraverseAll` / `SequenceAll` / `WhenAllAsync` OR-accumulates the `PersistOnFailure` flag onto the aggregated failure — `TransactionalCommandBehavior` then commits the staged permanent-failure mutation alongside whatever the other legs produced, which is almost never what the handler author intended.

```csharp
// WRONG — FailAfterCommit composed with Combine: the staged permanent-failure mutation
// commits alongside the validation-failure leg, even though the validation failure was
// the deciding factor.
public async Task<Result<OrderOutcome>> Handle(ProcessOrderCommand cmd, CancellationToken ct)
{
    Result<Unit> stagePermanentFailure = await MarkOrderAsPermanentlyFailedAsync(cmd.OrderId, ct);
    // ↑ returns Result.FailAfterCommit(new Error.Unavailable(...))

    Result<int> independentRule = Result.Fail<int>(
        Error.InvalidInput.ForRule("downstream_limit_exceeded", "Customer is over quota."));

    return stagePermanentFailure
        .Combine(independentRule)
        .Map((_, _) => new OrderOutcome(/* ... */));
    // ↑ aggregated Error contains BOTH inner errors AND carries PersistOnFailure = true,
    //   so TransactionalCommandBehavior commits the permanently_failed mutation.
}

// FIX — Treat FailAfterCommit as a terminal step. Run the aggregating composition to its
// own terminal outcome first, THEN decide whether to invoke FailAfterCommit (typically in
// a separate command or at the end of the handler with no further composition).
public async Task<Result<OrderOutcome>> Handle(ProcessOrderCommand cmd, CancellationToken ct)
{
    Result<int> independentRule = Result.Fail<int>(
        Error.InvalidInput.ForRule("downstream_limit_exceeded", "Customer is over quota."));

    if (independentRule.IsFailure)
        return Result.Fail<OrderOutcome>(independentRule.Error!);

    // Now decide whether the external state warrants a persisted permanent-failure record.
    // No composition with other legs — FailAfterCommit is the leaf.
    return (await MarkOrderAsPermanentlyFailedAsync(cmd.OrderId, ct))
        .Map(_ => new OrderOutcome(/* ... */));
}
```

> Severity: Documentation only — no analyzer fires. The intent of `FailAfterCommit` is durable persistence of a permanent-failure state on a single aggregate; aggregating it across legs reaches outside that intent and produces partial commits the consumer rarely wants.

## (No analyzer) — Domain event handler raises more domain events during dispatch

Domain-event handlers are side-effect-only. The dispatch behaviors snapshot `UncommittedEvents()` at entry and publish only that snapshot. If a handler appends events to the same aggregate, or mutates another aggregate in a tracked-dispatch snapshot, post-dispatch validation throws `DomainEventHandlerCascadedException`; `AcceptChanges()` is not called, so operators can inspect the original and cascaded events.

```csharp
// WRONG — handler mutates the source aggregate and raises another event during dispatch.
public sealed class AutoAdvanceOrderHandler(IOrderRepository orders)
    : IDomainEventHandler<OrderCreatedEvent>
{
    public async ValueTask HandleAsync(OrderCreatedEvent domainEvent, CancellationToken cancellationToken)
    {
        Order order = await orders.GetAsync(domainEvent.OrderId, cancellationToken);
        order.RaiseStatusChanged(OrderStatus.ReadyForFulfillment);
        // ↑ Raises OrderStatusChanged while OrderCreatedEvent is being dispatched.
        //   Post-dispatch validation throws DomainEventHandlerCascadedException.
    }
}

// FIX — follow-up domain mutation is a separate top-level command issued after the
// originating command completes. This is application-layer orchestration, not handler re-entry.
public sealed class OrderWorkflow(IMediator mediator)
{
    public async ValueTask<Result<Unit>> CreateAndAdvanceAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Result<Order> created = await mediator.Send(command, cancellationToken);
        if (!created.TryGetValue(out var order))
            return Result.Fail<Unit>(created.Error!);

        return await mediator.Send(
            new ChangeOrderStatusCommand(order.Id, OrderStatus.ReadyForFulfillment),
            cancellationToken);
    }
}
```

> Do not move the `mediator.Send(new ChangeOrderStatusCommand(...))` call into the `IDomainEventHandler<TEvent>`. The tracked-dispatch reentrancy guard skips nested tracked dispatch, so events raised by the nested command can be stranded. Queue post-commit work or issue the follow-up command from the application layer after the originating command completes.

Default handler exceptions are still **logged and swallowed** by `MediatorDomainEventPublisher`; cascade detection only catches handler-raised events. Durable side effects and durable at-least-once retry require the outbox pattern — planned for a future release, not shipped today.

