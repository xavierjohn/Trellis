namespace Trellis.Analyzers;

using System;
using Microsoft.CodeAnalysis;

/// <summary>
/// Diagnostic descriptors for Trellis analyzers.
/// </summary>
public static class DiagnosticDescriptors
{
    private const string Category = "Trellis";
    private const string HelpLinkBase = "https://xavierjohn.github.io/Trellis/api_reference/trellis-api-analyzers.html";

    /// <summary>
    /// TRLS001: Result return value is not handled.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultNotHandled = new(
        id: TrellisDiagnosticIds.ResultNotHandled,
        title: "Result return value is not handled",
        messageFormat: "The Result returned by '{0}' is not handled. Error information may be lost.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result<T> return values should be handled to ensure errors are not silently ignored. " +
                     "Use Bind, Map, Match, or assign to a variable.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS002: Use Bind instead of Map when the lambda returns a Result.
    /// </summary>
    public static readonly DiagnosticDescriptor UseBindInsteadOfMap = new(
        id: TrellisDiagnosticIds.UseBindInsteadOfMap,
        title: "Use Bind instead of Map when lambda returns Result",
        messageFormat: "The lambda returns a Result<T>. Use Bind instead of Map to avoid Result<Result<T>>.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When the transformation function returns a Result<T>, use Bind (flatMap) instead of Map. " +
                     "Map will produce Result<Result<T>> which is likely not intended.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS003: Accessing Maybe.Value without checking HasValue.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeMaybeValueAccess = new(
        id: TrellisDiagnosticIds.UnsafeMaybeValueAccess,
        title: "Unsafe access to Maybe.Value",
        messageFormat: "Accessing Maybe.Value without checking HasValue may throw an exception",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Maybe.Value throws an InvalidOperationException if the Maybe has no value. " +
                      "Check HasValue first, use TryGetValue, GetValueOrDefault, or convert to Result with ToResult.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS004: Result is double-wrapped as Result&lt;Result&lt;T&gt;&gt;.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultDoubleWrapping = new(
        id: TrellisDiagnosticIds.ResultDoubleWrapping,
        title: "Result is double-wrapped",
        messageFormat: "Result<Result<{0}>> detected. Use Bind instead of Map, or avoid wrapping an existing Result.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Result should not be wrapped inside another Result. This creates Result<Result<T>> which is almost always unintended. " +
                     "If combining Results, use Bind instead of Map. If wrapping a value, ensure it's not already a Result.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS005: Blocking on async Result or accessing properties incorrectly.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncResultMisuse = new(
        id: TrellisDiagnosticIds.AsyncResultMisuse,
        title: "Incorrect async Result usage",
        messageFormat: "Use 'await' with Task<Result<{0}>> instead of blocking or accessing Task properties",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Task<Result<T>> should be awaited, not blocked with .Result or .Wait(). " +
                     "Blocking can cause deadlocks and prevents proper async execution. Use await instead.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS007: Maybe is double-wrapped as Maybe&lt;Maybe&lt;T&gt;&gt;.
    /// </summary>
    public static readonly DiagnosticDescriptor MaybeDoubleWrapping = new(
        id: TrellisDiagnosticIds.MaybeDoubleWrapping,
        title: "Maybe is double-wrapped",
        messageFormat: "Maybe<Maybe<{0}>> detected. Avoid wrapping an existing Maybe inside another Maybe.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Maybe should not be wrapped inside another Maybe. This creates Maybe<Maybe<T>> which is almost always unintended. " +
                     "Avoid using Map when the transformation function returns a Maybe, as this creates double wrapping. " +
                     "Consider converting to Result with ToResult() for better composability.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS008: Consider using Result.Combine for multiple Result checks.
    /// </summary>
    public static readonly DiagnosticDescriptor UseResultCombine = new(
        id: TrellisDiagnosticIds.UseResultCombine,
        title: "Consider using Result.Combine",
        messageFormat: "Consider using Result.Combine() or .Combine() chaining for combining multiple Results instead of manual checks",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When combining multiple Result<T> values, Result.Combine() or .Combine() chaining provides a cleaner and more maintainable approach " +
                     "than manually checking IsSuccess on each result.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS009: Using async lambda with synchronous Map/Bind instead of async variant.
    /// </summary>
    public static readonly DiagnosticDescriptor UseAsyncMethodVariant = new(
        id: TrellisDiagnosticIds.UseAsyncMethodVariant,
        title: "Use async method variant for async lambda",
        messageFormat: "Use '{0}' instead of '{1}' for async work. Convert the enclosing method or lambda to an awaitable async flow manually if no code fix is offered.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When using async work with Map, Bind, Tap, or Ensure, use the async variant (MapAsync, BindAsync, etc.) " +
                     "and await the returned async pipeline. The code fix is only offered for simple, locally safe await insertions; " +
                     "convert synchronous void, value-returning, chained, nested, or delegate-changing scopes manually before applying it.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS010: Throwing exception inside Result chain instead of returning failure.
    /// </summary>
    public static readonly DiagnosticDescriptor ThrowInResultChain = new(
        id: TrellisDiagnosticIds.ThrowInResultChain,
        title: "Don't throw exceptions in Result chains",
        messageFormat: "Don't throw exceptions inside '{0}'. Return a failure Result instead to maintain Railway Oriented Programming semantics.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Throwing exceptions inside Bind, Map, Tap, or Ensure lambdas defeats the purpose of Railway Oriented Programming. " +
                     "Return Result.Fail<T>() to signal errors and keep the error on the failure track.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS013: Using <c>Maybe&lt;T&gt;.Value</c> in LINQ without checking HasValue.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeMaybeValueInLinq = new(
        id: TrellisDiagnosticIds.UnsafeMaybeValueInLinq,
        title: "Unsafe access to Maybe.Value in LINQ projection",
        messageFormat: "Accessing '{0}' in a LINQ projection without filtering by {1} first may throw at runtime. " +
                       "Filter via .Where(x => x.HasValue) before the projection (or use .Match(...)) to clear this warning. " +
                       "For EF Core IQueryable, prefer composing the query with " +
                       "Trellis.EntityFrameworkCore.MaybeQueryableExtensions: " +
                       "predicate helpers WhereHasValue / WhereNone / WhereEquals / " +
                       "WhereLessThan / WhereLessThanOrEqual / WhereGreaterThan / WhereGreaterThanOrEqual, " +
                       "and ordering helpers OrderByMaybe / OrderByMaybeDescending / ThenByMaybe / ThenByMaybeDescending, " +
                       "so .Value never appears in the chain.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct .Value access on Maybe<T> inside Select-family LINQ projections " +
                     "(Select/SelectMany/OrderBy/OrderByDescending/ThenBy/ThenByDescending/GroupBy/ToDictionary/ToLookup) " +
                     "throws InvalidOperationException for None elements unless an earlier .Where(...) " +
                     "lambda whose body mentions HasValue suppresses the diagnostic. " +
                     "Suppression is keyword-presence based; predicate-shape verification (e.g. distinguishing " +
                     ".Where(x => x.HasValue) from .Where(x => !x.HasValue)) is a known limitation. " +
                     "For EF Core IQueryable predicates over a Maybe<T> property, either " +
                     "register Trellis.EntityFrameworkCore.DbContextOptionsBuilderExtensions.AddTrellisInterceptors() " +
                     "(which rewrites .HasValue/.Value/GetValueOrDefault into EF.Property/null-checks/COALESCE), " +
                     "or use Trellis.EntityFrameworkCore.MaybeQueryableExtensions " +
                     "(WhereHasValue/WhereNone/WhereEquals/WhereLessThan/WhereLessThanOrEqual/WhereGreaterThan/WhereGreaterThanOrEqual) " +
                     "explicitly.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS013: Backwards-compatible alias for <see cref="UnsafeMaybeValueInLinq"/>.
    /// Retained because earlier versions of <c>Trellis.Analyzers</c> exposed this descriptor as
    /// <c>UnsafeValueInLinq</c>, which drifted from the matching <c>TrellisDiagnosticIds</c> constant
    /// (<see cref="TrellisDiagnosticIds.UnsafeMaybeValueInLinq"/>). Prefer the new field name in
    /// new code; this alias keeps existing custom analyzers and tests compiling.
    /// </summary>
    [Obsolete("Use UnsafeMaybeValueInLinq instead. Both names refer to the same DiagnosticDescriptor.")]
    public static readonly DiagnosticDescriptor UnsafeValueInLinq = UnsafeMaybeValueInLinq;

    /// <summary>
    /// TRLS014: Combine chain exceeds maximum supported tuple size.
    /// </summary>
    public static readonly DiagnosticDescriptor CombineChainTooLong = new(
        id: TrellisDiagnosticIds.CombineChainTooLong,
        title: "Combine chain exceeds maximum supported tuple size",
        messageFormat: "Combine chain produces a {0}-element tuple, but the maximum supported is 9. Group related validations into sub-objects.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Combine supports up to 9 elements. Downstream methods (Bind, Map, Tap, Match) also only support tuples up to 9 elements. " +
                     "Group related fields into intermediate value objects or sub-results, then combine those groups.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS015: Use SaveChangesResultAsync instead of SaveChangesAsync.
    /// </summary>
    public static readonly DiagnosticDescriptor UseSaveChangesResult = new(
        id: TrellisDiagnosticIds.UseSaveChangesResult,
        title: "Use SaveChangesResultAsync instead of SaveChangesAsync",
        messageFormat: "Use 'SaveChangesResultUnitAsync' or 'SaveChangesResultAsync' instead of '{0}' in non-UoW contexts. " +
                       "Direct SaveChanges/SaveChangesAsync calls bypass the Result pipeline and turn database errors into unhandled exceptions. " +
                       "Note: under AddTrellisUnitOfWork<TContext> the pipeline owns commit — repositories should stage changes only and not call SaveChanges/SaveChangesAsync at all.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct SaveChanges/SaveChangesAsync calls bypass the Result pipeline and turn database errors into unhandled exceptions. " +
                     "In non-UoW contexts, use SaveChangesResultAsync (returns Result<int>) or SaveChangesResultUnitAsync (returns Result<Unit>). " +
                     "Under AddTrellisUnitOfWork<TContext> the TransactionalCommandBehavior owns commit; repositories should stage changes via " +
                     "DbContext APIs (Add/Update/Remove) and not invoke SaveChanges/SaveChangesAsync at all.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS016: HasIndex references a Maybe&lt;T&gt; property.
    /// </summary>
    public static readonly DiagnosticDescriptor HasIndexMaybeProperty = new(
        id: TrellisDiagnosticIds.HasIndexMaybeProperty,
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
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS018: Result&lt;T&gt; deconstruction reads the value slot without a success/error gate.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsafeResultDeconstruction = new(
        id: TrellisDiagnosticIds.UnsafeResultDeconstruction,
        title: "Result<T> deconstruction reads value without success gate",
        messageFormat: "'{0}' is read from a Result<T> deconstruction without checking the success/error component first. The value slot is default(T) on failure and may silently propagate fake data.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Deconstructing Result<T> as 'var (_, value, _) = result;' (or any tuple form) returns default(T) when the result is a failure. " +
                     "For struct values like int/Guid this silently propagates a fake value rather than surfacing the error. " +
                     "Either capture the success/error component and gate the value read with an if-check, or use Match/IsSuccess/TryGetValue instead.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS019: Explicit default(Result), default(Result&lt;T&gt;), or default(Maybe&lt;T&gt;) at a use site.
    /// </summary>
    public static readonly DiagnosticDescriptor DefaultResultOrMaybe = new(
        id: TrellisDiagnosticIds.DefaultResultOrMaybe,
        title: "Avoid default(Result), default(Result<T>), and default(Maybe<T>)",
        messageFormat: "Explicit 'default' of '{0}' is a known footgun. Use {1} instead.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "default(Result) and default(Result<T>) are typed failures carrying " +
                     "the new Error.Unexpected(\"default-initialized\") sentinel — never a silent success. " +
                     "default(Maybe<T>) equals Maybe<T>.None but the explicit literal obscures intent. " +
                     "Always construct via Result.Ok(...)/Result.Fail(...) or Maybe<T>.None / Maybe.From(...). " +
                     "Suppress with [SuppressMessage(\"Trellis\", \"TRLS019\")] or '#pragma warning disable TRLS019' " +
                     "for sanctioned sentinel/test-helper sites.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS020: Composite value object DTO property is not safely deserializable.
    /// </summary>
    public static readonly DiagnosticDescriptor CompositeValueObjectDtoMissingJsonConverter = new(
        id: TrellisDiagnosticIds.CompositeValueObjectDtoMissingJsonConverter,
        title: "Composite value object DTO property is not safely deserializable",
        messageFormat: "Composite value object '{0}' is exposed by DTO property '{1}'. System.Text.Json may bypass TryCreate validation. For bare '{0}', add [JsonConverter(typeof(CompositeValueObjectJsonConverter<{0}>))] on the type; for Maybe<{0}>, use '{0}?' + Maybe.From(...) at the endpoint/API seam (cookbook Recipe 14).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Composite value objects exposed through request/response DTOs must use a supported transport. " +
                     "A bare composite property requires [JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))] on the type. " +
                     "Maybe<TComposite> on a DTO is never supported — Trellis ships no MaybeCompositeValueObjectJsonConverterFactory, " +
                     "and [JsonConverter] on the inner composite does not cover the Maybe<> wrapper. Use TComposite? plus Maybe.From(...) " +
                     "at the endpoint/API seam instead — applicable to controller actions, Minimal API handlers, and Mediator " +
                     "message-construction sites (cookbook Recipe 14). Otherwise System.Text.Json may fall back to default " +
                     "construction and silently bypass TryCreate validation.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS021: EF configuration duplicates Trellis conventions for Maybe&lt;T&gt; or [OwnedEntity].
    /// </summary>
    public static readonly DiagnosticDescriptor RedundantEfConfiguration = new(
        id: TrellisDiagnosticIds.RedundantEfConfiguration,
        title: "EF configuration duplicates Trellis conventions",
        messageFormat: "'{0}' manually configures '{1}', but ApplyTrellisConventions is already wired. Remove the redundant mapping and let Trellis conventions own Maybe<T> and [OwnedEntity] properties.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When ApplyTrellisConventions or ApplyTrellisConventionsFor<TContext>() is wired, manual HasConversion, OwnsOne, or Ignore configuration for Maybe<T> and [OwnedEntity] properties can override or conflict with Trellis EF conventions. " +
                     "Remove the redundant mapping so the convention-generated storage and ownership model stay authoritative.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS022: [OwnedEntity] property uses init-only setter; use { get; private set; } instead.
    /// </summary>
    public static readonly DiagnosticDescriptor OwnedEntityInitOnlyProperty = new(
        id: TrellisDiagnosticIds.OwnedEntityInitOnlyProperty,
        title: "[OwnedEntity] property uses init-only setter",
        messageFormat: "Property '{0}' on [OwnedEntity] type '{1}' uses an init-only setter. Use '{{ get; private set; }}' so EF Core can populate it during materialization through the generator-emitted parameterless constructor.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[OwnedEntity] types are materialized by EF Core through a generator-emitted private parameterless constructor. " +
                     "Init-only setters on [OwnedEntity] properties are not covered by Trellis tests today and round-trip behavior is not guaranteed. " +
                     "Use '{ get; private set; }' as the supported, tested shape.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS023: A Location-emitting builder call on a versioned controller is missing the api-version route value.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingApiVersionRouteValue = new(
        id: TrellisDiagnosticIds.MissingApiVersionRouteValue,
        title: "Location route is missing the api-version route value",
        messageFormat: "'{0}' on a versioned controller is not followed by '.WithVersionedRoute()' and does not include the 'api-version' route value. The resulting Location header will 404 on dereference under query/header API versioning. Chain '.WithVersionedRoute()' from 'Trellis.Asp.ApiVersioning' so the version is injected per-request.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When a controller declares one or more [ApiVersion(...)] attributes and emits a Location header via " +
                     "HttpResponseOptionsBuilder<T>.CreatedAtRoute, HttpResponseOptionsBuilder<T>.CreatedAtAction, or " +
                     "HttpResponseOptionsBuilder<T>.WithLocation, the " +
                     "Location header must carry the api-version that the client requested so the URL round-trips " +
                     "correctly. Authoring the route values dictionary by hand (and forgetting api-version) is the most " +
                     "common cause of \"Location 404s\". Chaining .WithVersionedRoute() from Trellis.Asp.ApiVersioning lets " +
                     "the framework inject the version from the per-request ApiVersionReader chain (with sensible " +
                     "declared/default fallbacks). Endpoints that are [ApiVersionNeutral] or use URL-segment versioning " +
                     "are exempt from this warning.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS054: Maybe.Equals is not translatable in IQueryable expressions.
    /// </summary>
    public static readonly DiagnosticDescriptor MaybeEqualsInQueryable = new(
        id: TrellisDiagnosticIds.MaybeEqualsInQueryable,
        title: "Use operators instead of Maybe.Equals/object.Equals in IQueryable expressions",
        messageFormat: "Maybe<T>.Equals and object.Equals on Maybe<T> are not translatable by EF Core. Use == or != operators for IQueryable comparisons.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "MaybeExpressionRewriter handles Maybe<T> == / != operator comparisons in IQueryable expression trees, " +
                     "but Maybe<T>.Equals(other) and object.Equals(maybe, other) remain opaque method calls to EF Core. " +
                     "Use the == or != operators for natural-form comparisons, or use Trellis.EntityFrameworkCore.MaybeQueryableExtensions.WhereEquals.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS055: HasValueWhere uses a captured predicate inside an IQueryable expression.
    /// </summary>
    public static readonly DiagnosticDescriptor NonInlineHasValueWhereInQueryable = new(
        id: TrellisDiagnosticIds.NonInlineHasValueWhereInQueryable,
        title: "Inline HasValueWhere predicates in IQueryable expressions",
        messageFormat: "HasValueWhere requires an inline lambda inside IQueryable expressions; captured delegate variables are not translatable. Inline the lambda or materialize the query first.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "MaybeExpressionRewriter can inspect and inline HasValueWhere(t => ...) predicate bodies in IQueryable expression trees. " +
                     "Captured Func<T, bool> variables, method groups, and other non-inline delegate shapes are opaque to the rewriter and fall through to EF Core translation failures.",
        helpLinkUri: HelpLinkBase);

    /// <summary>
    /// TRLS063: A FluentValidation Must/MustAsync rule has no WithErrorCode.
    /// </summary>
    public static readonly DiagnosticDescriptor MustWithoutErrorCode = new(
        id: TrellisDiagnosticIds.MustWithoutErrorCode,
        title: "Give a Must rule a reason code",
        messageFormat: "'{0}' reports as PredicateValidator, which maps to error.unspecified. Add WithErrorCode(\"...\") so the failure names itself.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every built-in FluentValidation validator carries a name that Trellis projects to a real reason code. " +
                     "Must and MustAsync report as PredicateValidator and AsyncPredicateValidator, both of which project to the " +
                     "error.unspecified sentinel, so a client receiving the failure has nothing to branch on. " +
                     "Chain WithErrorCode(\"...\") onto the rule to name it.",
        helpLinkUri: HelpLinkBase);
}