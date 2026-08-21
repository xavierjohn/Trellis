namespace Trellis.FluentValidation;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Maps a FluentValidation <c>ErrorCode</c> onto the Trellis reason-code vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// FluentValidation defaults a failure's <c>ErrorCode</c> to the validator's type name —
/// <c>"NotEmptyValidator"</c>, <c>"GreaterThanValidator"</c>. Those names are stable and
/// well-known, but they are FluentValidation's vocabulary, not Trellis's: a client would have to
/// key on <c>NotEmptyValidator</c> from a FluentValidation rule and <c>value.not-empty</c> from a
/// generated <c>TryCreate</c> for the same condition. Translating here is what lets one client
/// branch cover both producers.
/// </para>
/// <para>
/// The table is keyed on the <b>error-code string, never the CLR validator type</b>. The two
/// disagree in practice: <c>AspNetCoreCompatibleEmailValidator</c> reports
/// <c>Name = "EmailValidator"</c>, so a type-keyed lookup would miss it while a string-keyed one
/// hits. Keying on the string also means a caller's <c>WithErrorCode("EmailValidator")</c> maps
/// identically to the built-in rule, which is the behaviour a reader expects.
/// </para>
/// <para>
/// Four rules, in order:
/// </para>
/// <list type="number">
/// <item>A blank code becomes the neutral sentinel — there is nothing to report.</item>
/// <item>The legacy placeholder <c>validation.error</c> becomes the sentinel, so an application
/// that adopted the old placeholder is not mistaken for one making a deliberate statement.</item>
/// <item>A reserved name becomes its mapped Trellis code.</item>
/// <item>Anything else passes through <b>verbatim</b>. A caller who wrote
/// <c>WithErrorCode("order.too-large")</c> means it, and silently rewriting it would make
/// <c>WithErrorCode</c> useless.</item>
/// </list>
/// <para>
/// <c>PredicateValidator</c> and <c>AsyncPredicateValidator</c> map to the sentinel rather than to
/// a code of their own. A <c>Must(...)</c> rule can express any condition whatsoever, so its
/// validator name says only "some custom predicate failed" — which is precisely what the sentinel
/// means. Minting a <c>predicate.failed</c> code would look informative while telling a client
/// nothing it could branch on.
/// </para>
/// </remarks>
public static class ValidationCodeProjection
{
    private static FrozenDictionary<string, string> Reserved { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Presence and emptiness.
        ["NotNullValidator"] = ValidationCodes.ValueNotNull,
        ["NotEmptyValidator"] = ValidationCodes.ValueNotEmpty,
        ["NullValidator"] = ValidationCodes.ValueMustBeNull,
        ["EmptyValidator"] = ValidationCodes.ValueMustBeEmpty,

        // Comparison.
        ["EqualValidator"] = ValidationCodes.ValueMustEqual,
        ["NotEqualValidator"] = ValidationCodes.ValueMustNotEqual,
        ["GreaterThanValidator"] = ValidationCodes.ValueGreaterThan,
        ["GreaterThanOrEqualValidator"] = ValidationCodes.ValueGreaterThanOrEqual,
        ["LessThanValidator"] = ValidationCodes.ValueLessThan,
        ["LessThanOrEqualValidator"] = ValidationCodes.ValueLessThanOrEqual,
        ["InclusiveBetweenValidator"] = ValidationCodes.ValueBetweenInclusive,
        ["ExclusiveBetweenValidator"] = ValidationCodes.ValueBetweenExclusive,

        // String shape.
        ["LengthValidator"] = ValidationCodes.StringLength,
        ["MinimumLengthValidator"] = ValidationCodes.StringMinLength,
        ["MaximumLengthValidator"] = ValidationCodes.StringMaxLength,
        ["ExactLengthValidator"] = ValidationCodes.StringExactLength,
        ["RegularExpressionValidator"] = ValidationCodes.StringPattern,
        ["EmailValidator"] = ValidationCodes.StringEmail,
        ["AspNetCoreCompatibleEmailValidator"] = ValidationCodes.StringEmail,
        ["CreditCardValidator"] = ValidationCodes.StringCreditCard,

        // Domain-specific.
        ["EnumValidator"] = ValidationCodes.EnumUndefined,
        ["ScalePrecisionValidator"] = ValidationCodes.NumberPrecision,

        // Custom predicates carry no information a client can branch on.
        ["PredicateValidator"] = ValidationCodes.Unspecified,
        ["AsyncPredicateValidator"] = ValidationCodes.Unspecified,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Translates a FluentValidation error code into a Trellis reason code.
    /// </summary>
    /// <param name="errorCode">The failure's <c>ErrorCode</c>; may be <see langword="null"/> or blank.</param>
    /// <returns>
    /// The mapped Trellis code for a reserved FluentValidation validator name, the neutral sentinel
    /// for a blank or legacy-placeholder code, and <paramref name="errorCode"/> unchanged otherwise.
    /// </returns>
    public static string Project(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode) || errorCode == ValidationCodes.LegacyUnspecified)
            return ValidationCodes.Unspecified;

        return Reserved.TryGetValue(errorCode, out var mapped) ? mapped : errorCode;
    }

    /// <summary>
    /// Translates a FluentValidation error code into a Trellis reason code, refining
    /// <c>NotEmptyValidator</c> against the value that was actually rejected.
    /// </summary>
    /// <param name="errorCode">The failure's <c>ErrorCode</c>; may be <see langword="null"/> or blank.</param>
    /// <param name="attemptedValue">The failure's <c>AttemptedValue</c>.</param>
    /// <returns>The mapped Trellis code.</returns>
    /// <remarks>
    /// FluentValidation's <c>NotEmpty()</c> is one rule covering three failures the vocabulary keeps
    /// apart: an absent value is <c>value.not-null</c>, a present-but-blank string or empty
    /// collection is <c>value.not-empty</c>, and a value type left at its default —
    /// <c>Guid.Empty</c>, <c>0</c>, <c>default(DateTime)</c> — is <c>value.not-default</c>. Mapping
    /// the rule to a single code would make <c>RuleFor(x =&gt; x.Id).NotEmpty()</c> report
    /// <c>value.not-empty</c> for <c>Guid.Empty</c> while a Trellis primitive reports
    /// <c>value.not-default</c> for the same input, which is exactly the producer divergence the
    /// vocabulary exists to remove. The code describes the failure, not the rule that caught it.
    /// </remarks>
    public static string Project(string? errorCode, object? attemptedValue)
    {
        var projected = Project(errorCode);
        if (!string.Equals(errorCode, "NotEmptyValidator", StringComparison.Ordinal))
            return projected;

        return attemptedValue switch
        {
            null => ValidationCodes.ValueNotNull,
            string => ValidationCodes.ValueNotEmpty,
            System.Collections.IEnumerable => ValidationCodes.ValueNotEmpty,
            _ => ValidationCodes.ValueNotDefault,
        };
    }
}
