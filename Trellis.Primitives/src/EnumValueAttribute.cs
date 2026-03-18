namespace Trellis;

/// <summary>
/// Specifies the canonical symbolic value for a <see cref="RequiredEnum{TSelf}"/> member.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to a public static readonly field on a <see cref="RequiredEnum{TSelf}"/> type
/// only when the external symbolic name must differ from the default CLR field name.
/// </para>
/// <para>
/// If the attribute is not present, <see cref="RequiredEnum{TSelf}"/> falls back to the field name.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EnumValueAttribute : Attribute
{
    /// <summary>
    /// Gets the canonical symbolic value for the annotated enum member.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnumValueAttribute"/> class.
    /// </summary>
    /// <param name="value">The external symbolic value to use instead of the field name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public EnumValueAttribute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Enum value cannot be null, empty, or whitespace.", nameof(value));

        Value = value;
    }
}