namespace Trellis.Asp.Validation;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class RegisteredValidationJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        ScalarValuePathTracking.TryGetValidationConverter(typeToConvert) is not null
        || GetResultConverterType(typeToConvert) is not null;

    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Runtime generic construction is guarded by RuntimeFeature.IsDynamicCodeSupported; Native AOT uses closed registrations.")]
    [UnconditionalSuppressMessage("Trimming", "IL2055",
        Justification = "Reflection-enabled fallback only; Native AOT uses closed registrations.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Reflection-enabled converter types come from JsonConverterAttribute; Native AOT uses closed registrations.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The reflection helper rejects reflection-disabled runtimes; Native AOT uses closed registrations.")]
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        ScalarValuePathTracking.TryGetValidationConverter(typeToConvert)
        ?? CreateReflectionConverter(typeToConvert);

    [RequiresDynamicCode("Builds a closed collecting converter at runtime.")]
    [RequiresUnreferencedCode("Instantiates a converter supplied through JsonConverterAttribute.")]
    private static JsonConverter CreateReflectionConverter(Type typeToConvert)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || !JsonSerializer.IsReflectionEnabledByDefault)
            throw new InvalidOperationException($"No validation converter is registered for '{typeToConvert}'.");

        var converterType = GetResultConverterType(typeToConvert)
            ?? throw new InvalidOperationException($"No result-based converter exists for '{typeToConvert}'.");
        var inner = Activator.CreateInstance(converterType)
            ?? throw new InvalidOperationException($"Cannot create the converter for '{typeToConvert}'.");
        var adapter = typeof(CollectingResultJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)(Activator.CreateInstance(adapter, inner, ScalarValueTypeHelper.IsScalarValue(typeToConvert))
            ?? throw new InvalidOperationException($"Cannot create the collecting converter for '{typeToConvert}'."));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Converter base types are inspected only in the reflection-enabled fallback.")]
    private static Type? GetResultConverterType(Type valueType)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || !JsonSerializer.IsReflectionEnabledByDefault)
            return null;

        var converterType = valueType.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
        for (var current = converterType?.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(ResultJsonConverter<>)
                && current.GenericTypeArguments[0] == valueType)
            {
                return converterType;
            }
        }

        return null;
    }
}