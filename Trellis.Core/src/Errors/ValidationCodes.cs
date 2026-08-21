namespace Trellis;

/// <summary>
/// The closed set of machine-readable reason codes Trellis itself emits.
/// </summary>
/// <remarks>
/// <para>
/// A reason code is the stable, localizable identity of a failure. <see cref="FieldViolation.ReasonCode"/>
/// and <see cref="RuleViolation.ReasonCode"/> carry it to the wire, where a client uses it to look up
/// its own prose rather than parsing the English in <c>Detail</c>. That is only worth doing if the same
/// logical failure always arrives under the same code, which is why this set is closed and why it is
/// declared in one place instead of spelled inline at each producer.
/// </para>
/// <para>
/// <b>This vocabulary is frozen.</b> Codes may be added in later releases; an existing code is never
/// repointed at a different meaning and never silently renamed, because a client's catalog keys on it.
/// A code absent from a client's catalog degrades through namespace fallback — a client that does not
/// know <c>string.exact-length</c> can still fall back on <c>string</c> — so adding is safe in a way
/// that redefining is not.
/// </para>
/// <para>
/// The freeze constrains Trellis, not the application. An application is free to emit any code it
/// likes: FluentValidation's <c>WithErrorCode</c> passes through verbatim, and a domain type can hand
/// any string to <see cref="Error.InvalidInput"/>. Nothing here is validated against application codes.
/// </para>
/// <para>
/// <b>Namespace boundaries</b> decide which code a new producer should reach for, and exist because
/// ~210 producer sites would otherwise each answer the question differently:
/// </para>
/// <list type="table">
/// <item>
///   <term><c>format.*</c></term>
///   <description>
///   Lexical failures only — a CLR scalar could not be constructed from the text. This includes
///   out-of-range-for-type: <c>"99999999999"</c> into an <see cref="int"/> is
///   <see cref="FormatInteger"/>, not <see cref="NumberOverflow"/>, because no integer was ever built.
///   </description>
/// </item>
/// <item>
///   <term><c>string.*</c></term>
///   <description>
///   A string arrived intact but did not match an expected shape. All ISO code sets live here.
///   </description>
/// </item>
/// <item>
///   <term><c>number.*</c></term>
///   <description>
///   Validation of an <em>already-parsed</em> number. <see cref="NumberOverflow"/> is arithmetic only,
///   never a parse. <see cref="NumberPrecision"/> is a parsed decimal exceeding scale or precision —
///   malformed input is <see cref="FormatDecimal"/>.
///   </description>
/// </item>
/// <item>
///   <term><c>value.*</c></term>
///   <description>Type-agnostic presence and comparison.</description>
/// </item>
/// <item>
///   <term><c>fields.*</c></term>
///   <description>
///   The subject is <em>a set of fields</em> rather than one — the cross-field invariants in
///   <c>MaybeInvariant</c>. Accurate whichever wire member ends up carrying the violation.
///   </description>
/// </item>
/// <item>
///   <term><c>enum.*</c>, <c>money.*</c></term>
///   <description>Domain-specific.</description>
/// </item>
/// </list>
/// <para>
/// One code per meaning, regardless of which producer noticed it. A malformed integer is
/// <see cref="FormatInteger"/> whether it arrived through query-string binding, a JSON body, or a
/// generated <c>TryCreate</c> — a client cannot be asked to know which pipeline ran.
/// </para>
/// </remarks>
public static class ValidationCodes
{
    /// <summary>
    /// Emitted when a producer has made no finer code decision. The only string on the wire meaning
    /// "no reason available", and it appears at no site meaning anything else.
    /// </summary>
    public const string Unspecified = "error.unspecified";

    /// <summary>
    /// The placeholder that predates this vocabulary. Producers no longer emit it; every projection
    /// maps it onto <see cref="Unspecified"/> so an application that adopted it keeps working.
    /// </summary>
    public const string LegacyUnspecified = "validation.error";

    /// <summary>
    /// Maps <see cref="LegacyUnspecified"/> onto <see cref="Unspecified"/>, leaving every other code
    /// untouched.
    /// </summary>
    /// <remarks>
    /// Lives here rather than in a boundary package because more than one boundary applies it, and a
    /// second copy is how two altitudes come to disagree about the spelling of "no reason available".
    /// A consumer that reads a code from an HTTP body and pastes it into a trace query is relying on
    /// exactly this being one function.
    /// </remarks>
    /// <param name="code">The producer-supplied code.</param>
    /// <returns>The code a consumer should see.</returns>
    public static string Normalize(string code) =>
        string.Equals(code, LegacyUnspecified, StringComparison.Ordinal) ? Unspecified : code;

    // ---- format.* — a CLR scalar could not be constructed from the text ----

    /// <summary>Not a valid integer. Covers <c>byte</c>, <c>short</c>, <c>int</c> and <c>long</c>, including out-of-range-for-type.</summary>
    public const string FormatInteger = "format.integer";

    /// <summary>Not a valid floating-point number (<c>double</c>, <c>float</c>).</summary>
    public const string FormatNumber = "format.number";

    /// <summary>Not a valid decimal.</summary>
    public const string FormatDecimal = "format.decimal";

    /// <summary>Not a valid boolean.</summary>
    public const string FormatBoolean = "format.boolean";

    /// <summary>Not a valid GUID.</summary>
    public const string FormatGuid = "format.guid";

    /// <summary>Not a valid date and time.</summary>
    public const string FormatDateTime = "format.date-time";

    /// <summary>Not a valid date.</summary>
    public const string FormatDate = "format.date";

    /// <summary>Not a valid time of day.</summary>
    public const string FormatTime = "format.time";

    /// <summary>Not a valid duration or time span.</summary>
    public const string FormatDuration = "format.duration";

    /// <summary>
    /// The text could not be converted to the target type, and no more specific code applies —
    /// including a JSON token of the wrong type.
    /// </summary>
    public const string FormatConversion = "format.conversion";

    // ---- string.* — a string arrived intact but did not match an expected shape ----

    /// <summary>Length outside the permitted range.</summary>
    public const string StringLength = "string.length";

    /// <summary>Shorter than the permitted minimum.</summary>
    public const string StringMinLength = "string.min-length";

    /// <summary>Longer than the permitted maximum.</summary>
    public const string StringMaxLength = "string.max-length";

    /// <summary>Not the one permitted length.</summary>
    public const string StringExactLength = "string.exact-length";

    /// <summary>Did not match the required pattern.</summary>
    public const string StringPattern = "string.pattern";

    /// <summary>Not a valid email address.</summary>
    public const string StringEmail = "string.email";

    /// <summary>Not a valid URL.</summary>
    public const string StringUrl = "string.url";

    /// <summary>Not a valid host name.</summary>
    public const string StringHostname = "string.hostname";

    /// <summary>Not a valid IP address.</summary>
    public const string StringIpAddress = "string.ip-address";

    /// <summary>Not a valid URL slug.</summary>
    public const string StringSlug = "string.slug";

    /// <summary>Not a valid E.164 telephone number.</summary>
    public const string StringPhoneE164 = "string.phone-e164";

    /// <summary>Not a valid ISO 3166 country code.</summary>
    public const string StringCountryCode = "string.country-code";

    /// <summary>Not a valid ISO 639 language code.</summary>
    public const string StringLanguageCode = "string.language-code";

    /// <summary>Not a valid ISO 4217 currency code.</summary>
    public const string StringCurrencyCode = "string.currency-code";

    /// <summary>Not a valid credit card number.</summary>
    public const string StringCreditCard = "string.credit-card";

    // ---- number.* — validation of an already-parsed number ----

    /// <summary>Scale or precision exceeds what the target permits.</summary>
    public const string NumberPrecision = "number.precision";

    /// <summary>An arithmetic operation overflowed. Never a parse failure — that is <c>format.*</c>.</summary>
    public const string NumberOverflow = "number.overflow";

    // ---- value.* — type-agnostic presence and comparison ----

    /// <summary>A required value was null.</summary>
    public const string ValueNotNull = "value.not-null";

    /// <summary>A value was present but empty.</summary>
    public const string ValueNotEmpty = "value.not-empty";

    /// <summary>
    /// A value was the CLR default when a non-default was required — <c>Guid.Empty</c>,
    /// <c>DateTime.MinValue</c>, numeric zero. Distinct from <see cref="ValueNotEmpty"/>: zero is not
    /// empty, and only the string case is genuinely emptiness.
    /// </summary>
    public const string ValueNotDefault = "value.not-default";

    /// <summary>A value was required to be null and was not.</summary>
    public const string ValueMustBeNull = "value.must-be-null";

    /// <summary>A value was required to be empty and was not.</summary>
    public const string ValueMustBeEmpty = "value.must-be-empty";

    /// <summary>Did not equal the required value.</summary>
    public const string ValueMustEqual = "value.must-equal";

    /// <summary>Equalled a forbidden value.</summary>
    public const string ValueMustNotEqual = "value.must-not-equal";

    /// <summary>Not less than the comparison value.</summary>
    public const string ValueLessThan = "value.less-than";

    /// <summary>Not less than or equal to the comparison value.</summary>
    public const string ValueLessThanOrEqual = "value.less-than-or-equal";

    /// <summary>Not greater than the comparison value.</summary>
    public const string ValueGreaterThan = "value.greater-than";

    /// <summary>Not greater than or equal to the comparison value.</summary>
    public const string ValueGreaterThanOrEqual = "value.greater-than-or-equal";

    /// <summary>Outside an inclusive range.</summary>
    public const string ValueBetweenInclusive = "value.between-inclusive";

    /// <summary>Outside an exclusive range.</summary>
    public const string ValueBetweenExclusive = "value.between-exclusive";

    // ---- fields.* — the subject is a set of fields ----

    /// <summary>A field was provided that requires another field which was not.</summary>
    public const string FieldsRequiredWith = "fields.required-with";

    /// <summary>Some but not all of a group were provided, when the group is all-or-nothing.</summary>
    public const string FieldsAllOrNone = "fields.all-or-none";

    /// <summary>More than one of a mutually exclusive group was provided.</summary>
    public const string FieldsMutuallyExclusive = "fields.mutually-exclusive";

    /// <summary>None of a group was provided, when exactly one is required.</summary>
    public const string FieldsExactlyOne = "fields.exactly-one";

    /// <summary>
    /// More than one of a group was provided, when exactly one is required. Distinct from
    /// <see cref="FieldsExactlyOne"/> so a client can say "you must choose one" and "you may not choose
    /// both" differently.
    /// </summary>
    public const string FieldsOnlyOne = "fields.only-one";

    /// <summary>None of a group was provided, when at least one is required.</summary>
    public const string FieldsAtLeastOne = "fields.at-least-one";

    // ---- enum.* ----

    /// <summary>A value parsed as the enum's underlying type but is not a defined member.</summary>
    public const string EnumUndefined = "enum.undefined";

    /// <summary>
    /// A symbolic name does not correspond to any member. Distinct from <see cref="EnumUndefined"/>,
    /// which is about a numeric value that parsed.
    /// </summary>
    public const string EnumNameUndefined = "enum.name-undefined";

    // ---- money.* ----

    /// <summary>An operation combined two amounts in different currencies. Carries <c>expected</c> and <c>actual</c>.</summary>
    public const string MoneyCurrencyMismatch = "money.currency-mismatch";

    /// <summary>An operation would produce a negative amount where none is permitted.</summary>
    public const string MoneyNegativeResult = "money.negative-result";

    // ---- Pre-existing codes, brought under the convention ----
    //
    // These shipped before this vocabulary existed and carried snake_case spellings that
    // contradict the rule the set is supposed to teach. They are renamed rather than
    // grandfathered: a client cannot infer a convention from a set with exceptions, and these
    // are among the codes an integrator meets first.

    /// <summary>A page size was not positive, or exceeded the permitted maximum.</summary>
    public const string PageSizeOutOfRange = "page-size.out-of-range";

    /// <summary>An upstream HTTP response was 400.</summary>
    public const string HttpBadRequest = "http.bad-request";

    /// <summary>An upstream HTTP response was 422.</summary>
    public const string HttpUnprocessableContent = "http.unprocessable-content";

    /// <summary>An upstream HTTP response was 403.</summary>
    public const string HttpForbidden = "http.forbidden";

    /// <summary>An upstream HTTP response was 409.</summary>
    public const string HttpConflict = "http.conflict";

    /// <summary>
    /// An ETag header value could not be parsed. Named to match <see cref="CursorMalformed"/> —
    /// the same failure class, described the same way.
    /// </summary>
    public const string EtagMalformed = "etag.malformed";

    /// <summary>A pagination cursor could not be decoded.</summary>
    public const string CursorMalformed = "cursor.malformed";

    /// <summary>An actor attribute was not valid.</summary>
    public const string AttributeInvalid = "attribute.invalid";

    /// <summary>
    /// Returns the <c>format.*</c> code for "this input could not be read as <paramref name="type"/>".
    /// </summary>
    /// <param name="type">The target CLR type. A nullable value type is unwrapped first.</param>
    /// <returns>
    /// The matching <c>format.*</c> code, or <see cref="FormatConversion"/> when no more specific
    /// code applies.
    /// </returns>
    /// <remarks>
    /// Producer independence is the point: a value that fails to parse must report the same code
    /// whether it arrived as a query parameter, as a JSON scalar, or nested inside a composite. The
    /// mapping lives here, once, so a new producer cannot quietly invent a different answer for a
    /// failure the framework already has a code for.
    /// </remarks>
    public static string FormatCodeFor(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;

        if (target == typeof(int) || target == typeof(long) || target == typeof(short) || target == typeof(byte)
            || target == typeof(uint) || target == typeof(ulong) || target == typeof(ushort) || target == typeof(sbyte))
            return FormatInteger;
        if (target == typeof(decimal)) return FormatDecimal;
        if (target == typeof(double) || target == typeof(float)) return FormatNumber;
        if (target == typeof(bool)) return FormatBoolean;
        if (target == typeof(Guid)) return FormatGuid;
        if (target == typeof(DateTime) || target == typeof(DateTimeOffset)) return FormatDateTime;
        if (target == typeof(DateOnly)) return FormatDate;
        if (target == typeof(TimeOnly)) return FormatTime;
        if (target == typeof(TimeSpan)) return FormatDuration;

        return FormatConversion;
    }
}

/// <summary>
/// The reason codes Trellis emits on <see cref="Error.Unexpected"/> — internal faults rather than
/// anything the caller supplied.
/// </summary>
/// <remarks>
/// Separate from <see cref="ValidationCodes"/> because these describe a failure of the system, not of
/// the input, and a client should never localize prose against them. They follow the same punctuation
/// convention and are covered by the same test, so the framework's codes read consistently whichever
/// family they belong to.
/// </remarks>
public static class FaultCodes
{
    /// <summary>A <c>Result</c> was default-initialized and never assigned a success or failure.</summary>
    public const string DefaultInitialized = "default-initialized";

    /// <summary>An exception escaped to a boundary that converts it into a failed <c>Result</c>.</summary>
    public const string UnhandledException = "unhandled-exception";
}
