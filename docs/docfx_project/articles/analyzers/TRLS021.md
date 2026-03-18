# TRLS021: HasIndex references a Maybe\<T\> property

## Cause

A `HasIndex` lambda expression in an EF Core entity configuration references a property declared as `Maybe<T>`. This silently fails to create the index because `MaybeConvention` maps `Maybe<T>` through generated storage members, making the CLR property invisible to EF Core's index builder.

## Rule Description

When you use `Maybe<T>` for optional properties, Trellis provides `HasTrellisIndex` so you can keep indexing at the CLR-property level. Plain EF Core `HasIndex` does not understand the Trellis mapping strategy for `Maybe<T>`, so an index that mentions a `Maybe<T>` property will silently miss that column.

This rule fires as a **Warning** because the code compiles and runs without errors, but the database index simply won't exist at runtime.

## How to Fix Violations

Replace the lambda-based `HasIndex` with `HasTrellisIndex`. Use the string-based storage member name only as a fallback when you intentionally need to bypass the Trellis helper.

`HasTrellisIndex` only accepts direct property access on the lambda parameter. Expressions like `e => e.Customer.SubmittedAt` are rejected with `ArgumentException` instead of being interpreted as indexes on the root entity.

For `Maybe<T>` properties, `HasTrellisIndex` validates that the expected generated storage member exists on the entity CLR type hierarchy or is already mapped in the EF model. If that mapping is missing, it throws `InvalidOperationException` with guidance to use `partial` properties or explicit field mapping.

### Single property index

```csharp
// ❌ Bad - index silently not created
builder.HasIndex(e => e.SubmittedAt);

// ✅ Preferred - resolves Maybe<T> to the mapped storage member for you
builder.HasTrellisIndex(e => e.SubmittedAt);

// ✅ Fallback - uses the mapped storage member name directly
builder.HasIndex("_submittedAt");
```

### Composite index with Maybe\<T\>

```csharp
// ❌ Bad - SubmittedAt part of index silently ignored
builder.HasIndex(e => new { e.Status, e.SubmittedAt });

// ✅ Preferred - regular properties stay regular, Maybe<T> resolves automatically
builder.HasTrellisIndex(e => new { e.Status, e.SubmittedAt });

// ✅ Fallback - uses string-based overload with the mapped storage member
builder.HasIndex("Status", "_submittedAt");
```

### Composite index without Maybe\<T\>

```csharp
// ✅ Fine - no Maybe<T> properties, lambda works correctly
builder.HasIndex(e => new { e.Status, e.Name });
```

## Storage Naming Convention

If you must drop to the raw mapped member name, the generated storage field follows `_camelCase` naming from the property name:

| Property | Storage Member |
|----------|----------------|
| `SubmittedAt` | `_submittedAt` |
| `Phone` | `_phone` |
| `AlternateEmail` | `_alternateEmail` |

## Background

`Maybe<T>` is a `readonly struct`. EF Core cannot mark non-nullable struct properties as optional. Trellis compensates with generated storage members and conventions so your normal entry point can remain the CLR property and the Trellis helpers. See the [EF Core integration guide](../integration-ef.md) for full details.

## When to Suppress Warnings

Suppress this warning only if you intentionally don't want an index on the `Maybe<T>` property and the `HasIndex` includes other non-Maybe properties that you do want indexed:

```csharp
#pragma warning disable TRLS021
builder.HasIndex(e => new { e.Status, e.SubmittedAt }); // Only Status index matters
#pragma warning restore TRLS021
```

However, in this case it's clearer to remove the `Maybe<T>` property from the index expression entirely or switch to `HasTrellisIndex`.

## See Also

- [EF Core Integration](../integration-ef.md)
- [Maybe\<T\> with EF Core](../integration-ef.md)
- [MaybeConvention source](https://github.com/xavierjohn/Trellis)
