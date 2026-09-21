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
        Error.InvalidInput.ForField("email", ValidationCodes.StringEmail, "Email is invalid."))
    .Map(value => value.Trim().ToLowerInvariant());
```

## Key Features
- Compose success and failure paths with `Bind`, `Map`, `Tap`, and `Ensure`.
- Use static `Result.Ensure(condition, () => error)` guards to create errors only on failure; predicate and async-predicate overloads support the same lazy factories.
- Model optional data with `Maybe<T>` instead of `null`.
- Treat blank optional text as absent with `Maybe.OptionalNonBlank`; nonblank input reaches the factory unchanged, while `Maybe.Optional` remains null-only.
- Return typed errors that map cleanly to APIs, logs, and tests.
- Accumulate failures with `EnsureAll`, including lazy value-dependent error factories.
- Compose nested validation paths with `InputPointer.AppendProperty` and `AppendIndex`, preserving location and RFC 6901 escaping.
- Use `AsTask()` / `AsValueTask()` to return synchronous `Result` chains from async-shaped APIs.
- Build resource-aware HTTP errors tersely with `ResourceRef.For<TResource>(id)`.
- Define custom `Required*<TSelf>` value objects with source-generated parsing, JSON conversion, and tracing support.
- Persist staged state alongside a failure with `Result.FailAfterCommit<T>(error)` — opt-in for background-worker handlers that need a permanent-failure transition to commit even though the handler returns a failed result.
- Classify `Error` values into `Transient` / `Permanent` / `FailFast` retry buckets with `error.Classify()` / `error.IsTransient()` / `error.GetRetryAdvice()` — transport-neutral helpers for worker, consumer, and outbound-gateway retry loops.
- Validate pagination controls with `PageRequest`, encode typed continuation state with `ICursorCodec<TState>`, and return immutable `Page<T>` responses.

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
