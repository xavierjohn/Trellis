namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed boolean value objects that distinguish between
/// <c>false</c> (an explicit value) and <c>null</c>/missing (no value provided).
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for boolean-based value objects.
/// When used with the <c>partial</c> keyword, the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, bool&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(bool)</c> - Factory method for booleans (required by IScalarValue)</item>
/// <item><c>TryCreate(bool?, string?)</c> - Factory method with null validation and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from bool</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Feature flags (IsEnabled, IsActive) where <c>false</c> is meaningful</item>
/// <item>Consent tracking (HasConsented, AcceptedTerms)</item>
/// <item>Configuration options (GiftWrap, ExpressShipping)</item>
/// <item>Any domain concept where null vs false distinction matters</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed boolean value object:
/// <code>
/// public partial class GiftWrap : RequiredBool&lt;GiftWrap&gt; { }
///
/// var result = GiftWrap.TryCreate(true);
/// var noWrap = GiftWrap.TryCreate(false); // Success — false is a valid value!
/// var missing = GiftWrap.TryCreate((bool?)null); // Failure — null is rejected
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredInt{TSelf}"/>
public abstract class RequiredBool<TSelf> : ScalarValueObject<TSelf, bool>
    where TSelf : RequiredBool<TSelf>, IScalarValue<TSelf, bool>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredBool{TSelf}"/> class with the specified boolean value.
    /// </summary>
    /// <param name="value">The boolean value.</param>
    protected RequiredBool(bool value) : base(value)
    {
    }
}
