# Trellis.EntityFrameworkCore.Generator

[![NuGet](https://img.shields.io/nuget/v/Trellis.EntityFrameworkCore.Generator.svg)](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Generator)

**Roslyn source generator** for `Maybe<T>` partial properties in EF Core entity types. Automatically generates private nullable backing fields and property implementations.

## What It Does

Automatically generates backing field code for partial properties of type `Maybe<T>`:

```csharp
// You write this:
public partial class Customer
{
    public partial Maybe<PhoneNumber> Phone { get; set; }
    public partial Maybe<DateTime> SubmittedAt { get; set; }
}

// Generator creates:
// - Private nullable backing field (_phone, _submittedAt)
// - Property getter: returns Maybe.From(_phone) or Maybe<T>.None
// - Property setter: extracts value or sets null
```

## Installation

This package is included automatically when you install `Trellis.EntityFrameworkCore`:

```bash
dotnet add package Trellis.EntityFrameworkCore
```

**Note:** Both packages are required — the main package provides `MaybeConvention` and EF Core integration, this generator creates the property implementations.

## Generated Code

For each `partial Maybe<T>` property, the generator emits:

```csharp
// Auto-generated for: public partial Maybe<PhoneNumber> Phone { get; set; }
private PhoneNumber? _phone;
public partial Maybe<PhoneNumber> Phone
{
    get => _phone is not null ? Maybe.From(_phone) : Maybe<PhoneNumber>.None;
    set => _phone = value.HasValue ? value.Value : null;
}
```

### Naming Convention

| Property | Backing Field | Column Name |
|----------|---------------|-------------|
| `Phone` | `_phone` | `Phone` |
| `SubmittedAt` | `_submittedAt` | `SubmittedAt` |
| `AlternateEmail` | `_alternateEmail` | `AlternateEmail` |

## Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| TRLSGEN100 | Warning | `Maybe<T>` property should be declared `partial` |

## Requirements

- **.NET Standard 2.0** compatible (source generators must target netstandard2.0)
- **C# 13+** for partial property support
- Requires `Trellis.EntityFrameworkCore` package

## How It Works

1. Analyzes your code for partial properties of type `Maybe<T>`
2. Generates a private nullable backing field and getter/setter at compile-time
3. `MaybeConvention` in Trellis.EntityFrameworkCore discovers the backing field and maps it as optional in EF Core
4. No runtime reflection — all compile-time

## Source Code

This is a **source generator** — it runs at compile-time and generates C# code. The generated code is visible in:

- Visual Studio: Dependencies → Analyzers → Trellis.EntityFrameworkCore.Generator
- Rider: Generated Sources
