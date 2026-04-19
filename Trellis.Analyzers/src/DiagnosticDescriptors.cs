namespace Trellis.Analyzers;

using Microsoft.CodeAnalysis;

/// <summary>
/// Diagnostic descriptors for Trellis analyzers.
/// </summary>
public static class DiagnosticDescriptors
{
    private const string Category = "Trellis";
    private const string HelpLinkBase = "https://xavierjohn.github.io/Trellis/analyzers/";

    /// <summary>
    /// TRLS001: Result return value is not handled.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultNotHandled = new(
        id: "TRLS001",
        title: "Result return value is not handled",
        messageFormat: "The Result returned by '{0}' is not handled. Error information may be lost.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result<T> return values should be handled to ensure errors are not silently ignored. " +
                     "Use Bind, Map, Match, or assign to a variable.",
        helpLinkUri: HelpLinkBase + "TRLS001");

    /// <summary>
    /// TRLS002: Use Bind instead of Map when the lambda returns a Result.
    /// </summary>
    public static readonly DiagnosticDescriptor UseBindInsteadOfMap = new(
        id: "TRLS002",
        title: "Use Bind instead of Map when lambda returns Result",
        messageFormat: "The lambda returns a Result<T>. Use Bind instead of Map to avoid Result<Result<T>>.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When the transformation function returns a Result<T>, use Bind (flatMap) instead of Map. " +
                     "Map will produce Result<Result<T>> which is likely not intended.",
        helpLinkUri: HelpLinkBase + "TRLS002");

    /// <summary>
    /// TRLS003: Accessing Result.Value without checking IsSuccess.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeResultValueAccess = new(
        id: "TRLS003",
        title: "Unsafe access to Result.Value",
        messageFormat: "Accessing Result.Value without checking IsSuccess may throw an exception",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result.Value throws an InvalidOperationException if the Result is in a failure state. " +
                     "Check IsSuccess first, use TryGetValue, or use pattern matching with Match/MatchError.",
        helpLinkUri: HelpLinkBase + "TRLS003");

    /// <summary>
    /// TRLS004: Accessing Result.Error without checking IsFailure.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeResultErrorAccess = new(
        id: "TRLS004",
        title: "Unsafe access to Result.Error",
        messageFormat: "Accessing Result.Error without checking IsFailure may throw an exception",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result.Error throws an InvalidOperationException if the Result is in a success state. " +
                     "Check IsFailure first, use TryGetError, or use pattern matching with Match/MatchError.",
        helpLinkUri: HelpLinkBase + "TRLS004");

    /// <summary>
    /// TRLS006: Accessing Maybe.Value without checking HasValue.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeMaybeValueAccess = new(
        id: "TRLS006",
        title: "Unsafe access to Maybe.Value",
        messageFormat: "Accessing Maybe.Value without checking HasValue may throw an exception",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Maybe.Value throws an InvalidOperationException if the Maybe has no value. " +
                     "Check HasValue first, use TryGetValue, GetValueOrDefault, or convert to Result with ToResult.",
        helpLinkUri: HelpLinkBase + "TRLS006");

    /// <summary>
    /// TRLS007: Using .Value on TryCreate result instead of Create method.
    /// </summary>
    public static readonly DiagnosticDescriptor UseCreateInsteadOfTryCreateValue = new(
        id: "TRLS007",
        title: "Use Create instead of TryCreate().Value",
        messageFormat: "Using TryCreate().Value is unclear. Use '{0}.Create(...)' when you expect the value to be valid, or handle the Result properly.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using TryCreate().Value is unclear and provides poor error messages when validation fails. " +
                     "Use Create() when you expect success - it throws InvalidOperationException with the validation error details included. " +
                     "TryCreate().Value throws the same exception type but with a generic message, losing the validation error information. " +
                     "Or properly handle the Result returned by TryCreate() to avoid exceptions entirely.",
        helpLinkUri: HelpLinkBase + "TRLS007");

    /// <summary>
    /// TRLS008: Result is double-wrapped as Result&lt;Result&lt;T&gt;&gt;.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultDoubleWrapping = new(
        id: "TRLS008",
        title: "Result is double-wrapped",
        messageFormat: "Result<Result<{0}>> detected. Use Bind instead of Map, or avoid wrapping an existing Result.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result should not be wrapped inside another Result. This creates Result<Result<T>> which is almost always unintended. " +
                     "If combining Results, use Bind instead of Map. If wrapping a value, ensure it's not already a Result.",
        helpLinkUri: HelpLinkBase + "TRLS008");

    /// <summary>
    /// TRLS009: Blocking on async Result or accessing properties incorrectly.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncResultMisuse = new(
        id: "TRLS009",
        title: "Incorrect async Result usage",
        messageFormat: "Use 'await' with Task<Result<{0}>> instead of blocking or accessing Task properties",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Task<Result<T>> should be awaited, not blocked with .Result or .Wait(). " +
                     "Blocking can cause deadlocks and prevents proper async execution. Use await instead.",
        helpLinkUri: HelpLinkBase + "TRLS009");

    /// <summary>
    /// TRLS010: Using Error base class directly instead of specific error types.
    /// </summary>
    public static readonly DiagnosticDescriptor UseSpecificErrorType = new(
        id: "TRLS010",
        title: "Use specific error type instead of base Error class",
        messageFormat: "Use Error.Validation(), Error.NotFound(), or other specific error types instead of instantiating Error directly",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Using specific error types (ValidationError, NotFoundError, etc.) enables type-safe error handling with MatchError. " +
                     "Avoid instantiating the base Error class directly.",
        helpLinkUri: HelpLinkBase + "TRLS010");

    /// <summary>
    /// TRLS011: Maybe is double-wrapped as Maybe&lt;Maybe&lt;T&gt;&gt;.
    /// </summary>
    public static readonly DiagnosticDescriptor MaybeDoubleWrapping = new(
        id: "TRLS011",
        title: "Maybe is double-wrapped",
        messageFormat: "Maybe<Maybe<{0}>> detected. Avoid wrapping an existing Maybe inside another Maybe.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Maybe should not be wrapped inside another Maybe. This creates Maybe<Maybe<T>> which is almost always unintended. " +
                     "Avoid using Map when the transformation function returns a Maybe, as this creates double wrapping. " +
                     "Consider converting to Result with ToResult() for better composability.",
        helpLinkUri: HelpLinkBase + "TRLS011");

    /// <summary>
    /// TRLS012: Consider using Result.Combine for multiple Result checks.
    /// </summary>
    public static readonly DiagnosticDescriptor UseResultCombine = new(
        id: "TRLS012",
        title: "Consider using Result.Combine",
        messageFormat: "Consider using Result.Combine() or .Combine() chaining for combining multiple Results instead of manual checks",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When combining multiple Result<T> values, Result.Combine() or .Combine() chaining provides a cleaner and more maintainable approach " +
                     "than manually checking IsSuccess on each result.",
        helpLinkUri: HelpLinkBase + "TRLS012");

    /// <summary>
    /// TRLS013: Consider using GetValueOrDefault or Match instead of ternary.
    /// </summary>
    public static readonly DiagnosticDescriptor UseFunctionalValueOrDefault = new(
        id: "TRLS013",
        title: "Consider using GetValueOrDefault or Match",
        messageFormat: "Consider using GetValueOrDefault() or Match() instead of ternary operator for Result value extraction",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The pattern 'result.IsSuccess ? result.Value : default' can be replaced with GetValueOrDefault() or Match() " +
                     "for more idiomatic and safer code.",
        helpLinkUri: HelpLinkBase + "TRLS013");

    /// <summary>
    /// TRLS014: Using async lambda with synchronous Map/Bind instead of async variant.
    /// </summary>
    public static readonly DiagnosticDescriptor UseAsyncMethodVariant = new(
        id: "TRLS014",
        title: "Use async method variant for async lambda",
        messageFormat: "Use '{0}' instead of '{1}' when the lambda is async",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When using an async lambda with Map, Bind, Tap, or Ensure, use the async variant (MapAsync, BindAsync, etc.) " +
                     "to properly handle the async operation. Using sync methods with async lambdas causes the Task to not be awaited.",
        helpLinkUri: HelpLinkBase + "TRLS014");

    /// <summary>
    /// TRLS015: Throwing exception inside Result chain instead of returning failure.
    /// </summary>
    public static readonly DiagnosticDescriptor ThrowInResultChain = new(
        id: "TRLS015",
        title: "Don't throw exceptions in Result chains",
        messageFormat: "Don't throw exceptions inside '{0}'. Return a failure Result instead to maintain Railway Oriented Programming semantics.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Throwing exceptions inside Bind, Map, Tap, or Ensure lambdas defeats the purpose of Railway Oriented Programming. " +
                     "Return Result.Fail<T>() to signal errors and keep the error on the failure track.",
        helpLinkUri: HelpLinkBase + "TRLS015");

    /// <summary>
    /// TRLS016: Empty or missing error message.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptyErrorMessage = new(
        id: "TRLS016",
        title: "Error message should not be empty",
        messageFormat: "Error message should not be empty. Provide a meaningful message for debugging.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Error messages should provide context for debugging and user feedback. " +
                     "Empty error messages make it difficult to diagnose issues.",
        helpLinkUri: HelpLinkBase + "TRLS016");

    /// <summary>
    /// TRLS017: Comparing Result or Maybe to null.
    /// </summary>
    public static readonly DiagnosticDescriptor ComparingToNull = new(
        id: "TRLS017",
        title: "Don't compare Result or Maybe to null",
        messageFormat: "Don't compare {0} to null. Use '{1}' instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result<T> and Maybe<T> are structs and cannot be null. " +
                     "Use IsSuccess/IsFailure for Result, or HasValue/HasNoValue for Maybe.",
        helpLinkUri: HelpLinkBase + "TRLS017");

    /// <summary>
    /// TRLS018: Using .Value in LINQ without checking success state.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeValueInLinq = new(
        id: "TRLS018",
        title: "Unsafe access to Value in LINQ expression",
        messageFormat: "Accessing '{0}' in LINQ without filtering by {1} first may throw exceptions",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When using LINQ on collections of Result<T> or Maybe<T>, filter by IsSuccess/HasValue first, " +
                     "or use methods like Select with Match to safely extract values.",
        helpLinkUri: HelpLinkBase + "TRLS018");

    /// <summary>
    /// TRLS019: Combine chain exceeds maximum supported tuple size.
    /// </summary>
    public static readonly DiagnosticDescriptor CombineChainTooLong = new(
        id: "TRLS019",
        title: "Combine chain exceeds maximum supported tuple size",
        messageFormat: "Combine chain produces a {0}-element tuple, but the maximum supported is 9. Group related validations into sub-objects.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Combine supports up to 9 elements. Downstream methods (Bind, Map, Tap, Match) also only support tuples up to 9 elements. " +
                     "Group related fields into intermediate value objects or sub-results, then combine those groups.",
        helpLinkUri: HelpLinkBase + "TRLS019");

    /// <summary>
    /// TRLS020: Use SaveChangesResultAsync instead of SaveChangesAsync.
    /// </summary>
    public static readonly DiagnosticDescriptor UseSaveChangesResult = new(
        id: "TRLS020",
        title: "Use SaveChangesResultAsync instead of SaveChangesAsync",
        messageFormat: "Use 'SaveChangesResultUnitAsync' or 'SaveChangesResultAsync' instead of '{0}'. Direct SaveChanges calls bypass the Result pipeline and turn database errors into unhandled exceptions.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct SaveChanges/SaveChangesAsync calls bypass the Result pipeline and turn database errors into unhandled exceptions. " +
                     "Use SaveChangesResultAsync (returns Result<int>) or SaveChangesResultUnitAsync (returns Result) instead.",
        helpLinkUri: HelpLinkBase + "TRLS020");

    /// <summary>
    /// TRLS021: HasIndex references a Maybe&lt;T&gt; property.
    /// </summary>
    public static readonly DiagnosticDescriptor HasIndexMaybeProperty = new(
        id: "TRLS021",
        title: "HasIndex references a Maybe<T> property",
        messageFormat: "'{0}' is a Maybe<T> property. Prefer HasTrellisIndex; use mapped storage member '{1}' only as a fallback.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "HasIndex with a Maybe<T> property silently fails to create the index because MaybeConvention maps " +
                 "Maybe<T> via generated storage members, so the CLR property is invisible to EF Core's index builder. " +
                 "Prefer HasTrellisIndex so regular properties stay strongly typed and Maybe<T> properties resolve to their mapped storage automatically. " +
                 "If needed, you can also use string-based HasIndex with the storage member name directly. " +
                 "Examples: builder.HasTrellisIndex(e => new { e.Status, e.SubmittedAt }); or builder.HasIndex(\"Status\", \"_submittedAt\").",
        helpLinkUri: HelpLinkBase + "TRLS021");

    /// <summary>
    /// TRLS022: Wrong attribute namespace — System.ComponentModel.DataAnnotations instead of Trellis.
    /// </summary>
    public static readonly DiagnosticDescriptor WrongAttributeNamespace = new(
        id: "TRLS022",
        title: "Wrong [StringLength] or [Range] attribute namespace",
        messageFormat: "'{0}' uses System.ComponentModel.DataAnnotations.{1} which the Trellis source generator ignores. Use Trellis.{1} instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Trellis [StringLength] and [Range] attributes share names with System.ComponentModel.DataAnnotations versions. " +
                 "Using the wrong namespace compiles silently but the Trellis source generator ignores them, " +
                 "resulting in value objects without the expected validation constraints. " +
                 "Use the Trellis versions (namespace Trellis) instead.",
        helpLinkUri: HelpLinkBase + "TRLS022");

}