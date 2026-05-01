---
title: RequiredEnum
package: Trellis.Core
topics: [required-enum, primitive-obsession, validation, source-generator, analyzer, json, ddd]
related_api_reference: [trellis-api-primitives.md, trellis-api-analyzers.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# RequiredEnum

`RequiredEnum<TSelf>` is the Trellis primitive base for finite, behavior-rich symbolic value sets — a typed replacement for `enum` that round-trips through JSON, model binding, and EF Core via the bundled source generator.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Declare a symbolic set with optional behavior | `partial class : RequiredEnum<TSelf>` + `public static readonly TSelf` fields | [Defining members](#defining-members) |
| Override the wire / serialized name for one member | `[EnumValue("...")]` on the field | [Symbolic value names](#symbolic-value-names) |
| Parse user input safely (nullable, with field name) | `TryCreate(string?, fieldName?)` → `Result<TSelf>` | [Parsing and creation](#parsing-and-creation) |
| Throwing factory for trusted input | `Create(string)` | [Parsing and creation](#parsing-and-creation) |
| Look up by symbolic name (case-insensitive) | `TryFromName(string?, fieldName?)` | [Parsing and creation](#parsing-and-creation) |
| `IParsable<TSelf>` for ASP.NET binding pipelines | `Parse(s, provider)` / `TryParse(s, provider, out)` | [Parsing and creation](#parsing-and-creation) |
| JSON round-trip in `System.Text.Json` | Auto-applied `[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]` | [JSON serialization](#json-serialization) |
| Membership and negated membership | `Is(params TSelf[])` / `IsNot(params TSelf[])` | [Equality and membership](#equality-and-membership) |
| Persist symbolic values in EF Core | `HasConversion(status => status.Value, value => Status.Create(value))` | [Composition](#composition) |

## Use this guide when

- You want a closed set of named domain values that carry data or behavior, not just an `int`.
- You need stable string identities for JSON, model binding, and EF Core columns.
- You want invalid values to be unrepresentable — no `(OrderStatus)999` cast hole.
- You need a Result-returning factory and case-insensitive symbolic lookup baked into the type.

## Surface at a glance

`RequiredEnum<TSelf>` lives in `Trellis.Core`, namespace `Trellis`. The base type is hand-written; the per-derived members are emitted by the bundled `Trellis.Core.Generator` (attached automatically via `analyzers/dotnet/cs/`).

| Member | Source | Purpose |
|---|---|---|
| `Value` (`string`) | base | Canonical symbolic identity. Defaults to the field name unless `[EnumValue]` overrides it. |
| `Ordinal` (`int`) | base | Declaration-order metadata. Not a wire / storage identity. |
| `static GetAll()` | base | All discovered `public static readonly TSelf` members, in declaration order. |
| `static TryFromName(string? name, string? fieldName = null)` | base | Case-insensitive lookup → `Result<TSelf>`. |
| `Is(params TSelf[])` / `IsNot(params TSelf[])` | base | Membership / negated membership. |
| `Equals` / `==` / `!=` / `GetHashCode` | base | Case-insensitive symbolic equality on `Value`. |
| `static TryCreate(string)` and `static TryCreate(string?, string? fieldName = null)` | generated | Result-returning factories; both delegate to `TryFromName`. |
| `static Create(string)` | generated | Throwing factory. |
| `static Parse(string, IFormatProvider?)` / `TryParse(...)` | generated | `IParsable<TSelf>` implementation for binding pipelines. |
| `[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]` | generated | Applied to the derived class — no manual registration needed. |

Full signatures: [`trellis-api-core.md` → `RequiredEnum<TSelf>`](../api_reference/trellis-api-core.md#requiredenumtself) and the [source-generated members section](../api_reference/trellis-api-core.md#requiredenumtself-1). Package scope: [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md).

> [!NOTE]
> Generated `TryCreate` delegates only to `TryFromName`. There is no `TryFromValue` API path; the JSON converter and parser also resolve through `TryFromName`.

## Installation

```bash
dotnet add package Trellis.Core
```

`Trellis.Primitives` transitively depends on `Trellis.Core`, so projects that already use the primitive value-object library do not need a separate install.

## Quick start

A `partial class` derivation, behavior-carrying members, and a Result-returning lookup — no other plumbing required.

```csharp
using Trellis;

namespace QuickStart;

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new(canShip: false, isTerminal: false);

    [EnumValue("awaiting-payment")]
    public static readonly OrderStatus AwaitingPayment = new(canShip: false, isTerminal: false);

    public static readonly OrderStatus Paid = new(canShip: true, isTerminal: false);
    public static readonly OrderStatus Shipped = new(canShip: false, isTerminal: false);
    public static readonly OrderStatus Cancelled = new(canShip: false, isTerminal: true);

    private OrderStatus(bool canShip, bool isTerminal)
    {
        CanShip = canShip;
        IsTerminal = isTerminal;
    }

    public bool CanShip { get; }
    public bool IsTerminal { get; }
}

public static class Demo
{
    public static void Run()
    {
        Result<OrderStatus> parsed = OrderStatus.TryCreate("awaiting-payment");
        OrderStatus paid = OrderStatus.Create("Paid");
        bool isOpen = paid.Is(OrderStatus.Draft, OrderStatus.AwaitingPayment, OrderStatus.Paid);
        bool notDone = paid.IsNot(OrderStatus.Cancelled);
        IReadOnlyCollection<OrderStatus> all = OrderStatus.GetAll();
    }
}
```

## Defining members

Members are `public static readonly` fields of type `TSelf`. The base type discovers them by reflection on first access (then caches). The discovery contract:

| Requirement | Why |
|---|---|
| Class must be `partial` | The generator augments it with `IScalarValue<TSelf, string>`, the factories, and the `[JsonConverter]` attribute. |
| Fields must be `public static readonly TSelf` | Reflection inspects only `Public \| Static \| DeclaredOnly` init-only fields whose type equals `TSelf`. |
| Constructors should be `private` (or `protected`) | Prevents external instantiation outside the declared set. |
| Each `Value` must be unique (case-insensitive) | Duplicate detection runs the first time `GetAll` / `TryFromName` / `Value` is touched and throws `InvalidOperationException` naming the duplicate. |

`Ordinal` is assigned during discovery in declaration order (0, 1, 2, …). Reordering fields changes ordinals — do not persist or transmit them.

## Symbolic value names

By default, `Value` equals the C# field name:

```csharp
OrderStatus.Paid.Value == "Paid"; // true
```

Apply `[EnumValue("...")]` only when the external symbolic name must differ from the identifier (kebab-case wire formats, legacy compatibility, etc.):

```csharp
[EnumValue("awaiting-payment")]
public static readonly OrderStatus AwaitingPayment = new(canShip: false, isTerminal: false);

OrderStatus.AwaitingPayment.Value == "awaiting-payment"; // true
```

`EnumValueAttribute` is field-targeted and lives in `Trellis.Core`, namespace `Trellis`. See [`trellis-api-core.md` → `EnumValueAttribute`](../api_reference/trellis-api-core.md#enumvalueattribute) for the full signature. Keep the field name equal to `Value` whenever possible — one source of truth is easier to read and to grep for.

## Parsing and creation

The generator emits five entry points; all of them resolve through the base-type `TryFromName`.

| API | Returns | Failure mode |
|---|---|---|
| `TryCreate(string value)` | `Result<TSelf>` | `Fail` with `Error.UnprocessableContent` for null, empty, whitespace, or unknown name. |
| `TryCreate(string? value, string? fieldName = null)` | `Result<TSelf>` | Same; `fieldName` is included in the field violation. |
| `TryFromName(string? name, string? fieldName = null)` | `Result<TSelf>` | Base-type lookup that all generated factories delegate to. |
| `Create(string value)` | `TSelf` | Throws on failure. Use only for trusted, internally-known names. |
| `Parse(string s, IFormatProvider? provider)` / `TryParse(...)` | `TSelf` / `bool` | `IParsable<TSelf>` for ASP.NET model binding pipelines. |

```csharp
Result<OrderStatus> ok   = OrderStatus.TryCreate("Paid");          // Ok(Paid)
Result<OrderStatus> fail = OrderStatus.TryCreate("paid-in-full");  // Fail(UnprocessableContent)
Result<OrderStatus> none = OrderStatus.TryCreate(null, "status");  // Fail("status cannot be empty.")

OrderStatus paid = OrderStatus.Create("Paid");                     // throws on unknown
```

Lookup is case-insensitive (`OrdinalIgnoreCase`). The error message on an unknown value lists every valid name, alphabetised — convenient for API responses but verbose; trim before exposing externally if needed.

## JSON serialization

The generator emits `[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]` on the derived class, so `System.Text.Json` round-trips with no extra registration.

| Direction | Behavior |
|---|---|
| Read | Accepts JSON `string` and `null`; resolves the string through `TryFromName`. Other token types (number, object, array, bool) throw `JsonException`. |
| Write | Emits `value.Value` as a JSON string. |

```csharp
using System.Text.Json;

string json = JsonSerializer.Serialize(OrderStatus.AwaitingPayment); // "\"awaiting-payment\""
OrderStatus back = JsonSerializer.Deserialize<OrderStatus>(json)!;   // OrderStatus.AwaitingPayment
```

Converter signature: [`trellis-api-core.md` → `RequiredEnumJsonConverter<TRequiredEnum>`](../api_reference/trellis-api-core.md#requiredenumjsonconvertertrequiredenum).

## Equality and membership

`RequiredEnum<TSelf>` produces reference-stable singletons but uses **case-insensitive symbolic equality** on `Value`. All four — `Equals(object?)`, `Equals(RequiredEnum<TSelf>?)`, `==`, and `!=` — agree, and `GetHashCode` matches.

```csharp
OrderStatus.Paid == OrderStatus.Paid;      // true (also reference-equal)
OrderStatus.Paid.Equals(OrderStatus.Paid); // true

OrderStatus paid = OrderStatus.Create("PAID"); // resolves to OrderStatus.Paid
ReferenceEquals(paid, OrderStatus.Paid);       // true — Create returns the singleton

bool isOpen  = paid.Is(OrderStatus.Draft, OrderStatus.AwaitingPayment, OrderStatus.Paid);
bool notDone = paid.IsNot(OrderStatus.Cancelled);
```

`Is` and `IsNot` accept `params TSelf[]` and call `Contains` on the array — fine for short lists; for hot paths with large sets, pre-build a `HashSet<TSelf>` once and check membership against that.

## Validation rules

| Input or condition | Outcome |
|---|---|
| `null` or whitespace name passed to `TryCreate` / `TryFromName` | `Fail` with `Error.UnprocessableContent.ForField(field, "validation.error", "{Type} cannot be empty.")` |
| Unknown name | `Fail` with message `'{name}' is not a valid {Type}. Valid values: {alphabetised list}` |
| Two members declared with the same `Value` (case-insensitive) | `InvalidOperationException` thrown by the base class on first cache build, naming the duplicate symbol |
| `[EnumValue]` on a non-`TSelf` field, an instance member, or a non-`readonly` field | Silently ignored — only `public static readonly TSelf` init-only fields are discovered |

`fieldName` defaults to the camelCased type name (e.g., `orderStatus`) when omitted. Pass it explicitly to align field-violation paths with the calling DTO property.

## Analyzer warnings

Two analyzer / generator diagnostics commonly affect `RequiredEnum<TSelf>`-derived types. Full reference: [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md).

| ID | Severity | Trigger | Fix |
|---|---|---|---|
| `TRLS017` | Warning | `[StringLength]` / `[Range]` from `System.ComponentModel.DataAnnotations` applied to a Trellis primitive class. The Trellis generator only inspects attributes from `namespace Trellis`. | Switch the `using` to `using Trellis;` or fully-qualify (`[Trellis.StringLength(...)]`). |
| `TRLS031` | Warning | Source generator detected a `Required*`-derived class whose base is not in the supported set (`RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredLong`, `RequiredDecimal`, `RequiredBool`, `RequiredDateTime`, `RequiredEnum`). | Inherit directly from `RequiredEnum<TSelf>`; do not insert intermediate base classes. |

There is no analyzer that flags a `RequiredEnum<TSelf>` declared without `partial`. The build will simply fail to find the generated `IScalarValue<TSelf, string>` implementation — if `TryCreate` / `Parse` look missing on the derived class, the class is almost always missing the `partial` keyword.

## Composition

`RequiredEnum<TSelf>` round-trips cleanly through every Trellis surface that consumes `IScalarValue<TSelf, string>` — ASP.NET model binding, FluentValidation rules, and EF Core via a value converter on `Value`.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuickStart;

public sealed class Order
{
    public Guid Id { get; set; }
    public OrderStatus Status { get; set; } = null!;
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(order => order.Status)
            .HasConversion(
                status => status.Value,
                value  => OrderStatus.Create(value))
            .IsRequired();
    }
}
```

Because `TryCreate` returns `Result<TSelf>`, member parsing also composes inside ROP pipelines:

```csharp
public static Result<Order> Submit(Guid id, string statusName) =>
    OrderStatus.TryCreate(statusName, fieldName: "status")
        .Map(status => new Order { Id = id, Status = status });
```

> [!WARNING]
> Persist `Value`, never `Ordinal`. `Ordinal` is reflection-derived from declaration order and silently changes when fields are reordered.

## Practical guidance

- **Default to field names; reach for `[EnumValue]` only when the wire name must differ.** Two names per member is one too many to keep in sync.
- **Keep constructors `private`.** The point of `RequiredEnum<TSelf>` is that the declared set is the entire set.
- **Use `TryCreate` at boundaries, `Create` for trusted constants.** `Create` throws — pair it with literal strings, not user input.
- **Prefer `Is` / `IsNot` over `switch` chains for membership checks.** They read like the domain language.
- **Model state-transition rules on the type itself**, not in the caller — that is the point of moving from `enum` to value object.
- **Persist `Value`. Index `Value`. Never persist `Ordinal`.**
- **For hot membership checks against large sets, cache a `HashSet<TSelf>` once** instead of re-allocating a `params` array on every call.

## Cross-references

- API surface and source-generated members: [`trellis-api-core.md` → `RequiredEnum<TSelf>`](../api_reference/trellis-api-core.md#requiredenumtself)
- Concrete primitives derived from `Required*<TSelf>` bases: [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md)
- Analyzer / generator diagnostics: [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md) (in particular `TRLS017`, `TRLS031`)
- JSON converter: [`trellis-api-core.md` → `RequiredEnumJsonConverter<TRequiredEnum>`](../api_reference/trellis-api-core.md#requiredenumjsonconvertertrequiredenum)
- Companion article on scalar primitives: [`primitives.md`](primitives.md)
