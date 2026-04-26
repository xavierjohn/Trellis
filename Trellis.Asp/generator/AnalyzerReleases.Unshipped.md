; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID  | Category | Severity | Notes
---------|----------|----------|------------------------------------------------------------------------
TRLS039  | Trellis  | Warning  | ScalarValueJsonConverterGenerator: value object wraps a primitive that is not in the AOT-safe set, so no converter is generated and a custom JsonConverter must be supplied.
