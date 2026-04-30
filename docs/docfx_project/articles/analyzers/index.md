# Trellis analyzer rules

Trellis analyzers help you keep `Result<T>`, `Maybe<T>`, EF Core integration, and Trellis value objects on the happy path. Every rule below uses the **Trellis** analyzer category.

## Rule index

| ID | Severity | Title | Article |
|---|---|---|---|
| TRLS001 | Warning | Result return value is not handled | [TRLS001](TRLS001.md) |
| TRLS002 | Info | Use Bind instead of Map when lambda returns Result | [TRLS002](TRLS002.md) |
| TRLS003 | Error | Unsafe access to Maybe.Value | [TRLS003](TRLS003.md) |
| TRLS004 | Warning | Result is double-wrapped | [TRLS004](TRLS004.md) |
| TRLS005 | Warning | Incorrect async Result usage | [TRLS005](TRLS005.md) |
| TRLS007 | Warning | Maybe is double-wrapped | [TRLS007](TRLS007.md) |
| TRLS008 | Info | Consider using Result.Combine | [TRLS008](TRLS008.md) |
| TRLS009 | Warning | Use async method variant for async lambda | [TRLS009](TRLS009.md) |
| TRLS010 | Warning | Don't throw exceptions in Result chains | [TRLS010](TRLS010.md) |
| TRLS013 | Warning | Unsafe access to Maybe.Value in LINQ expression | [TRLS013](TRLS013.md) |
| TRLS014 | Error | Combine chain exceeds maximum supported tuple size | [TRLS014](TRLS014.md) |
| TRLS015 | Warning | Use SaveChangesResultAsync instead of SaveChangesAsync | [TRLS015](TRLS015.md) |
| TRLS016 | Warning | HasIndex references a Maybe<T> property | [TRLS016](TRLS016.md) |
| TRLS017 | Warning | Wrong [StringLength] or [Range] attribute namespace | [TRLS017](TRLS017.md) |
| TRLS018 | Warning | Unsafe Result<T> deconstruction | [TRLS018](TRLS018.md) |
| TRLS019 | Warning | Avoid default(Result&lt;T&gt;) / default(Maybe&lt;T&gt;) | [TRLS019](TRLS019.md) |

## Code fixes at a glance

These rules currently offer a code fix: **TRLS002**, **TRLS003**, **TRLS009**, and **TRLS015**.

## Suppressing a rule

Use `.editorconfig` when you want a project-wide or folder-wide setting:

```ini
dotnet_diagnostic.TRLS003.severity = none
```

Use `#pragma` when you need a narrow, local exception:

```csharp
#pragma warning disable TRLS003
var name = maybeName.Value;
#pragma warning restore TRLS003
```

> [!TIP]
> Treat analyzer suppressions as documentation. If a rule is intentionally suppressed, leave a short reason nearby.

