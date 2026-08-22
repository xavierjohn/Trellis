namespace Trellis;

using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using System.Reflection;

/// <summary>
/// The <see cref="Meter"/> and instruments Trellis publishes for validation failures.
/// </summary>
/// <remarks>
/// <para>
/// The question this exists to answer is "is this rule dead?" — whether a reason code the
/// framework can emit is ever actually emitted in production. Nothing else answers it. A trace
/// answers it only for requests that were sampled, and a zero-volume code is indistinguishable
/// from a code whose traces were all sampled away; a log answers it only if someone is
/// aggregating the logs, which is the thing a counter is for.
/// </para>
/// <para>
/// <b>A violation is counted where it is created.</b> The counting site is the <c>ReasonCode</c>
/// initializer on <see cref="FieldViolation"/> and <see cref="RuleViolation"/> — the violation
/// itself, not the <see cref="Error.InvalidInput"/> that carries it. A failure can surface at the
/// HTTP boundary, at the mediator pipeline, at both, or — in a worker — at neither, so no
/// reporting site sees each failure exactly once. Coordinating the reporting sites does not work
/// either: an <c>AsyncLocal</c> set inside an awaited <c>Send</c> is not visible to the caller
/// afterwards, because a callee's assignment does not flow back up.
/// </para>
/// <para>
/// The carrying <see cref="Error.InvalidInput"/> is the wrong site for a subtler reason: it is
/// rebuilt during re-projection. <c>JsonValidationPathRebase</c> re-roots pointers by constructing
/// a fresh <c>InvalidInput</c> from an existing one's violations, and the ASP validation context
/// aggregates collected violations into a final one — so counting there would count the same rule
/// firing two or three times. The violation is the atom that is created once when a rule fires and
/// only ever copied afterwards.
/// </para>
/// <para>
/// A <c>with</c>-expression does not recount. The synthesized copy constructor copies backing
/// fields rather than re-running the initializers, which is what makes re-projection free: the
/// rebase path rewrites a violation's pointer with <c>violation with { Field = ... }</c> and the
/// count is unaffected.
/// </para>
/// </remarks>
public static class ValidationMetrics
{
    /// <summary>
    /// The name of the <see cref="Meter"/> carrying Trellis validation instruments. Subscribe with
    /// <see cref="ValidationMeterProviderBuilderExtensions.AddTrellisValidationInstrumentation"/>.
    /// </summary>
    public const string MeterName = "Trellis.Validation";

    /// <summary>
    /// The name of the counter incremented once per violation carried by a validation failure.
    /// </summary>
    public const string FailuresInstrumentName = "trellis.validation.failures";

    /// <summary>
    /// The <c>validation.code</c> tag value substituted for any code outside the framework
    /// vocabulary.
    /// </summary>
    /// <remarks>
    /// An application code reaches the wire verbatim — <c>ValidationCodeProjection</c> passes
    /// through anything it does not reserve — so an application is free to mint a code per entity,
    /// per tenant, or per request. Tagging those verbatim would let a caller create an unbounded
    /// number of time series, which is the expensive failure mode in a hosted metrics backend.
    /// Framework codes are a closed set, and they are the only ones the dead-rule question is
    /// about, so everything else is folded here. The total stays exact; only the breakdown is
    /// bucketed.
    /// </remarks>
    public const string OtherCode = "other";

    private const string CodeTag = "validation.code";
    private const string ViolationTag = "validation.violation";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        FailuresInstrumentName,
        unit: "{failure}",
        description: "Validation violations, counted once per violation as the failure is created.");

    /// <summary>
    /// Every reason code declared by <see cref="ValidationCodes"/>, read from the constants
    /// themselves so a newly added code is counted without anyone maintaining a second list.
    /// </summary>
    private static readonly FrozenSet<string> KnownCodes =
        typeof(ValidationCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToFrozenSet(StringComparer.Ordinal);

    internal static string Observe(string reasonCode, string violation)
    {
        Record(reasonCode, violation);
        return reasonCode;
    }

    private static void Record(string? reasonCode, string violation)
    {
        if (!Failures.Enabled) return;

        Failures.Add(
            1,
            new KeyValuePair<string, object?>(CodeTag, Bucket(reasonCode)),
            new KeyValuePair<string, object?>(ViolationTag, violation));
    }

    /// <summary>
    /// Maps a reason code onto the bounded set of tag values.
    /// </summary>
    internal static string Bucket(string? reasonCode) =>
        reasonCode is not null && KnownCodes.Contains(reasonCode) ? reasonCode : OtherCode;
}
