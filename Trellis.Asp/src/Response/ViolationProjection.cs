namespace Trellis.Asp;

using System.Collections.Immutable;
using Trellis;

/// <summary>
/// Projects <see cref="FieldViolation"/> and <see cref="RuleViolation"/> onto their wire form.
/// </summary>
/// <remarks>
/// This exists once, and every pipeline that emits validation problems calls it, so the same
/// failure produces the same payload regardless of which pipeline noticed it. Four
/// consistent-looking implementations of the same rule is the failure mode this type removes.
/// </remarks>
internal static class ViolationProjection
{
    /// <summary>
    /// The neutral code emitted when a producer has not made a code decision.
    /// </summary>
    public const string UnspecifiedCode = ValidationCodes.Unspecified;

    /// <summary>
    /// The legacy placeholder that predates the code vocabulary; normalized to
    /// <see cref="UnspecifiedCode"/> on the wire so clients never see two spellings of
    /// "no code was chosen".
    /// </summary>
    public const string LegacyUnspecifiedCode = ValidationCodes.LegacyUnspecified;

    private const string BodyLocation = "body";
    private const string QueryLocation = "query";
    private const string PathLocation = "path";
    private const string HeaderLocation = "header";
    private const string UnknownLocation = "unknown";

    /// <summary>
    /// Maps the legacy placeholder onto the neutral sentinel, leaving every other code untouched.
    /// </summary>
    public static string NormalizeCode(string code) => ValidationCodes.Normalize(code);

    /// <summary>
    /// Projects a pointer onto a location object.
    /// </summary>
    /// <remarks>
    /// Named parameters carry a <c>name</c> and no <c>pointer</c>: their path is a single
    /// RFC 6901-escaped token, not a document location, so the name is recovered by unescaping
    /// that one token. Body and unknown locations carry the pointer as-is.
    /// </remarks>
    public static ViolationLocation ToLocation(InputPointer pointer) => pointer.In switch
    {
        InputLocation.Body => new ViolationLocation(BodyLocation, pointer.Path, null),
        InputLocation.Query => new ViolationLocation(QueryLocation, null, ToName(pointer.Path)),
        InputLocation.Path => new ViolationLocation(PathLocation, null, ToName(pointer.Path)),
        InputLocation.Header => new ViolationLocation(HeaderLocation, null, ToName(pointer.Path)),
        _ => new ViolationLocation(UnknownLocation, pointer.Path, null),
    };

    /// <summary>
    /// Recovers a parameter name from its single-token pointer, reversing the RFC 6901 §3
    /// escaping. <c>'~1'</c> is unescaped before <c>'~0'</c>, mirroring the escape order.
    /// </summary>
    private static string ToName(string path)
    {
        var token = path.Length > 0 && path[0] == '/' ? path[1..] : path;
        return token.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
    }

    /// <summary>
    /// Projects field violations onto their wire form, preserving order.
    /// </summary>
    public static FieldViolationProblemDetail[] ToFieldViolations(EquatableArray<FieldViolation> fields) =>
        fields.Items
            .Select(fv => new FieldViolationProblemDetail(
                NormalizeCode(fv.ReasonCode),
                fv.Detail,
                ToLocation(fv.Field),
                ToArgs(fv.Args)))
            .ToArray();

    /// <summary>
    /// Projects rule violations onto their wire form, preserving order.
    /// </summary>
    /// <remarks>
    /// <c>locations</c> is always materialized, so a rule with no pointers serializes as
    /// <c>[]</c> rather than an omitted member — the difference between "this rule is
    /// form-level" and "this rule told you nothing".
    /// </remarks>
    public static RuleViolationProblemDetail[] ToRuleViolations(EquatableArray<RuleViolation> rules) =>
        rules.Items
            .Select(rv => new RuleViolationProblemDetail(
                NormalizeCode(rv.ReasonCode),
                rv.Detail,
                rv.Fields.Items.Select(ToLocation).ToArray(),
                ToArgs(rv.Args)))
            .ToArray();

    /// <summary>
    /// Builds the problem extensions carrying the structured violations, so every pipeline
    /// emits the same members for the same failure.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> when there is nothing structured to add, which lets a
    /// caller pass it straight to <c>Results.ValidationProblem(extensions:)</c>.
    /// </remarks>
    public static Dictionary<string, object?>? ToProblemExtensions(Error.InvalidInput error)
    {
        Dictionary<string, object?>? extensions = null;

        if (error.Fields.Items.Length > 0)
            (extensions ??= new(StringComparer.Ordinal))["fieldViolations"] = ToFieldViolations(error.Fields);

        if (error.Rules.Items.Length > 0)
            (extensions ??= new(StringComparer.Ordinal))["ruleViolations"] = ToRuleViolations(error.Rules);

        return extensions;
    }

    private static ImmutableDictionary<string, string>? ToArgs(ImmutableDictionary<string, string>? args) =>
        args is { Count: > 0 } ? args : null;
}
