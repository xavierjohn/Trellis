namespace Trellis.Asp.Validation;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Wraps a non-collection object property whose graph transitively contains a scalar value object,
/// pushing the property name onto the validation ancestor path so a nested value-object failure reports
/// an index-precise field path (e.g. <c>/contact/email</c>) instead of just the leaf property name.
/// </summary>
/// <typeparam name="T">The property (object) type being wrapped.</typeparam>
/// <remarks>
/// Installed by the reflection-mode type-info modifier. It is never constructed under Native AOT, where
/// runtime closed-generic converter construction is disabled; AOT keeps leaf-only field names. The inner
/// object is deserialized through <see cref="JsonSerializer"/> (its own type converter), not a converter
/// captured at modifier time, so a self-referential DTO graph cannot re-enter metadata resolution while
/// this wrapper is being constructed.
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
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (ValidationErrorsContext.PushPathSegment(_propertyName))
        {
            return JsonSerializer.Deserialize<T>(ref reader, options);
        }
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
