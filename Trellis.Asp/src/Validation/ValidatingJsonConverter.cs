using Trellis.Asp;

namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A JSON converter for value objects that implement <see cref="IScalarValue{TSelf, TPrimitive}"/>.
/// This converter collects validation errors instead of throwing exceptions,
/// enabling comprehensive validation error responses.
/// </summary>
/// <typeparam name="TValue">The type of the value object to convert.</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
/// <remarks>
/// <para>
/// This converter enables the pattern where DTOs can contain value objects directly,
/// and all validation errors are collected during deserialization rather than failing
/// on the first invalid value.
/// </para>
/// <para>
/// When a value fails validation:
/// <list type="bullet">
/// <item>The error is added to <see cref="ValidationErrorsContext"/></item>
/// <item>A default value (null) is returned</item>
/// <item>Deserialization continues to collect additional errors</item>
/// </list>
/// </para>
/// <para>
/// After deserialization, use <see cref="ScalarValueValidationFilter"/> to check for errors
/// and return appropriate 400 Bad Request responses.
/// </para>
/// </remarks>
public sealed class ValidatingJsonConverter<TValue, TPrimitive> : ScalarValueJsonConverterBase<TValue?, TValue, TPrimitive>
    where TValue : class, IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
{
    /// <inheritdoc />
    protected override TValue? OnNullToken(string fieldName)
    {
        ValidationErrorsContext.AddError(fieldName, $"{typeof(TValue).Name} cannot be null.");
        return null;
    }

    /// <inheritdoc />
    protected override TValue? WrapSuccess(TValue value) => value;

    /// <inheritdoc />
    protected override TValue? OnValidationFailure() => null;

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TValue? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        PrimitiveJsonWriter.Write(writer, value.Value);
    }
}
