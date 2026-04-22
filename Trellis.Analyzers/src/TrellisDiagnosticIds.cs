namespace Trellis;

/// <summary>
/// Canonical string constants for every Trellis diagnostic ID emitted by the
/// analyzer assembly and the bundled source generators.
/// </summary>
/// <remarks>
/// <para>
/// Use these constants instead of magic strings for <c>[SuppressMessage]</c>
/// attributes and rule-set entries — for example:
/// </para>
/// <code>
/// [SuppressMessage("Trellis", TrellisDiagnosticIds.UnsafeMaybeValueAccess,
///     Justification = "guarded by HasValue check earlier in the pipeline")]
/// </code>
/// <para>
/// IDs in the <c>TRLS001</c>–<c>TRLS029</c> range are emitted by the
/// <c>Trellis.Analyzers</c> assembly. IDs in the <c>TRLS031</c>–<c>TRLS038</c>
/// range are emitted by the bundled source generators
/// (<c>Trellis.Core.Generator</c> and <c>Trellis.EntityFrameworkCore.Generator</c>).
/// Removed-rule tombstones live under
/// <c>docs/docfx_project/articles/analyzers/TRLS00X.md</c>.
/// </para>
/// </remarks>
public static class TrellisDiagnosticIds
{
    // ---- Analyzer IDs (Trellis.Analyzers) ----

    /// <summary>TRLS001 — <c>Result</c> return value is not handled.</summary>
    public const string ResultNotHandled = "TRLS001";

    /// <summary>TRLS002 — Use <c>Bind</c> instead of <c>Map</c> when lambda returns <c>Result</c>.</summary>
    public const string UseBindInsteadOfMap = "TRLS002";

    /// <summary>TRLS006 — Unsafe access to <c>Maybe.Value</c>.</summary>
    public const string UnsafeMaybeValueAccess = "TRLS006";

    /// <summary>TRLS008 — <c>Result</c> is double-wrapped.</summary>
    public const string ResultDoubleWrapping = "TRLS008";

    /// <summary>TRLS009 — Incorrect async <c>Result</c> usage.</summary>
    public const string AsyncResultMisuse = "TRLS009";

    /// <summary>TRLS010 — Use specific error type instead of base <c>Error</c> class.</summary>
    public const string UseSpecificErrorType = "TRLS010";

    /// <summary>TRLS011 — <c>Maybe</c> is double-wrapped.</summary>
    public const string MaybeDoubleWrapping = "TRLS011";

    /// <summary>TRLS012 — Consider using <c>Result.Combine</c>.</summary>
    public const string UseResultCombine = "TRLS012";

    /// <summary>TRLS014 — Use async method variant for async lambda.</summary>
    public const string UseAsyncMethodVariant = "TRLS014";

    /// <summary>TRLS015 — Don't throw exceptions in <c>Result</c> chains.</summary>
    public const string ThrowInResultChain = "TRLS015";

    /// <summary>TRLS016 — Error message should not be empty.</summary>
    public const string EmptyErrorMessage = "TRLS016";

    /// <summary>TRLS017 — Don't compare <c>Result</c> or <c>Maybe</c> to null.</summary>
    public const string ComparingToNull = "TRLS017";

    /// <summary>TRLS018 — Unsafe access to <c>Maybe.Value</c> in LINQ expression.</summary>
    public const string UnsafeMaybeValueInLinq = "TRLS018";

    /// <summary>TRLS019 — Combine chain exceeds maximum supported tuple size.</summary>
    public const string CombineChainTooLong = "TRLS019";

    /// <summary>TRLS020 — Use <c>SaveChangesResultAsync</c> instead of <c>SaveChangesAsync</c>.</summary>
    public const string UseSaveChangesResult = "TRLS020";

    /// <summary>TRLS021 — <c>HasIndex</c> references a <c>Maybe&lt;T&gt;</c> property.</summary>
    public const string HasIndexMaybeProperty = "TRLS021";

    /// <summary>TRLS022 — Wrong <c>[StringLength]</c> or <c>[Range]</c> attribute namespace.</summary>
    public const string WrongAttributeNamespace = "TRLS022";

    /// <summary>TRLS024 — <c>Result&lt;T&gt;</c> deconstruction reads value without success gate.</summary>
    public const string UnsafeResultDeconstruction = "TRLS024";

    /// <summary>TRLS029 — Avoid <c>default(Result)</c>, <c>default(Result&lt;T&gt;)</c>, and <c>default(Maybe&lt;T&gt;)</c>.</summary>
    public const string DefaultResultOrMaybe = "TRLS029";

    // ---- Generator IDs (Trellis.Core.Generator / Trellis.EntityFrameworkCore.Generator) ----
    // Renumbered from TRLSGEN### to TRLS### in v2 (see ADR-002 §3.5). Mapping:
    //   TRLSGEN001 → TRLS031   TRLSGEN002 → TRLS032
    //   TRLSGEN003 → TRLS033   TRLSGEN004 → TRLS034
    //   TRLSGEN100 → TRLS035   TRLSGEN101 → TRLS036
    //   TRLSGEN102 → TRLS037   TRLSGEN103 → TRLS038

    /// <summary>TRLS031 — Unsupported base type for <c>RequiredPartialClassGenerator</c>.</summary>
    public const string UnsupportedRequiredBaseType = "TRLS031";

    /// <summary>TRLS032 — <c>MinimumLength</c> exceeds <c>MaximumLength</c>.</summary>
    public const string InvalidStringLengthRange = "TRLS032";

    /// <summary>TRLS033 — <c>Range</c> minimum exceeds maximum (int / long / decimal).</summary>
    public const string InvalidRangeMinExceedsMax = "TRLS033";

    /// <summary>TRLS034 — Decimal range exceeds <c>decimal</c> bounds.</summary>
    public const string DecimalRangeExceedsDecimalRange = "TRLS034";

    /// <summary>TRLS035 — <c>Maybe&lt;T&gt;</c> property should be <c>partial</c>.</summary>
    public const string MaybePropertyShouldBePartial = "TRLS035";

    /// <summary>TRLS036 — <c>[OwnedEntity]</c> type should be <c>partial</c>.</summary>
    public const string OwnedEntityShouldBePartial = "TRLS036";

    /// <summary>TRLS037 — <c>[OwnedEntity]</c> type already has a parameterless constructor.</summary>
    public const string OwnedEntityAlreadyHasParameterlessCtor = "TRLS037";

    /// <summary>TRLS038 — <c>[OwnedEntity]</c> type must inherit from <c>ValueObject</c>.</summary>
    public const string OwnedEntityMustInheritValueObject = "TRLS038";
}
