# Trellis.Analyzers — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-efcore.md.

**NuGet:** `Trellis.Analyzers`

Roslyn analyzers and code fixes for correct `Result<T>`, `Maybe<T>`, and ROP pipeline usage.

| ID | Severity | Title |
|----|----------|-------|
| `TRLS001` | Warning | Result return value is not handled |
| `TRLS002` | Info | Use Bind instead of Map when lambda returns Result |
| `TRLS003` | Warning | Unsafe access to `Result.Value` without checking `IsSuccess` |
| `TRLS004` | Warning | Unsafe access to `Result.Error` without checking `IsFailure` |
| `TRLS005` | Info | Consider using MatchError for error type discrimination |
| `TRLS006` | Warning | Unsafe access to `Maybe.Value` without checking `HasValue` |
| `TRLS007` | Warning | Use `Create()` instead of `TryCreate().Value` |
| `TRLS008` | Warning | Result is double-wrapped as `Result<Result<T>>` |
| `TRLS009` | Warning | Blocking on `Task<Result<T>>` — use `await` |
| `TRLS010` | Info | Use specific error type instead of base `Error` class |
| `TRLS011` | Warning | Maybe is double-wrapped as `Maybe<Maybe<T>>` |
| `TRLS012` | Info | Consider using `Result.Combine` for multiple Result checks |
| `TRLS013` | Info | Consider `GetValueOrDefault` or `Match` instead of ternary |
| `TRLS014` | Warning | Use async method variant (`MapAsync`, `BindAsync`, etc.) for async lambda |
| `TRLS015` | Warning | Don't throw exceptions in Result chains — return failure |
| `TRLS016` | Warning | Error message should not be empty |
| `TRLS017` | Warning | Don't compare `Result` or `Maybe` to null (they are structs) |
| `TRLS018` | Warning | Unsafe access to `.Value` in LINQ without filtering by success state |
| `TRLS019` | Error | Combine chain exceeds maximum supported tuple size (9) |
| `TRLS020` | Warning | Use `SaveChangesResultAsync` instead of `SaveChangesAsync` |
| `TRLS021` | Warning | `HasIndex` references a `Maybe<T>` property — prefer `HasTrellisIndex` or use the backing field name |

Source generator diagnostics use a separate `TRLSGEN` prefix (see trellis-api-primitives.md and trellis-api-efcore.md).

---
