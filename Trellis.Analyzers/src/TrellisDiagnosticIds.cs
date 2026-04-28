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
/// IDs in the <c>TRLS001</c>–<c>TRLS021</c> range are emitted by the
/// <c>Trellis.Analyzers</c> assembly. IDs in the <c>TRLS031</c>–<c>TRLS039</c>
/// range are emitted by the bundled source generators
/// (<c>Trellis.Core.Generator</c>, <c>Trellis.EntityFrameworkCore.Generator</c>,
/// and <c>Trellis.AspSourceGenerator</c>).
/// Analyzer IDs were renumbered to be contiguous in v3-alpha; prior IDs
/// (former <c>TRLS006/008/009/010/011/012/014/015/016/017/018/019/020/021/022/024/029</c>)
/// are now <c>TRLS003</c>–<c>TRLS019</c>. Consumers suppressing by numeric ID should
/// prefer these constants rather than string literals.
/// </para>
/// </remarks>
public static class TrellisDiagnosticIds
{
    // ---- Analyzer IDs (Trellis.Analyzers) ----

    /// <summary>TRLS001 — <c>Result</c> return value is not handled.</summary>
    public const string ResultNotHandled = "TRLS001";

    /// <summary>TRLS002 — Use <c>Bind</c> instead of <c>Map</c> when lambda returns <c>Result</c>.</summary>
    public const string UseBindInsteadOfMap = "TRLS002";

    /// <summary>TRLS003 — Unsafe access to <c>Maybe.Value</c>.</summary>
    public const string UnsafeMaybeValueAccess = "TRLS003";

    /// <summary>TRLS004 — <c>Result</c> is double-wrapped.</summary>
    public const string ResultDoubleWrapping = "TRLS004";

    /// <summary>TRLS005 — Incorrect async <c>Result</c> usage.</summary>
    public const string AsyncResultMisuse = "TRLS005";

    // TRLS006 (UseSpecificErrorType / ErrorBaseClassAnalyzer) was removed in
    // v3-alpha. It detected `new Error(...)`, but `Error` is an abstract
    // record — the C# compiler blocks construction with CS0144 before the
    // analyzer ever runs, so the rule was redundant with the language.

    /// <summary>TRLS007 — <c>Maybe</c> is double-wrapped.</summary>
    public const string MaybeDoubleWrapping = "TRLS007";

    /// <summary>TRLS008 — Consider using <c>Result.Combine</c>.</summary>
    public const string UseResultCombine = "TRLS008";

    /// <summary>TRLS009 — Use async method variant for async lambda.</summary>
    public const string UseAsyncMethodVariant = "TRLS009";

    /// <summary>TRLS010 — Don't throw exceptions in <c>Result</c> chains.</summary>
    public const string ThrowInResultChain = "TRLS010";

    // TRLS011 (EmptyErrorMessage / EmptyErrorMessageAnalyzer) was removed in
    // v3-alpha. It hard-coded the v1 static factory names (Error.Validation,
    // Error.NotFound, Error.Unauthorized, Error.Forbidden, Error.Conflict,
    // Error.Unexpected) as its detection set. None of those factories exist
    // on the closed-ADT Error type (only UnprocessableContent.ForField/
    // ForRule survived), so the analyzer never matched a real call site.

    /// <summary>TRLS012 — Don't compare <c>Result</c> or <c>Maybe</c> to null.</summary>
    public const string ComparingToNull = "TRLS012";

    /// <summary>TRLS013 — Unsafe access to <c>Maybe.Value</c> in LINQ expression.</summary>
    public const string UnsafeMaybeValueInLinq = "TRLS013";

    /// <summary>TRLS014 — Combine chain exceeds maximum supported tuple size.</summary>
    public const string CombineChainTooLong = "TRLS014";

    /// <summary>TRLS015 — Use <c>SaveChangesResultAsync</c> instead of <c>SaveChangesAsync</c>.</summary>
    public const string UseSaveChangesResult = "TRLS015";

    /// <summary>TRLS016 — <c>HasIndex</c> references a <c>Maybe&lt;T&gt;</c> property.</summary>
    public const string HasIndexMaybeProperty = "TRLS016";

    /// <summary>TRLS017 — Wrong <c>[StringLength]</c> or <c>[Range]</c> attribute namespace.</summary>
    public const string WrongAttributeNamespace = "TRLS017";

    /// <summary>TRLS018 — <c>Result&lt;T&gt;</c> deconstruction reads value without success gate.</summary>
    public const string UnsafeResultDeconstruction = "TRLS018";

    /// <summary>TRLS019 — Avoid <c>default(Result)</c>, <c>default(Result&lt;T&gt;)</c>, and <c>default(Maybe&lt;T&gt;)</c>.</summary>
    public const string DefaultResultOrMaybe = "TRLS019";

    /// <summary>TRLS020 — Composite value object DTO property is missing <c>CompositeValueObjectJsonConverter&lt;T&gt;</c>.</summary>
    public const string CompositeValueObjectDtoMissingJsonConverter = "TRLS020";

    /// <summary>TRLS021 — EF configuration duplicates Trellis conventions for <c>Maybe&lt;T&gt;</c> or <c>[OwnedEntity]</c>.</summary>
    public const string RedundantEfConfiguration = "TRLS021";

    // ---- Generator IDs (Trellis.Core.Generator / Trellis.EntityFrameworkCore.Generator) ----
    // Renumbered from TRLSGEN### to TRLS###. Mapping:
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

    /// <summary>TRLS039 — Scalar value object wraps a primitive that is not supported by the AOT-safe JSON converter generator.</summary>
    public const string UnsupportedScalarValuePrimitiveForAotJson = "TRLS039";
}
