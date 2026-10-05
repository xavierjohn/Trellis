namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal static class PathTrackingJsonReader
{
    internal static T? ReadContainerValue<T>(ref Utf8JsonReader reader, JsonTypeInfo<T?> typeInfo)
    {
        var previousPropertyName = ValidationErrorsContext.CurrentPropertyName;
        // Converter-backed values target the container entry itself; object values retain
        // the property context so their scalar converters can supply a leaf name under AOT.
        if (typeInfo.Kind == JsonTypeInfoKind.None)
            ValidationErrorsContext.CurrentPropertyName = string.Empty;

        try
        {
            return JsonSerializer.Deserialize(ref reader, typeInfo);
        }
        finally
        {
            ValidationErrorsContext.CurrentPropertyName = previousPropertyName;
        }
    }
}
