# TRLS067 - Fresh GUID-derived reason code

- **Severity:** Warning
- **Category:** Trellis

## What it detects

Known fresh `System.Guid.NewGuid()` or `System.Guid.CreateVersion7(...)` provenance in a bound reason-code argument or property: error constructors/factories, violation reason codes, and FluentValidation's `WithErrorCode`.

Explicit and target-typed construction, named/reordered/multiline calls, supported string formatting, and safely traceable single-write string/GUID locals are covered. The rule resolves the receiving type and parameter; a resource ID, `FaultId`, unrelated `Code` property, or target-typed ID constructor is not a reason-code sink.

## Why it matters

Reason codes name failure categories. A fresh identifier makes every occurrence a new category, so wire consumers, dashboards, and telemetry cannot group the same failure. Keep the category stable and put the incident identifier in its own field.

## Bad examples

```csharp
Error.Unexpected failure = new(Code: Guid.NewGuid().ToString("N")); // TRLS067

var code = $"fault-{Guid.NewGuid():N}";
var anotherFailure = new Error.Unexpected(code);                  // TRLS067
```

## Good example

```csharp
Error.Unexpected failure = new(
    Code: FaultCodes.UnhandledException,
    FaultId: Guid.NewGuid().ToString("N"));
```

Use `FaultCodes.UnhandledException` only for that category. A domain-specific failure needs its own stable application code, not a guessed framework constant.

## Allowed dynamic codes and limitations

This is not a constant-only rule or a vocabulary-membership check. Configured or forwarded codes, field/property values, opaque application helpers, `Guid.Empty`, and parsed fixed GUIDs remain legal. TRLS064's literal-only vocabulary behavior is unchanged.

Provenance follows built-in conversions, parentheses, GUID/object/string `ToString`, `Convert.ToString`, `string.Concat`, interpolation, and string `+`. Concatenation inspects inline array/collection elements, compiler-expanded span parameters, and inline spreads without user-defined element conversions. Whole-object array stringification and nested array contents do not emit their GUID elements and remain silent. Known conditional/coalescing selectors exclude unchosen branches. Locals must have one write and no ref/address escape across their enclosing operation tree; mutable array/object locals are not followed. The analyzer does not infer opaque helper implementations or generated code. Silence is not proof that an unknown dynamic value is stable.

## Code fix available

No. Only the author knows the right category and where the identifier belongs.

## Configuration

```ini
dotnet_diagnostic.TRLS067.severity = error
```

For an intentional narrow exception, use a justified standard suppression:

```csharp
#pragma warning disable TRLS067 // Deliberate compatibility with this legacy producer.
var legacyFailure = new Error.Unexpected(Guid.NewGuid().ToString("N"));
#pragma warning restore TRLS067
```

See the [formal analyzer reference](../../api_reference/trellis-api-analyzers.md#perincidentreasoncodeanalyzer--trls067) for the complete sink and provenance boundaries.
