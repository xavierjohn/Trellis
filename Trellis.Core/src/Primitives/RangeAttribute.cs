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

    /// <summary>
    /// Gets or sets an application-defined reason code that replaces the framework default on every
    /// failure this attribute produces.
    /// </summary>
    /// <value>
    /// A non-empty code, or <see langword="null"/> to keep the framework default. Setting it to an
    /// empty or whitespace string is a generator error (<c>TRLS060</c>).
    /// </value>
    /// <remarks>
    /// The framework vocabulary is frozen so that one client catalog entry works across every Trellis
    /// service. An application that owns both ends of its own contract — it wrote the declaration and
    /// it writes the catalog key — may name the failure in its own terms instead, and nothing it does
    /// here can invalidate another party's catalog. An override carries no warning and no penalty.
    /// </remarks>
    public string? Code { get; set; }
}
