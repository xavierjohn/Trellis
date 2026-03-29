namespace Trellis;

/// <summary>
/// Extended interface for scalar value objects that support culture-sensitive string parsing.
/// Use for numeric and date types where <see cref="IFormatProvider"/> matters for parsing.
/// </summary>
/// <typeparam name="TSelf">The value object type itself (CRTP pattern)</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type (must be IComparable)</typeparam>
/// <remarks>
/// <para>
/// This interface extends <see cref="IScalarValue{TSelf, TPrimitive}"/> to add an overload
/// of <c>TryCreate</c> that accepts an <see cref="IFormatProvider"/> for culture-sensitive parsing.
/// </para>
/// <para>
/// Implemented by types whose underlying primitive requires locale-aware parsing:
/// <list type="bullet">
/// <item>Integer types (e.g., Age) — thousand separators vary by culture</item>
/// <item>Decimal types (e.g., MonetaryAmount, Percentage) — decimal separators vary by culture</item>
/// <item>DateTime types — date formats vary by culture</item>
/// </list>
/// </para>
/// <para>
/// <b>NOT</b> implemented by string-based types (EmailAddress, PhoneNumber, etc.)
/// where <see cref="IFormatProvider"/> is irrelevant for parsing.
/// </para>
/// </remarks>
public interface IFormattableScalarValue<TSelf, TPrimitive> : IScalarValue<TSelf, TPrimitive>
    where TSelf : IFormattableScalarValue<TSelf, TPrimitive>
    where TPrimitive : IComparable
{
    /// <summary>
    /// Attempts to create a validated scalar value from a string using the specified format provider.
    /// Use for culture-sensitive parsing of numeric and date values.
    /// </summary>
    /// <param name="value">The raw string value to parse and validate.</param>
    /// <param name="provider">
    /// The format provider for culture-sensitive parsing.
    /// When <c>null</c>, implementations should default to <see cref="System.Globalization.CultureInfo.InvariantCulture"/>.
    /// </param>
    /// <param name="fieldName">
    /// Optional field name for validation error messages. If null, implementations should use
    /// a default field name based on the type name.
    /// </param>
    /// <returns>Success with the scalar value, or Failure with validation errors.</returns>
    static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null);
}
