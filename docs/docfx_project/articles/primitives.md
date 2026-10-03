---
title: Value Objects
package: Trellis.Core, Trellis.Primitives
topics: [value-object, primitive-obsession, validation, ddd]
last_verified: 2026-10-02
audience: [developer]
---
# Model domain values with value objects

A `string` can be an email address, a product name, or a currency code. A `Guid` can identify any record in the system. Value objects replace those ambiguous primitives with types that carry one meaning and validate it once.

Trellis gives you two starting points:

- `Trellis.Core` generates domain-specific scalar types such as `CustomerName`, `OrderId`, and `Quantity`.
- `Trellis.Primitives` provides common types such as [EmailAddress](xref:Trellis.Primitives.EmailAddress), [Money](xref:Trellis.Primitives.Money), and [GeoCoordinate](xref:Trellis.Primitives.GeoCoordinate).

## Choose the shape first

| Your value | Start with |
|---|---|
| A common email, URL, phone number, country code, amount, or percentage | [Built-in value objects](built-in-primitives.md) |
| One CLR value with a domain-specific name or rule | [Create a custom scalar value](custom-primitives.md) |
| A finite set of named values with behavior | [Model symbolic values with `RequiredEnum`](required-enum.md) |
| Several fields that form one value | [ValueObject](xref:Trellis.ValueObject), or a structured built-in such as [Money](money.md) |
| A value that may be absent | [`Maybe<T>`](maybe-type.md), wrapped around the value object |

This choice is about meaning, not storage. An `OrderId` may still occupy one `uniqueidentifier` column, but the compiler no longer lets you pass a `CustomerId` by mistake.

## Run your first value-object pipeline

Create a console application and install `Trellis.Primitives`. The package brings in `Trellis.Core` and its source generator transitively.

```bash
dotnet new console -n ValueObjectDemo
cd ValueObjectDemo
dotnet add package Trellis.Primitives
```

Replace `Program.cs` with:

```csharp
using Trellis;
using Trellis.Primitives;

var registration = Registration.TryCreate(
    email: "ada@example.com",
    name: "  Ada Lovelace  ");

if (!registration.TryGetValue(out var customer, out var error))
{
    Console.Error.WriteLine(error);
    return;
}

Console.WriteLine($"{customer.Name.Value} <{customer.Email.Value}>");

[Trim, NotDefault, StringLength(100, MinimumLength = 2)]
public sealed partial class CustomerName : RequiredString<CustomerName>;

public sealed record Registration(EmailAddress Email, CustomerName Name)
{
    public static Result<Registration> TryCreate(string? email, string? name) =>
        Result.Combine(
                EmailAddress.TryCreate(email, nameof(email)),
                CustomerName.TryCreate(name, nameof(name)))
            .Map((validEmail, validName) => new Registration(validEmail, validName));
}
```

Run it:

```bash
dotnet run
```

The important work happens before `Registration` exists:

1. [EmailAddress.TryCreate](xref:Trellis.Primitives.EmailAddress.TryCreate(System.String,System.String)) validates the built-in value.
2. The generated `CustomerName.TryCreate` trims and validates the custom value.
3. [Result.Combine](xref:Trellis.CombineExtensions) keeps both field failures instead of stopping at the first one.
4. `Map` constructs the record only after both values are valid.

The `partial` keyword is required. The source generator supplies `Value`, `TryCreate`, `Create`, parsing, equality, and JSON conversion for [RequiredString<TSelf>](xref:Trellis.RequiredString`1); your declaration supplies only the domain name and rules.

## Put validation at the boundary

Use `TryCreate` for HTTP input, messages, files, and other untrusted data. It returns [Result<T>](xref:Trellis.Result`1), so validation remains on the normal control-flow path.

Use `Create` only for trusted constants and test setup, where an invalid value is a programming error. For generated identifiers, [RequiredGuid<TSelf>](xref:Trellis.RequiredGuid`1) also supplies `NewUniqueV7()`.

After the boundary, pass value-object-shaped commands and domain methods inward. That removes repeated string checks from handlers and makes invalid combinations harder to represent.

## Continue by task

- [Create custom scalar value objects](custom-primitives.md) for IDs, names, quantities, flags, and timestamps.
- [Browse the built-in value objects](built-in-primitives.md) before creating another email, URL, phone, or code type.
- [Work with money and percentages](money.md) when calculations must preserve validation failures.
- [Measure distance and build geographic bounds](geographic-values.md).
- [Model weekly local-time availability](weekly-schedules.md).
- [Connect value objects to ASP.NET Core](integration-aspnet.md) or [Entity Framework Core](integration-ef.md).

For complete signatures, follow the generated .NET API links in each guide or browse the [Trellis API catalog](../api/index.md).
