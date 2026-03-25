namespace Trellis;

/// <summary>
/// Specifies the minimum and maximum allowed values for numeric value objects
/// (<see cref="RequiredInt{TSelf}"/>, <see cref="RequiredDecimal{TSelf}"/>, <see cref="RequiredLong{TSelf}"/>).
/// </summary>
/// <remarks>
/// <para>
/// The source generator reads the constructor arguments at compile time and emits range validation
/// in the generated <c>TryCreate</c> method. This attribute does not rely on runtime reflection.
/// </para>
/// <para>
/// <strong>Note:</strong> This is <c>Trellis.RangeAttribute</c>, not <c>System.ComponentModel.DataAnnotations.RangeAttribute</c>.
/// If your project imports <c>System.ComponentModel.DataAnnotations</c>, use the fully qualified name
/// <c>[Trellis.Range(min, max)]</c> to avoid ambiguity.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Range(1, 999)]
/// public partial class Quantity : RequiredInt&lt;Quantity&gt; { }
///
/// [Range(0.01, 99.99)]
/// public partial class UnitPrice : RequiredDecimal&lt;UnitPrice&gt; { }
///
/// [Range(0L, 5_000_000_000L)]
/// public partial class LargeId : RequiredLong&lt;LargeId&gt; { }
/// </code>
/// </example>
/// <seealso cref="RequiredInt{TSelf}"/>
/// <seealso cref="RequiredDecimal{TSelf}"/>
/// <seealso cref="RequiredLong{TSelf}"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RangeAttribute : Attribute
{
    /// <summary>
    /// Specifies an int range. Use for <see cref="RequiredInt{TSelf}"/> and <see cref="RequiredDecimal{TSelf}"/> (whole numbers).
    /// </summary>
    public RangeAttribute(int minimum, int maximum) { }

    /// <summary>
    /// Specifies a long range. Use for <see cref="RequiredLong{TSelf}"/>.
    /// </summary>
    public RangeAttribute(long minimum, long maximum) { }

    /// <summary>
    /// Specifies a double range. Use for <see cref="RequiredDecimal{TSelf}"/> with fractional bounds.
    /// C# does not allow decimal in attribute constructors, so double is used.
    /// </summary>
    public RangeAttribute(double minimum, double maximum) { }
}
