namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed integer value objects that must be explicitly provided.
/// Provides a foundation for entity identifiers, counts, and other domain concepts represented by integers.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for integer-based value objects
/// with automatic validation. When used with the <c>partial</c> keyword,
/// the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, int&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(int)</c> - Factory method for integers (required by IScalarValue)</item>
/// <item><c>TryCreate(int?, string?)</c> - Factory method with null validation and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from int</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Legacy entity identifiers (CustomerId, OrderId when using int IDs)</item>
/// <item>Reference numbers (InvoiceNumber, TicketNumber)</item>
/// <item>Sequence numbers</item>
/// <item>Any domain concept requiring a validated integer value</item>
/// </list>
/// </para>
/// <para>
/// Benefits over plain integers:
/// <list type="bullet">
/// <item><strong>Type safety</strong>: Cannot accidentally use CustomerId where OrderId is expected</item>
/// <item><strong>Validation</strong>: Ensures values are explicitly provided (not null) at creation time</item>
/// <item><strong>Domain clarity</strong>: Makes code more self-documenting and expressive</item>
/// <item><strong>Serialization</strong>: Consistent JSON and database representation</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed entity identifier:
/// <code>
/// // Define the value object (partial keyword enables source generation)
/// public partial class TicketNumber : RequiredInt&lt;TicketNumber&gt;
/// {
/// }
/// 
/// // The source generator automatically creates:
/// // - IScalarValue&lt;TicketNumber, int&gt; interface implementation
/// // - public static Result&lt;TicketNumber&gt; TryCreate(int value, string? fieldName = null)
/// // - public static Result&lt;TicketNumber&gt; TryCreate(int? value, string? fieldName = null)
/// // - public static Result&lt;TicketNumber&gt; TryCreate(string? value, string? fieldName = null)
/// // - public static TicketNumber Parse(string s, IFormatProvider? provider)
/// // - public static bool TryParse(string? s, IFormatProvider? provider, out TicketNumber result)
/// // - public static explicit operator TicketNumber(int value)
/// // - private TicketNumber(int value) : base(value) { }
/// 
/// // Usage examples:
/// 
/// // Create from existing integer with validation
/// var result1 = TicketNumber.TryCreate(12345);
/// // Returns: Success(TicketNumber)
/// // Returns: Failure(Error.UnprocessableContent) if null
/// 
/// // Create from string with validation
/// var result2 = TicketNumber.TryCreate("12345");
/// // Returns: Success(TicketNumber) if valid integer format
/// // Returns: Failure(Error.UnprocessableContent) if invalid format
/// 
/// // With custom field name for validation errors
/// var result3 = TicketNumber.TryCreate(input, "ticket.number");
/// // Error field will be "ticket.number" instead of default "ticketNumber"
/// </code>
/// </example>
/// <example>
/// Multiple strongly-typed integer IDs in the same domain:
/// <code>
/// public partial class InvoiceNumber : RequiredInt&lt;InvoiceNumber&gt; { }
/// public partial class LineNumber : RequiredInt&lt;LineNumber&gt; { }
/// 
/// public class Invoice
/// {
///     public InvoiceNumber Number { get; }
///     private readonly List&lt;InvoiceLine&gt; _lines = [];
///     
///     // Compiler prevents mixing IDs:
///     // AddLine(invoiceNumber); // Won't compile - type safety!
/// }
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredGuid{TSelf}"/>
/// <seealso cref="RequiredString{TSelf}"/>
public abstract class RequiredInt<TSelf> : ScalarValueObject<TSelf, int>
    where TSelf : RequiredInt<TSelf>, IScalarValue<TSelf, int>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredInt{TSelf}"/> class with the specified integer value.
    /// </summary>
    /// <param name="value">The integer value.</param>
    /// <remarks>
    /// <para>
    /// This constructor is protected and should be called by derived classes.
    /// When using the source generator (with <c>partial</c> keyword), a private constructor
    /// is automatically generated that includes validation.
    /// </para>
    /// <para>
    /// Direct instantiation should be avoided. Instead, use the generated factory methods:
    /// <list type="bullet">
    /// <item><c>TryCreate(int, string?)</c> - Create from int with validation</item>
    /// <item><c>TryCreate(int?, string?)</c> - Create from nullable int with validation</item>
    /// <item><c>TryCreate(string?, string?)</c> - Create from string with validation</item>
    /// </list>
    /// </para>
    /// </remarks>
    protected RequiredInt(int value) : base(value)
    {
    }
}