using Trellis.Asp;

namespace Trellis.Asp.Validation;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Factory for creating validating JSON converters for <see cref="IScalarValue{TSelf, TPrimitive}"/> types.
/// </summary>
/// <remarks>
/// <para>
/// This factory is registered with <see cref="JsonSerializerOptions"/> and automatically
/// creates <see cref="ValidatingJsonConverter{TValue, TPrimitive}"/> instances
/// for any type implementing <see cref="IScalarValue{TSelf, TPrimitive}"/>.
/// </para>
/// <para>
/// Unlike the exception-throwing approach, this factory creates converters that collect
/// validation errors in <see cref="ValidationErrorsContext"/> for comprehensive error reporting.
/// </para>
/// </remarks>
public sealed class ValidatingJsonConverterFactory : JsonConverterFactory
{
    /// <summary>
    /// Determines whether this factory can create a converter for the specified type.
    /// </summary>
    /// <param name="typeToConvert">The type to check.</param>
    /// <returns><c>true</c> if the type implements <see cref="IScalarValue{TSelf, TPrimitive}"/>.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Reflection-enabled fallback factory. Trellis registers this factory only when JsonSerializer.IsReflectionEnabledByDefault is true.")]
    public override bool CanConvert(Type typeToConvert) =>
        ScalarValueTypeHelper.IsScalarValue(typeToConvert);

    /// <summary>
    /// Creates a validating converter for the specified value object type.
    /// </summary>
    /// <param name="typeToConvert">The value object type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A validating JSON converter for the value object type.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Reflection-enabled fallback factory. Trellis registers this factory only when JsonSerializer.IsReflectionEnabledByDefault is true.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reflection-enabled fallback factory. Source-generated contexts should use generated converters instead.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JsonConverterFactory dynamic converter creation is not Native AOT compatible; Trellis does not auto-register this factory when reflection is disabled.")]
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var primitiveType = ScalarValueTypeHelper.GetPrimitiveType(typeToConvert);
        return primitiveType is null
            ? null
            : ScalarValueTypeHelper.CreateGenericInstance<JsonConverter>(
                typeof(ValidatingJsonConverter<,>),
                typeToConvert,
                primitiveType);
    }
}
