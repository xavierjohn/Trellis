using Trellis.Asp;

namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A generic wrapper converter that sets the property name in <see cref="ValidationErrorsContext"/>
/// before delegating to an inner converter.
/// </summary>
/// <typeparam name="T">The type being converted.</typeparam>
/// <remarks>
/// <para>
/// This converter enables property-name-aware validation by:
/// <list type="bullet">
/// <item>Setting <see cref="ValidationErrorsContext.CurrentPropertyName"/> before reading</item>
/// <item>Delegating to the inner converter for actual deserialization</item>
/// <item>Clearing the property name after reading</item>
/// </list>
/// </para>
/// <para>
/// The inner converter (e.g., <see cref="ValidatingJsonConverter{TValueObject, TPrimitive}"/>) reads from
/// <see cref="ValidationErrorsContext.CurrentPropertyName"/> to determine the field name
/// for validation errors.
/// </para>
/// </remarks>
internal sealed class PropertyNameAwareConverter<T> : JsonConverter<T?>
{
    private readonly JsonConverter<T?> _innerConverter;
    private readonly string _propertyName;

    /// <summary>
    /// Tells System.Text.Json to call <see cref="Read"/> even when the JSON token is <c>null</c>.
    /// Without this, the serializer bypasses the converter for null tokens on reference-type
    /// properties, preventing the inner converter's null-token validation from firing.
    /// </summary>
    public override bool HandleNull => true;

    /// <summary>
    /// Creates a new property-name-aware wrapper converter.
    /// </summary>
    /// <param name="innerConverter">The inner converter to delegate to.</param>
    /// <param name="propertyName">The property name to set in the context during read operations.</param>
    public PropertyNameAwareConverter(JsonConverter<T?> innerConverter, string propertyName)
    {
        _innerConverter = innerConverter;
        _propertyName = propertyName;
    }

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Set the property name in context so the inner converter can use it
        var previousPropertyName = ValidationErrorsContext.CurrentPropertyName;
        ValidationErrorsContext.CurrentPropertyName = _propertyName;
        try
        {
            return _innerConverter.Read(ref reader, typeToConvert, options);
        }
        finally
        {
            // Restore the previous property name (for nested objects)
            ValidationErrorsContext.CurrentPropertyName = previousPropertyName;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
        _innerConverter.Write(writer, value, options);
}