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
    /// Gets the minimum allowed value (inclusive).
    /// </summary>
    /// <value>The minimum value, inclusive.</value>
    public int Minimum { get; }

    /// <summary>
    /// Gets the maximum allowed value (inclusive).
    /// </summary>
    /// <value>The maximum value, inclusive.</value>
    public int Maximum { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeAttribute"/> class
    /// with the specified minimum and maximum values.
    /// </summary>
    /// <param name="minimum">The minimum allowed value, inclusive.</param>
    /// <param name="maximum">
    /// The maximum allowed value, inclusive. Must be greater than or equal to <paramref name="minimum"/>.
    /// </param>
    /// <example>
    /// <code>
    /// [Range(1, 999)]
    /// public partial class Quantity : RequiredInt&lt;Quantity&gt; { }
    /// </code>
    /// </example>
    public RangeAttribute(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }
}
