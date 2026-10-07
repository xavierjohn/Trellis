# Trellis.Core

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)

Railway-oriented error handling for .NET with `Result<T>`, `Maybe<T>`, and typed errors.

## Installation
```bash
dotnet add package Trellis.Core
```

## Quick Example
```csharp
using Trellis;

Result<string> email = Result.Ok("ada@example.com")
    .Ensure(value => value.Contains('@'),
        _ => Error.InvalidInput.ForField(ValidationCodes.StringEmail, "email", detail: "Email is invalid."))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features
- Compose success and failure paths with `Bind`, `Map`, `Tap`, and `Ensure`.
- Use static `Result.Ensure(condition, () => error)` guards to create errors only on failure; predicate and async-predicate overloads support the same lazy factories.
- Carry required non-null values with static `Result.EnsureNotNull(value, fieldName, detail)`; nullable-query `EnsureNotNullAsync` forms support Task and ValueTask receivers.
- Model optional data with `Maybe<T>` instead of `null`.
- Treat blank optional text as absent with `Maybe.OptionalNonBlank`; nonblank input reaches the factory unchanged, while `Maybe.Optional` remains null-only.
- Return typed errors that map cleanly to APIs, logs, and tests.
- Accumulate failures with `EnsureAll`, including lazy value-dependent error factories.
- Validate collections with `TraverseAll((item, index) => ...)` or sequential `TraverseAllAsync((item, index, ct) => ..., cancellationToken)`, retaining input positions while accumulating failures.
- Compose nested validation paths with `InputPointer.AppendProperty` and `AppendIndex`, preserving location and RFC 6901 escaping.
- Use `AsTask()` / `AsValueTask()` to return synchronous `Result` chains from async-shaped APIs.
- Build resource-aware HTTP errors tersely with `ResourceRef.For<TResource>(id)`.
- Define custom `Required*<TSelf>` value objects with source-generated parsing, JSON conversion, and tracing support.
- Persist staged state alongside a failure with `Result.FailAfterCommit<T>(error)` — opt-in for background-worker handlers that need a permanent-failure transition to commit even though the handler returns a failed result.
- Classify `Error` values into `Transient` / `Permanent` / `FailFast` retry buckets with `error.Classify()` / `error.IsTransient()` / `error.GetRetryAdvice()` — transport-neutral helpers for worker, consumer, and outbound-gateway retry loops.
- Validate pagination controls with `PageRequest`, encode typed continuation state with `ICursorCodec<TState>`, and return immutable `Page<T>` responses.

## Error factories

Core's `FaultCodes.DuplicateKey`, `ReferentialIntegrity`, `RetryAborted`, and `RetryExhausted`
provide frozen persistence conflict codes for clients and tests without an EF Core dependency.

Factories put `code` first and optional `detail` last:

```csharp
Error.Conflict.For<Order>("order.already-shipped", id: orderId);
Error.NotFound.For<Order>(id: orderId);
```

Required codes must be nonblank; `NotFound` and `Gone` still allow omission. See [factory signatures](../docs/docfx_project/api_reference/trellis-api-core.md#construction-and-case-scoped-factories) and [migration guidance](../MIGRATION_v3.md#code-first-error-factories) before changing positional string arguments.

## Required values

```csharp
string? name = "Ada";
int? quantity = 0;
Result<string> label = Result.EnsureNotNull(name, "name", "Name is required.")
    .Combine(Result.EnsureNotNull(quantity, "quantity", "Quantity is required."))
    .Map((name, quantity) => $"{name}:{quantity}");
```

Success carries the non-null reference or unwrapped struct, so `Map` needs no `!`;
`Combine` still accumulates missing-field failures. These guards are null-only:
blank strings and default scalar values succeed. Supply an `Error` or `Func<Error>`
for a custom failure. Field/detail overloads create required errors only on null.
For nullable queries, use `task.EnsureNotNullAsync(...)` and continue with async
composition; task faults and cancellation propagate.

For a nonblank string, use `name.EnsureNotNullOrWhiteSpace("name", "Name is required.")`.
Null, empty, and whitespace fail with `ValidationCodes.ValueNotEmpty`; valid strings
are not trimmed. The field/detail and `Func<Error>` overloads create errors only on
failure. Use a `static` factory for custom errors when no captured state is needed.

`Error.InvalidInput.Required(fieldName, detail)` names the standard
`ValidationCodes.ValueNotNull` error. Both it and `FieldViolation.Required` accept an
`InputPointer`, preserving its location. Use
`Result.EnsureNotNull(value, () => Error.InvalidInput.Required(pointer, detail))`
for a location-aware lazy guard. String field names may be null/empty to target the root;
guards reject malformed full pointers only when the value is missing.
Core's nullable `ToResult` APIs and universal no-argument lift are removed. Use
these guards for required values, `Result.Ok(value)` for deliberate success wrapping,
and `Maybe<T>.ToResult(error)` for ordinary absence becoming a failure.

## Typed pagination

```csharp
Result<PageRequest> request = PageRequest.TryCreate(cursor, limit);
var codec = CursorCodec.Composite<DateTimeOffset, Guid>();
Result<Maybe<(DateTimeOffset Primary, Guid Secondary)>> boundary =
    request.Bind(pageRequest => pageRequest.Decode(codec));
```

Only a missing cursor means the first page; empty/whitespace tokens fail. A missing
limit defaults to 50, zero/negative limits fail, and values above the default cap
of 100 clamp while preserving the requested limit. Pass
`policy: PageSizeLimitPolicy.Reject` to reject above-cap requests.
`PageSize.FromRequested` is a trusted throwing convenience, not a raw-input parser.

`Cursor`, `PageSize`, and `Page<T>` are sealed record classes, not defaultable
struct values. `PageBuilder.FromOverFetch(rows, size, last => codec.Encode(state))`
only assembles an already ordered/sought batch; its encoder runs on the last
retained item only when another row exists. For provider-owned continuation
tokens, construct `Page<T>` directly. `Page<T>.Map` preserves both cursors and limits.

Use `CursorCodec.Map` for named validated state/context or `Create` for an explicit
serializer/parser. Built-in codecs are versioned, round-trip-checked, limited to
1,024 encoded characters, and unsigned. Old unversioned tokens are rejected.
Storage queries, authorization, signing, and computed-search algorithms remain
application-owned.

## `Result<T>` is not directly JSON-serializable

`Result<T>` carries a default `[JsonConverter]` that throws `NotSupportedException` on direct `JsonSerializer.Serialize` / `Deserialize`. Returning a raw `Result<T>` from a controller would otherwise hand MVC a domain disposition whose public JSON surface is only status/error metadata such as `{"IsSuccess": true, "IsFailure": false, "Error": null}`; the success value stays private, and `Error.*` cases still get no HTTP status-code mapping (an `Error.NotFound` would render as 200 OK instead of 404). The throw fires at the first request with an actionable message. Fix paths:

- **HTTP** — call `.ToHttpResponse()` (Trellis.Asp). The returned `Microsoft.AspNetCore.Http.IResult` writes the body itself; the struct never reaches STJ.
- **Non-HTTP** — unwrap with `Match` / `TryGetValue` before serialization.
- **Genuinely need raw JSON** (logging, IPC) — register a converter (or a `JsonConverterFactory`) in `JsonSerializerOptions.Converters`; option-registered converters take precedence over the type's `[JsonConverter]` attribute. **The override must match the declared static type:** `JsonConverter<Result<T>>` covers only `Result<T>`-declared values; `IResult<T>`-declared values need `JsonConverter<IResult<T>>`; `IResult`-declared values need `JsonConverter<IResult>`. Use a `JsonConverterFactory` to cover multiple shapes at once.

## Optional: API guidance for coding assistants

Trellis packages include API references that assistants may not find in the NuGet cache.
[Trellis.AgentDocs](https://www.nuget.org/packages/Trellis.AgentDocs) (independent local tool, command `agentdocs`)
installs the Trellis references under Git-root `.agentdocs/` and points `AGENTS.md` at them, once you approve
`Trellis.Core` in `.agentdocs/policy.json` (it is the only Trellis package that publishes guidance). Trellis
works without this step; restore and build do not edit your instructions.

Core publishes the complete reference set using a private `Trellis.AgentDocs.Packaging`
build dependency. The helper does not become a dependency of consuming applications,
and feature packages do not publish duplicate guides.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-core.md)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Core\tests\Trellis.Core.Tests.csproj -c Release
```
