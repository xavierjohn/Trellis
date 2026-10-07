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

Core's `FaultCodes.DuplicateKey`, `ReferentialIntegrity`, `RetryAborted`, and `RetryExhausted`
provide frozen persistence conflict codes for clients and tests without an EF Core dependency.

Case-scoped factories put `code` first and optional `detail` last. Use `Error.Conflict.For<Order>("order.already-shipped", id: orderId)` for a resource conflict, or `Error.NotFound.For<Order>(id: orderId)` without inventing a reason code. `ForField(code, field, args: ..., detail: ...)` supports a property name or `InputPointer`; `ForRule(code, fields: ..., args: ..., detail: ...)` supports related fields.

Required codes reject null/empty/whitespace, including constructors and `with` assignments. Custom codes remain supported. `NotFound` and `Gone` retain optional codes and the unspecified sentinel. Explicit resources use `ResourceRef`. This is a breaking argument-order change: migrate positional string IDs and validation fields by meaning, not just until the code compiles.

## Optional: API guidance for coding assistants

`Trellis.Core` includes the versioned API reference for the whole Trellis family (Core, ASP.NET Core,
Mediator, EF Core, Analyzers and the rest), but an AI coding assistant working in your repository will not
find those documents inside the NuGet cache. This optional setup installs them into Git-root `.agentdocs/`
and adds small pointers to `AGENTS.md` and `.github/copilot-instructions.md`, so assistants read the
routing guide instead of guessing API signatures. Confirm a project references a package before using its
API. **Trellis works normally without it.** Restore and build never create `.agentdocs/` or edit your
instructions; you choose whether to install the guidance.
`Trellis.AgentDocs` is independently versioned from Core; `0.1.0-preview.20` is available from NuGet.org.
Core's private `Trellis.AgentDocs.Packaging` build dependency publishes the complete reference
set; it is not a dependency of your application, and feature packages do not duplicate the guides.

To install it at the Git root, run these **from the Git root** (use a relative path
if the solution is in a subdirectory):

```bash
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.20 --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run agentdocs init <solution-or-project>
```

If `.config/dotnet-tools.json` already exists, skip the `dotnet new` step and reuse it. Restoring a package
never activates its guide: `init` lists `Trellis.Core` as pending and prints the package IDs to add to
`approvedPackages` in `.agentdocs/policy.json`. Approve `Trellis.Core`, then run
`dotnet tool run agentdocs sync`. Only `Trellis.Core` publishes guidance, so that one approval covers every
first-party package that depends on `Trellis.Core`. A project that references only `Trellis.Analyzers` has no
`Trellis.Core` in its graph and receives no guidance, and packages published from other repositories
(for example `Trellis.ServiceLevelIndicators`) ship their own reference.

The generated index makes one document required reading, `trellis-start-here.md` (about 23 KB): it routes
each task to the reference or recipe to open. Every other reference is listed in the index as on demand,
with a one-line description of when to open it, so an agent can pick the right one without reading them all.
After a Trellis upgrade, run `dotnet restore` and then `dotnet tool run agentdocs sync`. CI can run
`dotnet tool run agentdocs check` to detect drift without writes. Commit the tool pin, the managed
instruction pointers, `.agentdocs/policy.json` and `.agentdocs/` with the package change. `.agentdocs/` and
the pinned tool manifest live at the Git root even if the selected solution is nested; the installer adds a
pointer to the nested project's `AGENTS.md`, and Visual Studio Copilot uses the managed Git-root
`.github/copilot-instructions.md` pointer when custom instructions are enabled. The selected graph must
resolve each package to one version: `init` stops without writing context if versions conflict.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-core.html)
- [Cross-package cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html)
- [Error-handling guide](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
- [Pagination guide](https://xavierjohn.github.io/Trellis/articles/pagination.html)
