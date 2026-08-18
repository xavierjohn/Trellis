namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed <see cref="System.DateTimeOffset"/> value objects that
/// prevent primitive obsession for instants on the wall clock with an offset from UTC. Rejects
/// only <c>null</c> by default; <see cref="System.DateTimeOffset.MinValue"/> rejection is opt-in
/// via the <see cref="NotDefaultAttribute"/> attribute. Recommended for any DateTimeOffset type
/// used as an EF-mapped property to preserve the database invariant guarantee enforced by
/// <c>TrellisScalarConverter</c> on rehydration.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base
/// for <see cref="System.DateTimeOffset"/>-based value objects. When used with the <c>partial</c>
/// keyword, the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, DateTimeOffset&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(DateTimeOffset)</c> - Factory method for DateTimeOffsets (required by IScalarValue)</item>
/// <item><c>TryCreate(DateTimeOffset?, string?)</c> - Factory method with null validation and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from DateTimeOffset</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Validation rules emitted by the source generator:
/// <list type="bullet">
/// <item><c>null</c> is always rejected with the per-type "cannot be empty." message.</item>
/// <item>Apply <see cref="NotDefaultAttribute"/> to additionally reject <see cref="System.DateTimeOffset.MinValue"/> with the per-type "cannot be DateTimeOffset.MinValue." message. Recommended for any DateTimeOffset used as an EF-mapped property to preserve the database invariant guarantee enforced by <c>TrellisScalarConverter</c> on rehydration.</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Audit timestamps (CreatedAt, ModifiedAt, DeletedAt) that need to retain the originating offset</item>
/// <item>Time-zone-aware event timestamps (SubmittedAt, ProcessedAt)</item>
/// <item>Any domain concept requiring a non-default <see cref="System.DateTimeOffset"/></item>
/// </list>
/// </para>
/// <para>
/// Prefer <see cref="RequiredDateTimeOffset{TSelf}"/> over <see cref="RequiredDateTime{TSelf}"/>
/// when the originating UTC offset is part of the domain contract (cross-time-zone scheduling,
/// audit-trail provenance). Use <see cref="RequiredDateTime{TSelf}"/> when the value is always
/// stored and read in a single fixed time zone (typically UTC) and the offset is implicit.
/// </para>
/// </remarks>
/// <example>
/// Lenient default — only <c>null</c> is rejected:
/// <code>
/// public partial class EventTimestamp : RequiredDateTimeOffset&lt;EventTimestamp&gt; { }
///
/// var ok = EventTimestamp.TryCreate(DateTimeOffset.UtcNow);            // Success
/// var min = EventTimestamp.TryCreate(DateTimeOffset.MinValue);         // Success (lenient)
/// var nul = EventTimestamp.TryCreate((DateTimeOffset?)null);           // Failure
/// </code>
/// </example>
/// <example>
/// Strict opt-in — <see cref="System.DateTimeOffset.MinValue"/> rejected:
/// <code>
/// [NotDefault]
/// public partial class SubmittedAt : RequiredDateTimeOffset&lt;SubmittedAt&gt; { }
///
/// var ok = SubmittedAt.TryCreate(DateTimeOffset.UtcNow);               // Success
/// var min = SubmittedAt.TryCreate(DateTimeOffset.MinValue);
/// // Failure: "Submitted At cannot be DateTimeOffset.MinValue."
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredDateTime{TSelf}"/>
public abstract class RequiredDateTimeOffset<TSelf> : ScalarValueObject<TSelf, System.DateTimeOffset>
    where TSelf : RequiredDateTimeOffset<TSelf>, IScalarValue<TSelf, System.DateTimeOffset>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredDateTimeOffset{TSelf}"/> class with the specified DateTimeOffset value.
    /// </summary>
    /// <param name="value">The DateTimeOffset value.</param>
    protected RequiredDateTimeOffset(System.DateTimeOffset value) : base(value)
    {
    }

    /// <summary>
    /// Returns the DateTimeOffset in ISO 8601 round-trip format ("O") using invariant culture.
    /// This ensures JSON serialization via ParsableJsonConverter is deterministic,
    /// culture-independent, and preserves the originating UTC offset on round-trip.
    /// </summary>
    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}