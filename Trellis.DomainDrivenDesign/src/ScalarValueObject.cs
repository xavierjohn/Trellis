namespace Trellis;

/// <summary>
/// Base class for value objects that wrap a single scalar value.
/// Provides a strongly-typed wrapper around primitive types with domain semantics.
/// </summary>
/// <typeparam name="TSelf">The derived value object type itself (CRTP pattern).</typeparam>
/// <typeparam name="T">The type of the wrapped scalar value. Must implement <see cref="IComparable"/>.</typeparam>
/// <remarks>
/// <para>
/// Scalar value objects wrap a single primitive value (int, string, decimal, Guid, etc.) to provide:
/// <list type="bullet">
/// <item>Type safety: Prevents mixing of semantically different values (e.g., CustomerId vs OrderId)</item>
/// <item>Domain semantics: Makes code more expressive and self-documenting</item>
/// <item>Validation: Encapsulates validation rules for the wrapped value</item>
/// <item>Implicit conversion: Allows transparent usage as the underlying type</item>
/// <item>IConvertible support: Enables conversion to other types when needed</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Entity identifiers (CustomerId, OrderId, ProductId)</item>
/// <item>Domain primitives (EmailAddress, PhoneNumber, PostalCode)</item>
/// <item>Measurements (Temperature, Distance, Weight)</item>
/// <item>Quantifiers (Percentage, Quantity, Amount)</item>
/// </list>
/// </para>
/// <para>
/// The class implements <see cref="IConvertible"/> to allow conversion operations,
/// and provides an implicit operator to seamlessly convert to the underlying type.
/// </para>
/// </remarks>
/// <example>
/// Simple scalar value object for a strongly-typed ID:
/// <code><![CDATA[
/// public class CustomerId : ScalarValueObject<CustomerId, Guid>
/// {
///     private CustomerId(Guid value) : base(value) { }
///     
///     public static CustomerId NewUnique() => new(Guid.NewGuid());
///     
///     public static Result<CustomerId> TryCreate(Guid value) =>
///         value.ToResult()
///             .Ensure(v => v != Guid.Empty, Error.Validation("Customer ID cannot be empty"))
///             .Map(v => new CustomerId(v));
///     
///     public static Result<CustomerId> TryCreate(string? stringOrNull) =>
///         stringOrNull.ToResult(Error.Validation("Customer ID cannot be empty"))
///             .Bind(s => Guid.TryParse(s, out var guid)
///                 ? Result.Success(guid)
///                 : Error.Validation("Invalid GUID format"))
///             .Bind(TryCreate);
/// }
/// 
/// // Usage
/// var id = CustomerId.NewUnique();
/// Guid guidValue = id; // Implicit conversion to Guid
/// ]]></code>
/// </example>
/// <example>
/// Scalar value object with custom equality and validation:
/// <code><![CDATA[
/// public class Temperature : ScalarValueObject<Temperature, decimal>
/// {
///     private Temperature(decimal value) : base(value) { }
///     
///     public static Result<Temperature> TryCreate(decimal value) =>
///         value.ToResult()
///             .Ensure(v => v >= -273.15m, 
///                    Error.Validation("Temperature cannot be below absolute zero"))
///             .Ensure(v => v <= 1_000_000m,
///                    Error.Validation("Temperature exceeds physical limits"))
///             .Map(v => new Temperature(v));
///     
///     // Custom equality - round to 2 decimal places
///     protected override IEnumerable<IComparable> GetEqualityComponents()
///     {
///         yield return Math.Round(Value, 2);
///     }
///     
///     // Domain operations
///     public static Temperature FromCelsius(decimal celsius) => new(celsius);
///     public static Temperature FromFahrenheit(decimal fahrenheit) => 
///         new((fahrenheit - 32) * 5 / 9);
///     
///     public decimal ToCelsius() => Value;
///     public decimal ToFahrenheit() => (Value * 9 / 5) + 32;
/// }
/// 
/// // Usage
/// var temp1 = Temperature.TryCreate(98.6m);
/// var temp2 = Temperature.TryCreate(98.60m);
/// temp1 == temp2; // true - rounded to same value
/// 
/// decimal celsius = temp1.Value; // Access underlying value
/// ]]></code>
/// </example>
/// <example>
/// Scalar value object for email addresses:
/// <code><![CDATA[
/// public class EmailAddress : ScalarValueObject<EmailAddress, string>
/// {
///     private EmailAddress(string value) : base(value) { }
///     
///     public static Result<EmailAddress> TryCreate(string email) =>
///         email.ToResult(Error.Validation("Email is required", "email"))
///             .Ensure(e => !string.IsNullOrWhiteSpace(e),
///                    Error.Validation("Email cannot be empty", "email"))
///             .Ensure(e => e.Contains("@"),
///                    Error.Validation("Email must contain @", "email"))
///             .Ensure(e => e.Length <= 254,
///                    Error.Validation("Email too long", "email"))
///             .Map(e => new EmailAddress(e.Trim().ToLowerInvariant()));
///     
///     public string Domain => Value.Split('@')[1];
///     public string LocalPart => Value.Split('@')[0];
/// }
/// ]]></code>
/// </example>
public abstract class ScalarValueObject<TSelf, T> : ValueObject, IConvertible
where TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>
where T : IComparable
{
    /// <summary>
    /// Gets the wrapped scalar value.
    /// </summary>
    /// <value>The underlying primitive value wrapped by this value object.</value>
    /// <remarks>
    /// While the value is publicly accessible, the constructor is typically protected,
    /// forcing creation through factory methods that enforce validation.
    /// </remarks>
    public T Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScalarValueObject{TSelf, T}"/> class with the specified value.
    /// </summary>
    /// <param name="value">The value to wrap.</param>
    /// <remarks>
    /// This constructor is protected to enforce creation through factory methods
    /// (typically TryCreate) that implement validation logic.
    /// </remarks>
    protected ScalarValueObject(T value) => Value = value;

    /// <summary>
    /// Returns the components used for equality comparison.
    /// By default, returns the wrapped <see cref="Value"/>.
    /// </summary>
    /// <returns>An enumerable containing the scalar value.</returns>
    /// <remarks>
    /// <para>
    /// Override this method to customize equality comparison.
    /// For example, to compare email addresses case-insensitively or to round decimal values.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// // Custom equality for Temperature - round to 2 decimal places
    /// protected override IEnumerable<IComparable> GetEqualityComponents()
    /// {
    ///     yield return Math.Round(Value, 2);
    /// }
    /// ]]></code>
    /// </example>
    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <summary>
    /// Returns a string representation of the wrapped value.
    /// </summary>
    /// <returns>The string representation of <see cref="Value"/>, or an empty string if null.</returns>
    public override string ToString() => Value?.ToString() ?? string.Empty;

    /// <summary>
    /// Implicitly converts the scalar value object to its underlying type.
    /// </summary>
    /// <param name="valueObject">The scalar value object to convert.</param>
    /// <returns>The wrapped value of type <typeparamref name="T"/>.</returns>
    /// <remarks>
    /// This implicit operator allows scalar value objects to be used transparently
    /// as their underlying type in most contexts, reducing the need for explicit unwrapping.
    /// </remarks>
    /// <example>
    /// <code>
    /// var customerId = CustomerId.NewUnique();
    /// Guid guid = customerId; // Implicit conversion
    /// 
    /// var temperature = Temperature.Create(98.6m);
    /// decimal value = temperature; // Implicit conversion
    /// </code>
    /// </example>
    public static implicit operator T(ScalarValueObject<TSelf, T> valueObject) => valueObject.Value;

    /// <summary>
    /// Creates a validated value object from a primitive value.
    /// Throws an exception if validation fails.
    /// </summary>
    /// <param name="value">The raw primitive value</param>
    /// <returns>The validated value object</returns>
    /// <exception cref="InvalidOperationException">Thrown when validation fails</exception>
    /// <remarks>
    /// <para>
    /// Use this method when you know the value is valid (e.g., in tests, with constants,
    /// or when building from other validated value objects). This provides cleaner code
    /// than calling <c>TryCreate().Value</c>.
    /// </para>
    /// <para>
    /// ⚠️ Don't use this method with user input or uncertain data - use <c>TryCreate</c>
    /// instead to handle validation errors gracefully.
    /// </para>
    /// <para>
    /// This is a default implementation that can be overridden if custom behavior is needed.
    /// </para>
    /// </remarks>
    /// <example>
    /// Use in tests or with known-valid values:
    /// <code><![CDATA[
    /// // ✅ Good - Test data
    /// var email = EmailAddress.Create("test@example.com");
    /// 
    /// // ✅ Good - Building from validated data
    /// var temp = Temperature.Create(98.6m);
    /// 
    /// // ❌ Bad - User input (use TryCreate instead)
    /// var email = EmailAddress.Create(userInput); 
    /// ]]></code>
    /// </example>
#pragma warning disable CA1000 // Do not declare static members on generic types - Required by CRTP pattern
    public static TSelf Create(T value)
#pragma warning restore CA1000
    {
        var result = TSelf.TryCreate(value);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to create {typeof(TSelf).Name}: {result.Error.Detail}");

        return result.Value;
    }

    // IConvertible implementation - delegates to Convert class for the wrapped value

    /// <summary>
    /// Returns the type code of the wrapped value.
    /// </summary>
    /// <returns>The <see cref="TypeCode"/> of type <typeparamref name="T"/>.</returns>
    public TypeCode GetTypeCode() => Type.GetTypeCode(typeof(T));

    /// <summary>
    /// Converts the wrapped value to a <see cref="bool"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="bool"/>.</returns>
    public bool ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="byte"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="byte"/>.</returns>
    public byte ToByte(IFormatProvider? provider) => Convert.ToByte(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="char"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="char"/>.</returns>
    public char ToChar(IFormatProvider? provider) => Convert.ToChar(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="DateTime"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="DateTime"/>.</returns>
    public DateTime ToDateTime(IFormatProvider? provider) => Convert.ToDateTime(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="decimal"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="decimal"/>.</returns>
    public decimal ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="double"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="double"/>.</returns>
    public double ToDouble(IFormatProvider? provider) => Convert.ToDouble(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="short"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="short"/>.</returns>
    public short ToInt16(IFormatProvider? provider) => Convert.ToInt16(Value, provider);

    /// <summary>
    /// Converts the wrapped value to an <see cref="int"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to an <see cref="int"/>.</returns>
    public int ToInt32(IFormatProvider? provider) => Convert.ToInt32(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="long"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="long"/>.</returns>
    public long ToInt64(IFormatProvider? provider) => Convert.ToInt64(Value, provider);

    /// <summary>
    /// Converts the wrapped value to an <see cref="sbyte"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to an <see cref="sbyte"/>.</returns>
    public sbyte ToSByte(IFormatProvider? provider) => Convert.ToSByte(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="float"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="float"/>.</returns>
    public float ToSingle(IFormatProvider? provider) => Convert.ToSingle(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="string"/> using the specified format provider.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="string"/>, or an empty string if null.</returns>
    public string ToString(IFormatProvider? provider) => Convert.ToString(Value, provider) ?? string.Empty;

    /// <summary>
    /// Converts the wrapped value to the specified type.
    /// </summary>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to the specified type.</returns>
    public object ToType(Type conversionType, IFormatProvider? provider) => Convert.ChangeType(Value, conversionType, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="ushort"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="ushort"/>.</returns>
    public ushort ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="uint"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="uint"/>.</returns>
    public uint ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(Value, provider);

    /// <summary>
    /// Converts the wrapped value to a <see cref="ulong"/>.
    /// </summary>
    /// <param name="provider">An <see cref="IFormatProvider"/> for culture-specific formatting.</param>
    /// <returns>The wrapped value converted to a <see cref="ulong"/>.</returns>
    public ulong ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(Value, provider);
}