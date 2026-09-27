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

## Optional: API guidance for coding assistants

Trellis packages include API reference documents, but an AI coding assistant working in
your repository may not find those documents inside the NuGet cache. This optional setup
copies the references for your restored package versions into `.trellis/` and adds a small
pointer to `AGENTS.md` so assistants know where to start instead of guessing API
signatures. Core includes references for optional Trellis packages too; confirm a
project references a package before using its API. **Trellis works normally without it.**
Restore/build do not create `.trellis/` or edit your instructions; you choose whether to
install the guidance.

To install it at the Git root, run these **from the Git root** (use a relative path
if the solution is in a subdirectory):

```bash
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version __TRELLIS_PACKAGE_VERSION__ --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project>
```

The command requires `.config/dotnet-tools.json` at the selected scope; a
`dotnet-tools.json` created at the Git root by plain `dotnet new tool-manifest` will not
work. Move that existing manifest into `.config/` rather than creating a second one.
If `.config/dotnet-tools.json` already exists, skip the `dotnet new` step. Commit
the tool pin, `AGENTS.md`, and `.trellis/` with the package change. After cloning, run
`dotnet tool restore`, `dotnet restore <solution-or-project>`, then
`dotnet tool run trellis agent sync`. CI can run `dotnet tool run trellis agent check` to
detect drift without writes. The default `.trellis/` directory is created at the Git root.
For an independent nested scope, run the setup commands from that directory to create
its own `.config/dotnet-tools.json` with `"isRoot": true`, and pass `--scope .` to
`init`, `sync`, and `check`; `.trellis/` then lives in that directory. The selected graph
must have compatible restored Trellis packages: `init` stops without writing context
if older packages lack guidance manifests or versions conflict. Avoid external edits to
context files during mutating commands.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-core.html)
- [Cross-package cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html)
- [Error-handling guide](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)
