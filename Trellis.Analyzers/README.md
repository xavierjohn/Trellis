# Trellis.Analyzers

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Analyzers.svg)](https://www.nuget.org/packages/Trellis.Analyzers)

Roslyn analyzers that keep Trellis usage safe, idiomatic, and review-friendly.

## Requirements

Requires the **.NET SDK 10.0.300 or later** (Roslyn 5.6+). The analyzers are compiled against Roslyn 5.6; on an older host the compiler reports `CS9057` and skips them, so the `TRLS` diagnostics stop firing.

## Installation

The analyzers are **opt-in** and ship separately — installing `Trellis.Core` does not include them.

```bash
dotnet add package Trellis.Analyzers
```

Prevent the package from flowing transitively to consumers of your library (analyzers are a build-time-only concern):

```xml
<PackageReference Include="Trellis.Analyzers" Version="...">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

Tune any rule's severity per project via `.editorconfig`, e.g. `dotnet_diagnostic.TRLS001.severity = error`.

## Quick Example
```csharp
using Trellis;

Result<int> Parse(string text) => Result.Ok(text.Length);

// TRLS002 recommends Bind instead of Map when the lambda returns Result<T>.
var result = Parse("abc")
    .Bind(length => Result.Ok(length + 1));
```

## Key Features
- Flags unsafe `Maybe.Value` access as an error (TRLS003).
- Catches async misuse, double-wrapped results, and common ROP anti-patterns (TRLS001/004/005/007/009/010).
- Catches unsafe `Result<T>` deconstruction reads, `default(Result/Maybe)` literals, and `[OwnedEntity]` init-only properties (TRLS018, TRLS019, TRLS022).
- Flags composite value objects exposed through request/response DTOs without `CompositeValueObjectJsonConverter<T>` (TRLS020).
- Flags redundant EF Core `HasConversion`, `OwnsOne`, and `Ignore` mappings for Trellis convention-owned properties (TRLS021).
- Warns when a fresh GUID-derived value reaches an error/violation reason code or FluentValidation `WithErrorCode`, while allowing incident/resource IDs and other dynamic application codes (TRLS067).
- Includes fixes for Trellis-specific issues such as `SaveChangesAsync` vs. `SaveChangesResultAsync`.
- See the [API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-analyzers.html) for the full diagnostic list, including TRLS067 and the bundled generator diagnostics.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/analyzers/index.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-analyzers.md)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the analyzer tests from the repository root:

```powershell
dotnet test --project Trellis.Analyzers\tests\Trellis.Analyzers.Tests.csproj -c Release
```

Analyzer and code-fix test compilations use .NET 10 reference metadata (`ReferenceAssemblies.Net.Net100`) and C# 14, matching the SDK guard fixtures. Shared helpers and tests that construct their own Roslyn harnesses use the same defaults; other parse options are preserved.

`Net10ReferenceParityTests` compiles the same 18 reason-code guard source scenarios as `ReasonCodeGuardBuildTests`, verifies actual .NET 10 API bindings and Roslyn operation shapes, and checks exact diagnostic IDs, severities, and source spans. Two additional checks exercise the analyzer and code-fix helpers with .NET 10 APIs. These tests use real framework metadata, not framework API shims; the existing minimal Trellis stubs do not establish shipped-package compatibility.

`ReasonCodeGuardBuildTests` retains every original case and input. Fourteen expression rows plus the single-write-local and mutable-spread cases now run in process, sharing the parity tests' fresh-compilation and exact-diagnostic checks. The positive `Guid.CreateVersion7` and negative stable-`Code`/fresh-`FaultId` rows still use real SDK builds, including the unrelated `CustomerId` and TRLS019-isolation checks. Together with the path-scope, analyzer-loading, suppression, and configuration controls, this leaves 15 real SDK builds instead of 31. The independent configuration/reflection check and all 20 parity/helper checks remain.

Repository CI replaces the reason-code text grep with the semantic TRLS067 build guard. To run the same source guard locally, build its analyzer first:

```powershell
dotnet build Trellis.Analyzers\src\Trellis.Analyzers.csproj -c Release
dotnet build -c Release -p:TrellisEnableReasonCodeGuard=true
```

The guard matches `src` path segments relative to the repository root and promotes TRLS067 to an error; a clone's ancestor directory named `src` does not expand its scope. It leaves other rules unchanged in projects already using the analyzer suite; otherwise it suppresses only the newly loaded standalone rules, not bundled generators.

The prebuilt analyzer is expected at `Trellis.Analyzers\src\bin\$(Configuration)\netstandard2.0\Trellis.Analyzers.dll`, matching its required Roslyn analyzer target. A missing-analyzer failure names that exact output path, configuration, and framework. Missing configuration or disabled analyzer execution also fail explicitly. Normal builds and consumer opt-in are unchanged.
