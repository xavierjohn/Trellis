---
package: Trellis.Analyzers (applied form)
namespaces: [Trellis, Trellis.Analyzers]
types: [TRLS001, TRLS003, TRLS010, TRLS013, TRLS014, TRLS015, TRLS016, TRLS018, TRLS019, TRLS020, TRLS021, TRLS035, TRLS036, TRLS037, TRLS038, TRLS039, TRLS054, TRLS055, TRLS056, TRLS059, TRLS062, TRLS063, TRLS064]
related_docs: [trellis-api-analyzers.md, trellis-api-cookbook.md]
version: v4
last_verified: 2026-08-18
audience: [llm]
---
# Trellis Anti-Pattern → Fix Gallery

> **Requires `Trellis.Analyzers`.** This gallery is organised by `TRLS###` diagnostic, but those diagnostics only fire while the consuming project holds a `PackageReference` to `Trellis.Analyzers`. Referencing `Trellis.Core` alone leaves all of them silent, and this file is delivered by `Trellis.Core` — so its presence in `.github/` does **not** mean the analyzers are active. Check the `.csproj`. The FIX shapes below remain correct either way; what changes is whether anything tells you when you have drifted from them.

> A condensed atlas mapping each common Trellis analyzer trigger to its idiomatic fix. **Read this file alongside `trellis-api-cookbook.md` whenever you are touching a Trellis pipeline.** Each section's WRONG/FIX pair captures the canonical control-flow shape the analyzer expects — preserve that shape and adapt identifiers, types, and error values to your caller. The snippets are pattern examples, not drop-in replacements.

This file is the canonical reference for analyzer-triggered anti-patterns. It used to live as Recipe 11 in `trellis-api-cookbook.md` and was extracted so that:

1. It can be loaded independently when you are debugging an analyzer warning.
2. The reference list in `copilot-instructions.md` can name it directly, so AI sessions are more likely to load it.
3. The cookbook's Patterns Index can route by symptom into this file when the symptom is "I am getting `TRLSxxx`."

The analyzer rules themselves are documented in `trellis-api-analyzers.md` (severity, when they fire, suppression guidance). This file is the *applied* form — the snippets you adapt.

## Use this file when

- The compiler or IDE reports a `TRLSxxx` diagnostic and you need the fix shape, not the rule's specification.
- You are writing or reviewing a `Result`/`Maybe` pipeline, an EF Core mapping for a value object, or a composite value object crossing the JSON boundary — the three areas that generate almost all Trellis diagnostics.
- A reviewer flagged a shape as unidiomatic and you want the canonical form the analyzers were written to enforce.

Jump straight to the section named by your diagnostic ID. If you have a symptom rather than an ID, route through the table below.

## Patterns Index

| Symptom | Diagnostic | Fix |
|---|---|---|
| A call returning `Result`/`Result<T>` is invoked as a statement and its outcome silently vanishes | TRLS001 | [Result return value not handled](#trls001--result-return-value-not-handled) |
| Reading `.Value` on a `Maybe<T>` that may be empty | TRLS003 | [Unsafe `Maybe.Value`](#trls003--unsafe-maybevalue) |
| Reading `.Value` on a `Maybe<T>` inside a LINQ projection | TRLS013 | [Unsafe `Maybe<T>.Value` in LINQ projection](#trls013--unsafe-maybetvalue-in-linq-projection) |
| Throwing to signal a business failure inside a Result chain | TRLS010 | [Throwing in a Result chain](#trls010--throwing-in-a-result-chain) |
| Deconstructing a `Result<T>` without checking success first | TRLS018 | [Unsafe `Result<T>` deconstruction](#trls018--unsafe-resultt-deconstruction) |
| Using `default(Result)` or `default(Maybe<T>)` as if it were success | TRLS019 | [`default(Result)` / `default(Maybe<T>)`](#trls019--defaultresult--defaultmaybet) |
| A `Combine` chain has grown past the largest supported tuple | TRLS014 | [Combine chain exceeds maximum supported tuple size](#trls014--combine-chain-exceeds-maximum-supported-tuple-size) |
| Indexing or otherwise configuring a `Maybe<T>` property in EF Core | TRLS016 | [`HasIndex` on a `Maybe<T>` property](#trls016--hasindex-on-a-maybet-property) |
| Hand-writing EF configuration that Trellis conventions already apply | TRLS021 | [EF configuration duplicates Trellis conventions](#trls021--ef-configuration-duplicates-trellis-conventions) |
| Calling `SaveChangesAsync` instead of the Result-returning form | TRLS015 | [Use `SaveChangesResultAsync`](#trls015--use-savechangesresultasync-instead-of-savechangesasync) |
| Comparing a `Maybe<T>` with `Equals` inside an EF query | TRLS054 | [`Maybe<T>.Equals` in an `IQueryable` expression](#trls054--maybetequals-in-an-iqueryable-expression) |
| A `HasValueWhere` predicate is not inlined into the query | TRLS055 | [Non-inline `HasValueWhere`](#trls055--non-inline-hasvaluewhere-in-an-iqueryable-expression) |
| A composite value object on a DTO cannot round-trip through deserialization | TRLS020 | [Composite value object DTO property is not safely deserializable](#trls020--composite-value-object-dto-property-is-not-safely-deserializable) |
| A `Maybe<T>` or `[OwnedEntity]` type will not compile against its generated half | TRLS035, TRLS036, TRLS037, TRLS038 | [`Maybe<T>` must be `partial`](#trls035--maybet-property-should-be-partial), [`[OwnedEntity]` must be `partial`](#trls036--ownedentity-type-must-be-partial), [parameterless constructor](#trls037--ownedentity-type-already-declares-a-parameterless-constructor), [must inherit `ValueObject`](#trls038--ownedentity-type-must-inherit-from-valueobject) |
| A value object breaks under Native AOT or trimming when serialized | TRLS039, TRLS059 | [Unsupported scalar primitive](#trls039--unsupported-scalar-value-primitive-for-aot-safe-json-converter), [context without `[JsonSerializable]`](#trls059--generatescalarvalueconverters-context-without-jsonserializable) |
| Redeclaring `Id`, equality, or `TryCreate` that a base type already supplies | TRLS056 | [Required value object redeclares a generated member](#trls056--required-value-object-redeclares-a-generated-member) |
| Post-commit failures disappear when combined with `Combine`/`Sequence` | *(no analyzer)* | [`Result.FailAfterCommit` with aggregating operators](#no-analyzer--resultfailaftercommit-composed-with-aggregating-operators) |
| A domain event handler raises further domain events and they are lost or dispatched twice | *(no analyzer)* | [Domain event handler raises more domain events](#no-analyzer--domain-event-handler-raises-more-domain-events-during-dispatch) |

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

**Guard shapes the analyzer already accepts.** TRLS003 does not require the nested `if` above — do not restructure working code to satisfy it. Any of these suppress the diagnostic:

| Shape | Example |
| --- | --- |
| `HasValue` / `HasNoValue` condition | `if (m.HasValue) { … m.Value … }` |
| Early-return guard | `if (m.HasNoValue) return …; … m.Value` |
| Property pattern (incl. negated and ternary forms) | `if (m is { HasValue: true }) … m.Value` |
| Short-circuiting `&&` | `if (m.HasValue && m.Value.IsActive) …` |
| Prior assignment that cannot be `None` | `var x = Maybe<int>.From(n); … x.Value` — only when `T` is a non-nullable value type, where `From` can never yield `None` |
| `TryGetValue` block | `if (m.TryGetValue(out var v)) …` |
| Lambda body of a Trellis `Maybe` operator | the value-side lambda of `Bind` / `Map` / `Tap` / `Ensure` (and `*Async` forms), or the `onSome` argument of `Match` / `Switch` — it only runs when a value exists |

Prefer the early-return guard in ROP code: it keeps the happy path unindented and matches the surrounding `Result` style. FIX 2 remains the better answer whenever the absence is a *domain* outcome the caller must handle rather than a local precondition.

## TRLS010 — Throwing in a Result chain

```csharp
// WRONG
.Bind(o => throw new InvalidOperationException("bad"))   // TRLS010

// FIX
.Bind(o => Result.Fail<Order>(new Error.Conflict(ResourceRef.For<Order>(o.Id), "invalid-state")))
```

## TRLS016 — `HasIndex` on a `Maybe<T>` property

```csharp
// WRONG
b.HasIndex(c => c.Email);                          // TRLS016 — silently no-op

// FIX
b.HasTrellisIndex(c => new { c.Email });
```

## TRLS021 — EF configuration duplicates Trellis conventions

When a `DbContext` opts into Trellis conventions, manual mapping for `Maybe<T>` and `[OwnedEntity]` properties is redundant.

```csharp
// WRONG — in a context with ApplyTrellisConventions wired
builder.Property(o => o.SubmittedAt).HasConversion<DateTime?>(); // TRLS021
builder.OwnsOne(o => o.Total);                                  // TRLS021

// FIX — let Trellis conventions own those properties
configurationBuilder.ApplyTrellisConventions();
```

> TRLS021 correlates direct `DbContext` configuration with the specific context that wires conventions. Standalone `IEntityTypeConfiguration<TEntity>` classes are attributed through `ApplyConfiguration(new TConfig())`, `ApplyConfigurationsFromAssembly(...)`, or matching `DbSet<TEntity>` properties. Separate contexts in the same compilation are not flagged merely because one context opted in, and unattributable configuration classes are ignored.

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

Direct `.Value` access on `Maybe<T>` inside System.Linq `Enumerable` / `Queryable` Select-family LINQ projections throws for `None` elements unless an earlier `.Where(...)` lambda mentions `HasValue`.

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
> User-defined methods named `Select`, `GroupBy`, `OrderBy`, etc. are not LINQ for TRLS013 unless they resolve to `System.Linq.Enumerable` or `System.Linq.Queryable`.

## TRLS014 — Combine chain exceeds maximum supported tuple size

Trellis `Result.Combine` / `CombineAsync` supports up to nine elements.

```csharp
// WRONG
var combined = r1.Combine(r2).Combine(r3).Combine(r4).Combine(r5)
    .Combine(r6).Combine(r7).Combine(r8).Combine(r9).Combine(r10); // TRLS014

// FIX
var identity = r1.Combine(r2).Combine(r3).Combine(r4).Combine(r5);
var contact = r6.Combine(r7).Combine(r8).Combine(r9).Combine(r10);
var combined = identity.Combine(contact);
```

> The analyzer resolves the method symbol and only counts Trellis combine extension methods; user-defined `Combine` / `CombineAsync` methods are ignored.

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
return await db.SaveChangesResultUnitAsync(ct);   // propagate the Result up the ROP chain
```

Pick FIX 2 when the non-UoW caller needs the affected-row count.

```csharp
// WRONG — raw EF save returns an int by throwing on database failures
int count = await db.SaveChangesAsync(ct);

// FIX 2 — keep the affected-row count inside Result<int>
return await db.SaveChangesResultAsync(ct);       // Result<int> carries the affected-row count
```

> Under `AddTrellisUnitOfWork<TContext>`, repositories should stage changes only and not call `SaveChanges`/`SaveChangesAsync` at all. `TransactionalCommandBehavior` owns commit.

## TRLS020 — Composite value object DTO property is not safely deserializable

Composite value objects exposed through request/response DTOs need a supported JSON transport so binding round-trips through `TryCreate`.

| DTO property shape | Required transport |
|---|---|
| Bare `TComposite` | `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]` on the value-object type |
| `Maybe<TComposite>` | Not analyzer-clean even when the inner composite type has that converter. Use a nullable transport (`TComposite?`) plus `Maybe.From(...)` at the endpoint/API seam |

**Where the rule looks.** Only at DTOs actually bound from the wire: a controller `[FromBody]` parameter or response type, or a minimal API endpoint handler parameter. A Mediator command is flagged only when it is itself the bound request body at one of those seams; a command constructed server-side (mapped from a separate request DTO and never deserialized from JSON) is not flagged, because System.Text.Json never touches it. The DTO type alone is not enough to trip the rule.

```csharp
// WRONG — bare composite [OwnedEntity] value object exposed as a [FromBody] DTO property without the converter
[OwnedEntity]
public sealed partial class Money : ValueObject
{
    public string Currency { get; }
    public decimal Amount { get; }

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Currency);
        components.Add(Amount);
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

## TRLS059 — `[GenerateScalarValueConverters]` context without `[JsonSerializable]`

Severity: Warning.

Roslyn source generators all analyze the same original compilation and cannot observe one another's output. Anything Trellis emits is therefore invisible to System.Text.Json's generator, so the attributes STJ needs must be written by hand. Getting this wrong produces compiler errors that point at generated code and never mention the real cause.

```csharp
// WRONG — the context declares no [JsonSerializable] of its own.
// STJ's generator skips the context entirely and never emits the abstract members
// JsonSerializerContext requires, so the build fails with two CS0534 errors. // TRLS059
[GenerateScalarValueConverters]
public partial class AppJsonContext : JsonSerializerContext;

// WRONG — OrderId is reachable from a serialized DTO but carries no [JsonConverter]
// in original source. STJ treats it as a POCO and emits `ObjectCreator = () => new OrderId()`,
// which does not exist on a Trellis value object, failing with CS1729 inside STJ's own file.
public partial class OrderId : RequiredGuid<OrderId>;

public sealed class OrderDto
{
    public OrderId? Id { get; set; }
}

[GenerateScalarValueConverters]
[JsonSerializable(typeof(OrderDto))]
public partial class AppJsonContext2 : JsonSerializerContext;

// FIX — declare both attributes yourself.
[JsonConverter(typeof(ParsableJsonConverter<OrderId>))]
public partial class OrderId : RequiredGuid<OrderId>;

[GenerateScalarValueConverters]
[JsonSerializable(typeof(OrderDto))]
public partial class AppJsonContext : JsonSerializerContext;
```

Declaring `[JsonConverter]` yourself is explicitly supported: `RequiredPartialClassGenerator` detects the attribute on your partial and skips its own, so there is no CS0579 duplicate. Use `ParsableJsonConverter<T>` for scalar `Required*` types and `RequiredEnumJsonConverter<T>` for `RequiredEnum<T>` — the same converters Trellis would have emitted.

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

## TRLS062 — `ValidateAdditional` rejects a value without naming a reason

A custom rule is the value object's own reason for rejecting a value. The three-argument `ValidateAdditional` can reject but has nowhere to put that reason, so the failure reaches the client as `error.unspecified` — indistinguishable from every other unnamed failure.

```csharp
// WRONG — rejects, but the client is told only that something was invalid
public sealed partial class ReservationCode : RequiredString<ReservationCode>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage) // TRLS062
    {
        if (!value.StartsWith("RES-", StringComparison.Ordinal))
            errorMessage = "Reservation Code must start with RES-.";
    }
}

// FIX — take the four-argument overload and name the failure
public sealed partial class ReservationCode : RequiredString<ReservationCode>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage, ref string? errorCode)
    {
        if (value.StartsWith("RES-", StringComparison.Ordinal))
            return;

        errorMessage = "Reservation Code must start with RES-.";
        errorCode = "reservation.code.malformed";
    }
}
```

> Severity: Info. The three-argument form stays legal and behaves exactly as before, so this is a prompt rather than a gate. Declare only one of the two overloads — declaring both is `TRLS061`, an error. Leaving `errorCode` unset or blank in the four-argument form falls back to `error.unspecified`, which is the same outcome as never having taken the overload.

## TRLS063 — FluentValidation `Must` rule with no `WithErrorCode`

Every built-in FluentValidation validator carries a name that `Trellis.FluentValidation` projects to a real reason code. `Must` and `MustAsync` are the exception: they report as `PredicateValidator` and `AsyncPredicateValidator`, both of which project to the `error.unspecified` sentinel. Since `Must` is also the validator applications reach for most, it is the largest single producer of failures a client cannot branch on.

```csharp
// WRONG — the client receives error.unspecified and cannot tell these two apart
RuleFor(x => x.Name).Must(BeKnownCustomer);                 // TRLS063
RuleFor(x => x.Slot).MustAsync(BeAvailableAsync);           // TRLS063

// FIX — name each failure
RuleFor(x => x.Name).Must(BeKnownCustomer).WithErrorCode("customer.unknown");
RuleFor(x => x.Slot).MustAsync(BeAvailableAsync).WithErrorCode("slot.taken");
```

A `WithErrorCode` only counts for the rule component it follows. Modifiers such as `WithMessage`, `When`, and `WithName` can sit in between, but another validator starts a new component:

```csharp
// WRONG — the code names the second Must; the first is still unnamed
RuleFor(x => x.Name)
    .Must(BeLongEnough)                                     // TRLS063
    .Must(BeShortEnough)
    .WithErrorCode("name.too.long");

// FIX — one code per component
RuleFor(x => x.Name)
    .Must(BeLongEnough).WithErrorCode("name.too.short")
    .Must(BeShortEnough).WithErrorCode("name.too.long");
```

> Severity: Info, because uncoded `Must` rules are legal and existing validators are full of them. Raise it with `dotnet_diagnostic.TRLS063.severity = warning` once a codebase has caught up. There is deliberately no code fix: only the author knows what the rule's failure should be called, and a placeholder code looks deliberate on the wire in a way the sentinel does not.

The analyzer reports only where it can prove no code applies. It stays silent when the rule's value escapes the statement (`var rule = RuleFor(...).Must(...);`), when the chain calls `Configure(...)` — which can set `ErrorCode` directly — or when it passes through any helper your application declared, including one placed in `namespace FluentValidation`, since such a helper may itself wrap `WithErrorCode`. Those shapes may well be uncoded, but a false accusation against an author who did the right thing costs more than a miss on a shape this rule flags everywhere else.

## TRLS064 — reason-code literal that collides with the frozen vocabulary

Trellis freezes a small set of reason codes and dispatches on their exact wire spelling. Restating one as a literal works right up until the day it doesn't: a typo in a literal is a silent wire break, whereas a typo in a constant name does not compile.

```csharp
// WRONG — the literal is unchecked, and there are usually dozens of it
Error.InvalidInput.ForField("email", "value.not-null");        // TRLS064
Error.InvalidInput.ForField("name", "string.max-length");      // TRLS064

// FIX — the constant is the same string, checked by the compiler
Error.InvalidInput.ForField("email", ValidationCodes.ValueNotNull);
Error.InvalidInput.ForField("name", ValidationCodes.StringMaxLength);
```

A code fix applies this, and because the motivating case is one literal repeated across a codebase, fix-all is supported.

The other two shapes claim a name that is not yours to claim. `error.*` is reserved for the single `error.unspecified` sentinel — a second member there makes the fallback lossy, because a client that sees `error.` can no longer read it as "this failure was never named". And a first segment the framework publishes a meaning for makes an application failure read as a framework one to any client using the documented prefix fallback:

```csharp
// WRONG — squats a reserved or framework-owned namespace
Error.InvalidInput.ForField("token", "error.expired");         // TRLS064
Error.InvalidInput.ForField("total", "money.over-budget");     // TRLS064

// FIX — name it in your own domain's terms
Error.InvalidInput.ForField("token", "session.expired");
Error.InvalidInput.ForField("total", "budget.exceeded");
```

The same three findings apply wherever a reason code is written, not only to a `reasonCode` argument. FluentValidation's `WithErrorCode` and the `Code` property on the Trellis primitive attributes both reach the wire the same way:

```csharp
// WRONG
RuleFor(x => x.Name).NotEmpty().WithErrorCode("value.not-empty");   // TRLS064 — restates a frozen code
RuleFor(x => x.Age).Must(BeAdult).WithErrorCode("value.too-young"); // TRLS064 — squats value.*

[StringLength(50, Code = "string.max-length")]                      // TRLS064 — restates a frozen code
public partial class DisplayName : RequiredString<DisplayName>;

// FIX
RuleFor(x => x.Name).NotEmpty().WithErrorCode(ValidationCodes.ValueNotEmpty);
RuleFor(x => x.Age).Must(BeAdult).WithErrorCode("customer.under-age");

[StringLength(50, Code = ValidationCodes.StringMaxLength)]
public partial class DisplayName : RequiredString<DisplayName>;
```

Setting `Code` to a name of your own is the documented reason the property exists, so `[NotDefault(Code = "tenant.id.missing")]` is silent — as it should be.

> Severity: Info. **This rule does not check vocabulary membership**, and that boundary is deliberate: the freeze constrains Trellis, not your application, and `trellis-api-primitives.md` promises that no analyzer pressures the choice to override a framework code or keep it. A novel, well-formed code of your own — `order.cancel-after-ship`, or a bare `required` — is silent, including where it is a synonym for a code Trellis also has. Only restating a frozen code, or claiming a namespace whose meaning Trellis has published, is reported. Note also that only *literals* are reported: `ValidationCodes.ValueNotNull` is the recommended shape, so matching on constant value rather than syntax would flag the fix itself.

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
        Error.InvalidInput.ForRule("quota.downstream-limit-exceeded", "Customer is over quota."));

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
        Error.InvalidInput.ForRule("quota.downstream-limit-exceeded", "Customer is over quota."));

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

Default handler exceptions are still **logged and swallowed** by `MediatorDomainEventPublisher`; cascade detection only catches handler-raised events. Durable side effects and durable at-least-once retry require the transactional outbox, which **is shipped** in `Trellis.EntityFrameworkCore.Outbox` — wire it via `AddTrellisOutbox<TContext>()`, `AddTrellisOutbox(ModelBuilder)`, and `AddTrellisOutboxInterceptor(...)`. See [trellis-api-efcore-outbox.md](trellis-api-efcore-outbox.md#use-this-file-when).
