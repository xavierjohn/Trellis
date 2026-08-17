namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Wraps a non-collection object property whose graph transitively contains a scalar value object,
/// pushing the property name onto the validation ancestor path so a nested value-object failure reports
/// an index-precise field path (e.g. <c>/contact/email</c>) instead of just the leaf property name.
/// </summary>
/// <typeparam name="T">The property (object) type being wrapped.</typeparam>
/// <remarks>
/// <para>
/// Installed by the type-info modifier, which closes this generic either at runtime (reflection mode,
/// via <c>Type.MakeGenericType</c>) or at compile time (Native AOT, via the source-generated
/// <see cref="ScalarValuePathTracking"/> registrations). The converter body itself is AOT-safe.
/// </para>
/// <para>
/// The inner object is deserialized through its own <see cref="JsonTypeInfo{T}"/> resolved from
/// <see cref="JsonSerializerOptions"/> at read time — not a converter captured at modifier time — so a
/// self-referential DTO graph cannot re-enter metadata resolution while this wrapper is being
/// constructed. Resolving the type info (rather than calling the <c>Deserialize&lt;T&gt;(options)</c>
/// overload) is also what keeps this converter free of <c>RequiresDynamicCode</c>.
/// </para>
/// </remarks>
internal sealed class PathTrackingObjectConverter<T> : JsonConverter<T?>
{
    private readonly string _propertyName;

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <summary>
    /// Creates a new path-tracking object wrapper.
    /// </summary>
    /// <param name="propertyName">The JSON property name to push onto the ancestor path during reads.</param>
    public PathTrackingObjectConverter(string propertyName) => _propertyName = propertyName;

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (ValidationErrorsContext.PushPathSegment(_propertyName))
        {
            return JsonSerializer.Deserialize(ref reader, TypeInfo(options));
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value!, TypeInfo(options));

    private static JsonTypeInfo<T> TypeInfo(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
