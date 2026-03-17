# RequiredEnum

RequiredEnum is a type-safe enumeration with behavior, providing a powerful alternative to C# enums for domain modeling. When used with the `partial` keyword, the source generator automatically adds `IScalarValue<TSelf, string>`, JSON serialization, ASP.NET Core model binding, and `IParsable<T>`.

## Why RequiredEnum?

C# enums have limitations that can lead to bugs:

```csharp
// C# enum problems
public enum OrderStatus { Draft, Confirmed, Shipped }

var status = (OrderStatus)999;  // Valid! No compile or runtime error
var invalid = Enum.Parse<OrderStatus>("Invalid");  // Throws at runtime
```

RequiredEnum solves these issues:

```csharp
// RequiredEnum - type-safe, with behavior
public partial class OrderState : RequiredEnum<OrderState>
{
    public static readonly OrderState Draft = new();
    public static readonly OrderState Confirmed = new();
    public static readonly OrderState Shipped = new();
    
    private OrderState() { }
}

// Cannot create invalid values - Value is auto-derived from field name
var result = OrderState.TryCreate("Invalid");  // Returns Result.Failure with "Valid values: Draft, Confirmed, Shipped"
var state = OrderState.Draft;  // state.Value == "Draft"
```

## Basic Usage

### Defining a RequiredEnum

Use `partial` to enable source generation. The Value is automatically derived from the field name — pure domain, no strings needed:

```csharp
using Trellis;
using Trellis.Primitives;

public partial class PaymentMethod : RequiredEnum<PaymentMethod>
{
    // Value auto-derived: "CreditCard", "DebitCard", etc.
    public static readonly PaymentMethod CreditCard = new();
    public static readonly PaymentMethod DebitCard = new();
    public static readonly PaymentMethod BankTransfer = new();
    public static readonly PaymentMethod Crypto = new();

    private PaymentMethod() { }
}
```

### Creating Instances

```csharp
// From name (case-insensitive, returns Result<T>)
var result = PaymentMethod.TryCreate("creditcard");
if (result.IsSuccess)
    Console.WriteLine(result.Value.Value);  // "CreditCard"

// Direct access when known valid
var method = PaymentMethod.Create("CreditCard");  // Throws if invalid

// Check membership
if (payment.Is(PaymentMethod.CreditCard, PaymentMethod.DebitCard))
    ApplyCardFee();

if (payment.IsNot(PaymentMethod.Crypto))
    ProcessTraditionalPayment();

// Enumerate all values
foreach (var m in PaymentMethod.GetAll())
    Console.WriteLine($"{m.Ordinal}: {m.Value}");
```

## Adding Behavior

RequiredEnum types can have properties and methods:

```csharp
public partial class OrderState : RequiredEnum<OrderState>
{
    // Value auto-derived from field name
    public static readonly OrderState Draft = new(canModify: true, canCancel: true, isTerminal: false);
    public static readonly OrderState Confirmed = new(canModify: false, canCancel: true, isTerminal: false);
    public static readonly OrderState Shipped = new(canModify: false, canCancel: false, isTerminal: false);
    public static readonly OrderState Delivered = new(canModify: false, canCancel: false, isTerminal: true);
    public static readonly OrderState Cancelled = new(canModify: false, canCancel: false, isTerminal: true);

    public bool CanModify { get; }
    public bool CanCancel { get; }
    public bool IsTerminal { get; }

    private OrderState(bool canModify, bool canCancel, bool isTerminal)
    {
        CanModify = canModify;
        CanCancel = canCancel;
        IsTerminal = isTerminal;
    }
}

// Usage
if (order.State.CanModify)
    order.AddLine(product, quantity);

if (order.State.CanCancel)
    order.Cancel();
```

## State Machine Pattern

RequiredEnum types excel at modeling state machines with valid transitions:

```csharp
public partial class OrderState : RequiredEnum<OrderState>
{
    // ... members defined above ...

    public IReadOnlyList<OrderState> AllowedTransitions => this switch
    {
        _ when this == Draft => [Confirmed, Cancelled],
        _ when this == Confirmed => [Shipped, Cancelled],
        _ when this == Shipped => [Delivered],
        _ => []  // Terminal states have no transitions
    };

    public bool CanTransitionTo(OrderState newState) =>
        AllowedTransitions.Contains(newState);

    public Result<OrderState> TryTransitionTo(OrderState newState)
    {
        if (CanTransitionTo(newState))
            return newState;

        return Error.Validation(
            $"Cannot transition from '{this}' to '{newState}'. " +
            $"Allowed: {string.Join(", ", AllowedTransitions)}");
    }
}
```

## JSON Serialization

When using the `partial` keyword, the source generator automatically adds a `[JsonConverter]` attribute. No manual configuration needed:

```csharp
// Source generator adds [JsonConverter(typeof(RequiredEnumJsonConverter<OrderState>))]
public partial class OrderState : RequiredEnum<OrderState>
{
    // ...
}

// Serializes to: "Confirmed" (the Value)
var json = JsonSerializer.Serialize(OrderState.Confirmed);

// Deserializes from string (case-insensitive)
var state = JsonSerializer.Deserialize<OrderState>("\"Confirmed\"");
```

## Entity Framework Core

Store RequiredEnum types using the auto-generated Value property:

```csharp
// In DbContext.OnModelCreating
modelBuilder.Entity<Order>(builder =>
{
    // Store as string using the auto-generated Value ("Draft", "Confirmed", ...)
    builder.Property(o => o.State)
        .HasConversion(
            state => state.Value,
            value => OrderState.GetAll().First(s => s.Value == value))
        .IsRequired();
});
```

**Note:** `Ordinal` is assigned based on field declaration order (0, 1, 2, ...).
If you persist ordinals yourself instead of `Value`, reordering fields will change stored values.

## ASP.NET Core Integration

Because the source generator adds `IScalarValue<TSelf, string>`, RequiredEnum types work automatically with ASP.NET Core:

- **Route parameters:** `/orders/states/{state}` — binds and validates automatically
- **Query parameters:** `?state=Draft` — binds and validates automatically  
- **JSON body:** `{ "state": "Draft" }` — validates via `ValidatingJsonConverter`
- **Validation errors:** Returns rich error messages like `"Valid values: Draft, Confirmed, Shipped, Delivered, Cancelled"`

## API Reference

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Value` | `string` | Auto-derived from field name |
| `Ordinal` | `int` | Auto-generated based on declaration order (0, 1, 2, ...) |

### Static Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `GetAll()` | `IReadOnlyCollection<T>` | All defined members |
| `TryCreate(string)` | `Result<T>` | Find by name (case-insensitive) |
| `TryCreate(string?, string?)` | `Result<T>` | Find by name with field name for errors |
| `Create(string)` | `T` | Find by name, throws if invalid |

### Instance Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Is(params T[])` | `bool` | Check if instance is one of the specified values |
| `IsNot(params T[])` | `bool` | Check if instance is not one of the specified values |

### Operators

- Equality: `==`, `!=`

## Best Practices

1. **Use private constructor** - Prevent external instantiation
2. **Define members as static readonly** - Ensures single instances
3. **No strings in domain** - Value is auto-derived from field name
4. **Add behavior for domain logic** - Encapsulate rules in the enum
5. **Use TryCreate** - For user input validation
6. **Use Create** - For known-valid values (tests, constants)
7. **Model state machines** - When values have valid transitions
8. **Use Is() and IsNot()** - For readable membership checks

## See Also

- [Domain-Driven Design](intro.md#domain-driven-design) - Overview of DDD patterns
- [Entity Framework Core Integration](integration-ef.md) - Persistence patterns
- [Clean Architecture](clean-architecture.md) - Using RequiredEnum in layered applications
