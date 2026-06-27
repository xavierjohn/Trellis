namespace Trellis.Asp.Validation;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads a JSON array element-by-element, pushing the collection property name and each element index
/// onto the validation ancestor path, so a scalar value object nested inside a collection reports an
/// index-precise field path (e.g. <c>/members/0/email</c>) instead of just the leaf property name.
/// </summary>
/// <typeparam name="TCollection">The collection property type (e.g. <c>List&lt;T&gt;</c> or <c>T[]</c>).</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
/// <remarks>
/// System.Text.Json's built-in collection converters give no per-element hook, so the modifier installs
/// this wrapper for collection properties whose element graph transitively contains a value object. It is
/// never constructed under Native AOT (runtime closed-generic construction is disabled there).
/// </remarks>
internal sealed class PathTrackingCollectionConverter<TCollection, TElement> : JsonConverter<TCollection?>
{
    private readonly string _propertyName;

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <summary>
    /// Creates a new path-tracking collection wrapper.
    /// </summary>
    /// <param name="propertyName">The JSON property name to push onto the ancestor path during reads.</param>
    public PathTrackingCollectionConverter(string propertyName) => _propertyName = propertyName;

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    public override TCollection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected the start of an array for '{_propertyName}'.");

        var items = new List<TElement?>();
        using (ValidationErrorsContext.PushPathSegment(_propertyName))
        {
            var index = 0;
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                using (ValidationErrorsContext.PushPathSegment(index.ToString(CultureInfo.InvariantCulture)))
                {
                    items.Add(JsonSerializer.Deserialize<TElement>(ref reader, options));
                }

                index++;
                reader.Read();
            }
        }

        return Materialize(items);
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reflection-mode-only converter; the modifier never installs it under Native AOT.")]
    public override void Write(Utf8JsonWriter writer, TCollection? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);

    private static TCollection Materialize(List<TElement?> items)
    {
        if (typeof(TCollection).IsArray)
        {
            var array = new TElement?[items.Count];
            items.CopyTo(array);
            return (TCollection)(object)array;
        }

        // The modifier only installs this converter when List<TElement> is assignable to TCollection
        // (List<T> itself or the IList/ICollection/IEnumerable/IReadOnlyList/IReadOnlyCollection<T> it
        // implements), so this cast is always valid.
        return (TCollection)(object)items;
    }
}
