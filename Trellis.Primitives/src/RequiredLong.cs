namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed long integer value objects.
/// Provides a foundation for large identifiers, sequence numbers, and other domain concepts represented by long integers.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for long-based value objects
/// with automatic validation. When used with the <c>partial</c> keyword,
/// the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, long&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(long)</c> - Factory method for longs (required by IScalarValue)</item>
/// <item><c>TryCreate(long?, string?)</c> - Factory method with validation and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from long</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed long identifier:
/// <code>
/// public partial class TraceId : RequiredLong&lt;TraceId&gt; { }
///
/// var result = TraceId.TryCreate(123456789L);
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredInt{TSelf}"/>
public abstract class RequiredLong<TSelf> : ScalarValueObject<TSelf, long>
    where TSelf : RequiredLong<TSelf>, IScalarValue<TSelf, long>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredLong{TSelf}"/> class with the specified long value.
    /// </summary>
    /// <param name="value">The long value.</param>
    protected RequiredLong(long value) : base(value)
    {
    }
}