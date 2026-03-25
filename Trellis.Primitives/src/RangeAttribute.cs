namespace Trellis;

/// <summary>
/// Specifies the minimum and maximum allowed values for a
/// <see cref="RequiredInt{TSelf}"/>-derived value object.
/// </summary>
/// <remarks>
/// <para>
/// When applied to a <c>partial class</c> inheriting from <see cref="RequiredInt{TSelf}"/>,
/// the source generator automatically includes range validation in the generated <c>TryCreate</c> method.
/// The range check replaces the default zero-check, so <c>[Range(0, 100)]</c> will allow zero.
/// </para>
/// <para>
/// This attribute is designed specifically for Trellis value objects and is processed at compile time
/// by the PrimitiveValueObjectGenerator source generator. It does not rely on runtime reflection.
/// </para>
/// <para>
/// <strong>Note:</strong> This is <c>Trellis.RangeAttribute</c>, not <c>System.ComponentModel.DataAnnotations.RangeAttribute</c>.
/// If your project imports <c>System.ComponentModel.DataAnnotations</c>, use the fully qualified name
/// <c>[Trellis.Range(min, max)]</c> to avoid ambiguity.
/// </para>
/// </remarks>
/// <example>
/// Range with positive minimum:
/// <code>
/// [Range(1, 999)]
/// public partial class Quantity : RequiredInt&lt;Quantity&gt; { }
///
/// // Generated TryCreate validates:
/// // - Value &gt;= 1
/// // - Value &lt;= 999
/// </code>
/// </example>
/// <example>
/// Range allowing zero:
/// <code>
/// [Range(0, 100)]
/// public partial class Percentage : RequiredInt&lt;Percentage&gt; { }
///
/// // Generated TryCreate validates:
/// // - Value &gt;= 0
/// // - Value &lt;= 100
/// // (zero is allowed because minimum is 0)
/// </code>
/// </example>
/// <seealso cref="RequiredInt{TSelf}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RangeAttribute : Attribute
{
    /// <summary>
    /// Gets the minimum allowed value (inclusive) as int.
    /// </summary>
    public int Minimum { get; }

    /// <summary>
    /// Gets the maximum allowed value (inclusive) as int.
    /// </summary>
    public int Maximum { get; }

    /// <summary>
    /// Gets the minimum allowed value (inclusive) as long.
    /// </summary>
    public long LongMinimum { get; }

    /// <summary>
    /// Gets the maximum allowed value (inclusive) as long.
    /// </summary>
    public long LongMaximum { get; }

    /// <summary>
    /// Gets the minimum allowed value (inclusive) as double.
    /// Used for RequiredDecimal when fractional bounds are needed.
    /// </summary>
    public double DoubleMinimum { get; }

    /// <summary>
    /// Gets the maximum allowed value (inclusive) as double.
    /// </summary>
    public double DoubleMaximum { get; }

    /// <summary>
    /// True when the long constructor was used.
    /// </summary>
    public bool IsLongRange { get; }

    /// <summary>
    /// True when the double constructor was used.
    /// </summary>
    public bool IsDoubleRange { get; }

    /// <summary>
    /// Initializes with int range values. Use for RequiredInt and RequiredDecimal (whole numbers).
    /// </summary>
    public RangeAttribute(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        LongMinimum = minimum;
        LongMaximum = maximum;
        DoubleMinimum = minimum;
        DoubleMaximum = maximum;
    }

    /// <summary>
    /// Initializes with long range values. Use for RequiredLong.
    /// </summary>
    public RangeAttribute(long minimum, long maximum)
    {
        LongMinimum = minimum;
        LongMaximum = maximum;
        IsLongRange = true;
        DoubleMinimum = minimum;
        DoubleMaximum = maximum;
        Minimum = (int)Math.Clamp(minimum, int.MinValue, int.MaxValue);
        Maximum = (int)Math.Clamp(maximum, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// Initializes with double range values. Use for RequiredDecimal when fractional bounds are needed.
    /// </summary>
    /// <example>
    /// <code>
    /// [Range(0.01, 999.99)]
    /// public partial class UnitPrice : RequiredDecimal&lt;UnitPrice&gt; { }
    /// </code>
    /// </example>
    public RangeAttribute(double minimum, double maximum)
    {
        DoubleMinimum = minimum;
        DoubleMaximum = maximum;
        IsDoubleRange = true;
        Minimum = (int)Math.Clamp(minimum, int.MinValue, int.MaxValue);
        Maximum = (int)Math.Clamp(maximum, int.MinValue, int.MaxValue);
        LongMinimum = (long)Math.Clamp(minimum, long.MinValue, long.MaxValue);
        LongMaximum = (long)Math.Clamp(maximum, long.MinValue, long.MaxValue);
    }
}
