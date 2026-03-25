namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed DateTime value objects that prevent primitive obsession for dates.
/// Rejects <see cref="DateTime.MinValue"/> as the "empty" equivalent.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for DateTime-based value objects
/// with automatic validation that prevents default/MinValue dates. When used with the <c>partial</c> keyword,
/// the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, DateTime&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(DateTime)</c> - Factory method for DateTimes (required by IScalarValue)</item>
/// <item><c>TryCreate(DateTime?, string?)</c> - Factory method with null/MinValue validation and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from DateTime</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Order dates (OrderDate, ShipDate)</item>
/// <item>Personal dates (BirthDate, HireDate)</item>
/// <item>Scheduling (DueDate, StartDate, EndDate)</item>
/// <item>Any domain concept requiring a non-default DateTime</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed DateTime value object:
/// <code>
/// public partial class OrderDate : RequiredDateTime&lt;OrderDate&gt; { }
///
/// var result = OrderDate.TryCreate(DateTime.UtcNow);
/// var fromString = OrderDate.TryCreate("2026-01-15T12:00:00Z");
/// var invalid = OrderDate.TryCreate(DateTime.MinValue); // Failure
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredInt{TSelf}"/>
public abstract class RequiredDateTime<TSelf> : ScalarValueObject<TSelf, DateTime>
    where TSelf : RequiredDateTime<TSelf>, IScalarValue<TSelf, DateTime>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredDateTime{TSelf}"/> class with the specified DateTime value.
    /// </summary>
    /// <param name="value">The DateTime value. Must not be <see cref="DateTime.MinValue"/>.</param>
    protected RequiredDateTime(DateTime value) : base(value)
    {
    }
}
