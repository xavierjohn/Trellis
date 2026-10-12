namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal static class PathTrackingJsonReader
{
    internal static T? ReadValue<T>(ref Utf8JsonReader reader, JsonTypeInfo<T> typeInfo, bool scalar)
    {
        if (typeInfo.Converter is ResultJsonConverter<T> resultConverter)
            return JsonValidationReader.Read(ref reader, resultConverter, typeof(T), typeInfo.Options, scalar);

        if (ValidationErrorsContext.Current is not null
            && typeInfo.Converter.GetType().IsGenericType
            && typeInfo.Converter.GetType().GetGenericTypeDefinition() == typeof(ParsableJsonConverter<>)
            && ScalarValuePathTracking.TryGetValidationConverter(typeof(T)) is System.Text.Json.Serialization.JsonConverter<T> validatingConverter)
        {
            return validatingConverter.Read(ref reader, typeof(T), typeInfo.Options);
        }

        return JsonSerializer.Deserialize(ref reader, typeInfo);
    }

    internal static T? ReadContainerValue<T>(ref Utf8JsonReader reader, JsonTypeInfo<T?> typeInfo)
    {
        var previousPropertyName = ValidationErrorsContext.CurrentPropertyName;
        // Converter-backed values target the container entry itself; object values retain
        // the property context so their scalar converters can supply a leaf name under AOT.
        if (typeInfo.Kind == JsonTypeInfoKind.None)
            ValidationErrorsContext.CurrentPropertyName = string.Empty;

        try
        {
            return ReadValue(ref reader, typeInfo, scalar: false);
        }
        finally
        {
            ValidationErrorsContext.CurrentPropertyName = previousPropertyName;
        }
    }
}