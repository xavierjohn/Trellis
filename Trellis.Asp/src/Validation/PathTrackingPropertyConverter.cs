namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Wraps a scalar property with its effective JSON name without constructing a closed generic at runtime.
/// </summary>
/// <typeparam name="T">The scalar or optional scalar property type.</typeparam>
internal sealed class PathTrackingPropertyConverter<T> : JsonConverter<T?>
{
    private readonly string _propertyName;

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <summary>
    /// Creates a new property path-tracking wrapper.
    /// </summary>
    /// <param name="propertyName">The effective JSON property name.</param>
    public PathTrackingPropertyConverter(string propertyName) => _propertyName = propertyName;

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var previousPropertyName = ValidationErrorsContext.CurrentPropertyName;
        ValidationErrorsContext.CurrentPropertyName = _propertyName;
        try
        {
            return JsonSerializer.Deserialize(ref reader, TypeInfo(options));
        }
        finally
        {
            ValidationErrorsContext.CurrentPropertyName = previousPropertyName;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value!, TypeInfo(options));

    private static JsonTypeInfo<T> TypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
