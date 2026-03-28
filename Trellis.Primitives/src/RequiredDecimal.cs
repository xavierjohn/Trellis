namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed decimal value objects that must be explicitly provided.
/// Provides a foundation for monetary amounts, percentages, and other domain concepts represented by decimals.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for decimal-based value objects
/// with automatic validation. When used with the <c>partial</c> keyword,
/// the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, decimal&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(decimal)</c> - Factory method for decimals (required by IScalarValue)</item>
/// <item><c>TryCreate(decimal?, string?)</c> - Factory method with null validation and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from decimal</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Monetary amounts (Price, Amount, Balance)</item>
/// <item>Rates and percentages (InterestRate, TaxRate)</item>
/// <item>Measurements requiring precision (Weight, Distance)</item>
/// <item>Any domain concept requiring a validated decimal value</item>
/// </list>
/// </para>
/// <para>
/// Benefits over plain decimals:
/// <list type="bullet">
/// <item><strong>Type safety</strong>: Cannot accidentally use Price where TaxRate is expected</item>
/// <item><strong>Validation</strong>: Ensures values are explicitly provided (not null) at creation time</item>
/// <item><strong>Domain clarity</strong>: Makes code more self-documenting and expressive</item>
/// <item><strong>Serialization</strong>: Consistent JSON and database representation</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed monetary value:
/// <code>
/// // Define the value object (partial keyword enables source generation)
/// public partial class UnitPrice : RequiredDecimal&lt;UnitPrice&gt;
/// {
/// }
/// 
/// // The source generator automatically creates:
/// // - IScalarValue&lt;UnitPrice, decimal&gt; interface implementation
/// // - public static Result&lt;UnitPrice&gt; TryCreate(decimal value, string? fieldName = null)
/// // - public static Result&lt;UnitPrice&gt; TryCreate(decimal? value, string? fieldName = null)
/// // - public static Result&lt;UnitPrice&gt; TryCreate(string? value, string? fieldName = null)
/// // - public static UnitPrice Parse(string s, IFormatProvider? provider)
/// // - public static bool TryParse(string? s, IFormatProvider? provider, out UnitPrice result)
/// // - public static explicit operator UnitPrice(decimal value)
/// // - private UnitPrice(decimal value) : base(value) { }
/// 
/// // Usage examples:
/// 
/// // Create from existing decimal with validation
/// var result1 = UnitPrice.TryCreate(19.99m);
/// // Returns: Success(UnitPrice) if value != 0
/// // Returns: Failure(ValidationError) if value == 0
/// 
/// // Create from string with validation
/// var result2 = UnitPrice.TryCreate("19.99");
/// // Returns: Success(UnitPrice) if valid decimal format
/// // Returns: Failure(ValidationError) if invalid format
/// 
/// // With custom field name for validation errors
/// var result3 = UnitPrice.TryCreate(input, "product.price");
/// // Error field will be "product.price" instead of default "unitPrice"
/// </code>
/// </example>
/// <example>
/// Multiple strongly-typed decimal values in the same domain:
/// <code>
/// public partial class Price : RequiredDecimal&lt;Price&gt; { }
/// public partial class TaxRate : RequiredDecimal&lt;TaxRate&gt; { }
/// public partial class DiscountAmount : RequiredDecimal&lt;DiscountAmount&gt; { }
/// 
/// public class OrderLine
/// {
///     public Price UnitPrice { get; }
///     public TaxRate Tax { get; }
///     
///     // Compiler prevents mixing values:
///     // ApplyDiscount(price); // Won't compile when DiscountAmount expected!
/// }
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredGuid{TSelf}"/>
/// <seealso cref="RequiredString{TSelf}"/>
/// <seealso cref="RequiredInt{TSelf}"/>
public abstract class RequiredDecimal<TSelf> : ScalarValueObject<TSelf, decimal>
    where TSelf : RequiredDecimal<TSelf>, IScalarValue<TSelf, decimal>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredDecimal{TSelf}"/> class with the specified decimal value.
    /// </summary>
    /// <param name="value">The decimal value.</param>
    /// <remarks>
    /// <para>
    /// This constructor is protected and should be called by derived classes.
    /// When using the source generator (with <c>partial</c> keyword), a private constructor
    /// is automatically generated that includes validation.
    /// </para>
    /// <para>
    /// Direct instantiation should be avoided. Instead, use the generated factory methods:
    /// <list type="bullet">
    /// <item><c>TryCreate(decimal, string?)</c> - Create from decimal with validation</item>
    /// <item><c>TryCreate(decimal?, string?)</c> - Create from nullable decimal with validation</item>
    /// <item><c>TryCreate(string?, string?)</c> - Create from string with validation</item>
    /// </list>
    /// </para>
    /// </remarks>
    protected RequiredDecimal(decimal value) : base(value)
    {
    }
}