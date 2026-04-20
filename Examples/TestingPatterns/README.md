# TestingPatterns

xUnit test patterns commonly used with Trellis. This is **not** a sample of a
runtime application — it is a sample of *how to test* code that uses Trellis.

(Renamed from `Examples/Xunit/` so the folder name describes intent, not framework.)

## What this teaches

| File | Pattern |
|---|---|
| `AsyncUsageExamples.cs` | Async patterns with `Result<T>` / `Task<Result<T>>` |
| `MaybeExamples.cs` | `Maybe<T>` test patterns and equality |
| `ParallelExamples.cs` | xUnit v3 parallel collection setup |
| `MULTI_STAGE_PARALLEL_EXAMPLES.md` | Long-form notes on multi-stage parallel test design |
| `DomainDrivenDesignSamplesTests.cs` | Aggregate / event / repository test patterns |
| `FluentValidationSamplesTests.cs` | `Trellis.FluentValidation` adapter test patterns |
| `ValidationExample.cs` | End-to-end validation test |
| `TraceFixture.cs` | OpenTelemetry trace assertion fixture |
| `ValueObject/FirstName.cs`, `LastName.cs` | Test-local VOs used by the examples above |

## Run it

```pwsh
dotnet test Examples/TestingPatterns/TestingPatterns.Tests.csproj
```

## Why this lives in `Examples/`

The patterns here are the ones the framework expects consumers to use. They
double as compile-checked documentation: if a Trellis API changes, these
tests break and the docs get fixed at the same time.

## Related Docs

- [Examples README](../README.md) — 11-axiom contract every sample is held to.
- [Trellis.Testing](https://xavierjohn.github.io/Trellis/articles/testing.html)
