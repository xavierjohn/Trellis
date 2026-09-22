# Trellis.Core

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)

Railway-oriented error handling and domain foundations for .NET with `Result<T>`, `Maybe<T>`, typed errors, value-object bases, and DDD primitives.

## Installation

```bash
dotnet add package Trellis.Core
```

## Quick Example

```csharp
using Trellis;

Result<string> email = Result.Ok("ada@example.com")
    .Ensure(
        value => value.Contains('@'),
        _ => Error.InvalidInput.ForField(
            ValidationCodes.StringEmail,
            "email",
            detail: "Email is invalid."))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features

- Compose explicit success and failure paths with `Bind`, `Map`, `Tap`, `Ensure`, `Combine`, and their async variants.
- Use static `Result.Ensure(condition, () => error)` guards to create errors only on failure; predicate and async-predicate overloads support the same lazy factories.
- Model expected absence with `Maybe<T>` instead of `null` or exceptions.
- Treat blank optional text as absent with `Maybe.OptionalNonBlank`; nonblank input reaches the factory unchanged, while `Maybe.Optional` remains null-only.
- Return a closed set of typed errors that adapters can map consistently.
- Accumulate failures with `EnsureAll`, including lazy value-dependent error factories.
- Validate collections with `TraverseAll((item, index) => ...)` or sequential `TraverseAllAsync((item, index, ct) => ..., cancellationToken)`, retaining input positions while accumulating failures.
- Compose nested validation paths with `InputPointer.AppendProperty` and `AppendIndex`, preserving location and RFC 6901 escaping.
- Build aggregates, entities, value objects, specifications, domain events, and integration-event contracts.
- Define source-generated `Required*<TSelf>` scalar value objects.
- Validate cursor pagination with `PageRequest`, `CursorCodec`, `Page<T>`, and `PageBuilder`.
- Classify failures for retry-aware workers and consumers.

`Result<T>` is deliberately not directly JSON-serializable. At an HTTP boundary, map it with `Trellis.Asp.ToHttpResponse()`; elsewhere, unwrap it through `Match` or `TryGetValue` before serialization.

Generated `Required*<TSelf>` types are lenient by default: use `[NotDefault]` to reject sentinel values and `[Trim]` to normalize strings.

## Error factories

Case-scoped factories put `code` first and optional `detail` last. Use `Error.Conflict.For<Order>("order.already-shipped", id: orderId)` for a resource conflict, or `Error.NotFound.For<Order>(id: orderId)` without inventing a reason code. `ForField(code, field, args: ..., detail: ...)` supports a property name or `InputPointer`; `ForRule(code, fields: ..., args: ..., detail: ...)` supports related fields.

Required codes reject null/empty/whitespace, including constructors and `with` assignments. Custom codes remain supported. `NotFound` and `Gone` retain optional codes and the unspecified sentinel. Explicit resources use `ResourceRef`. This is a breaking argument-order change: migrate positional string IDs and validation fields by meaning, not just until the code compiles.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-core.html)
- [Cross-package cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html)
- [Error-handling guide](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)
