; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID  | Category | Severity | Notes
---------|----------|----------|------------------------------------------------------------------------
TRLS035  | Trellis  | Warning  | MaybePartialPropertyGenerator: Maybe<T> property should be `partial` so the generator can emit the underlying field/getter/setter.
TRLS036  | Trellis  | Error    | OwnedEntityGenerator: type marked [OwnedEntity] should be `partial` so the generator can emit the EF parameterless constructor.
TRLS037  | Trellis  | Warning  | OwnedEntityGenerator: type marked [OwnedEntity] already declares a parameterless constructor; the generated one is suppressed.
TRLS038  | Trellis  | Error    | OwnedEntityGenerator: type marked [OwnedEntity] must inherit from ValueObject.
