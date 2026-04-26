; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID  | Category | Severity | Notes
---------|----------|----------|------------------------------------------------------------------------
TRLS001  | Trellis  | Warning  | Result return value is not handled (must be observed via Match/Bind/Map/Tap/IsSuccess gate).
TRLS002  | Trellis  | Info     | Use Bind instead of Map when the lambda returns a Result<T>.
TRLS003  | Trellis  | Warning  | Unsafe access to Maybe.Value without a HasValue/TryGetValue guard.
TRLS004  | Trellis  | Warning  | Result is double-wrapped (Result<Result<T>>).
TRLS005  | Trellis  | Warning  | Incorrect async Result usage (e.g., awaiting Result<T> instead of using async extension).
TRLS006  | Trellis  | Info     | Use a specific Error subclass instead of the base Error class.
TRLS007  | Trellis  | Warning  | Maybe is double-wrapped (Maybe<Maybe<T>>).
TRLS008  | Trellis  | Info     | Consider using Result.Combine to aggregate multiple results.
TRLS009  | Trellis  | Warning  | Use the async method variant when the lambda is async.
TRLS010  | Trellis  | Warning  | Don't throw exceptions inside Result chains; convert to Error instead.
TRLS011  | Trellis  | Warning  | Error message should not be empty.
TRLS012  | Trellis  | Warning  | Don't compare Result or Maybe to null; use IsSuccess / HasValue.
TRLS013  | Trellis  | Warning  | Unsafe access to Maybe.Value inside a LINQ expression.
TRLS014  | Trellis  | Error    | Combine chain exceeds the maximum supported tuple size.
TRLS015  | Trellis  | Warning  | Use SaveChangesResultAsync instead of SaveChangesAsync inside a Result pipeline.
TRLS016  | Trellis  | Warning  | HasIndex references a Maybe<T> property; use the underlying field instead.
TRLS017  | Trellis  | Warning  | Wrong [StringLength] / [Range] attribute namespace (use Trellis attributes, not DataAnnotations).
TRLS018  | Trellis  | Warning  | Result<T> deconstruction reads value without a success gate.
TRLS019  | Trellis  | Warning  | Avoid default(Result), default(Result<T>), and default(Maybe<T>); prefer factories.
TRLS020  | Trellis  | Warning  | Composite value object DTO property is missing CompositeValueObjectJsonConverter<T>.
TRLS021  | Trellis  | Warning  | EF configuration duplicates Trellis conventions for Maybe<T> or [OwnedEntity].
