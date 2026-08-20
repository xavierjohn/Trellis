namespace Trellis.Asp.Validation;

using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Reads a JSON array element-by-element, pushing the collection property name and each element index
/// onto the validation ancestor path, so a scalar value object nested inside a collection reports an
/// index-precise field path (e.g. <c>/members/0/email</c>) instead of just the leaf property name.
/// </summary>
/// <typeparam name="TCollection">The collection property type (e.g. <c>List&lt;T&gt;</c> or <c>T[]</c>).</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
/// <remarks>
/// System.Text.Json's built-in collection converters give no per-element hook, so the modifier installs
/// this wrapper for collection properties whose element graph transitively contains a value object. The
/// modifier closes this generic either at runtime (reflection mode) or at compile time (Native AOT, via
/// the source-generated <see cref="ScalarValuePathTracking"/> registrations); the converter body itself
/// is AOT-safe because elements are read through a resolved <see cref="JsonTypeInfo{T}"/>.
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
    public override TCollection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            // The property segment is pushed before throwing rather than left to be rebased from
            // JsonException.Path afterwards: arrays sit in the guaranteed body-pointer tier, and
            // the live ancestor stack is the lossless source. A plain JsonException here could
            // not produce a field violation at all, so this failure was invisible to clients.
            using (ValidationErrorsContext.PushPathSegment(_propertyName))
            {
                var message = $"Expected the start of an array for '{_propertyName}'.";
                throw JsonValidationPathRebase.Rebase(new TrellisJsonValidationException(message)
                {
                    InvalidInput = Error.InvalidInput.ForField(
                        InputPointer.Root, ViolationProjection.LegacyUnspecifiedCode, message) with
                    {
                        Detail = message,
                    },
                });
            }
        }

        var elementTypeInfo = (JsonTypeInfo<TElement?>)options.GetTypeInfo(typeof(TElement));
        var items = new List<TElement?>();
        using (ValidationErrorsContext.PushPathSegment(_propertyName))
        {
            var index = 0;
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                using (ValidationErrorsContext.PushPathSegment(index.ToString(CultureInfo.InvariantCulture)))
                {
                    // Re-root composite-relative pointers while the ancestor stack is still live.
                    // Marked exceptions are already absolute and pass straight through — see
                    // JsonValidationPathRebase for why the marker, and not a prefix check, decides.
                    try
                    {
                        items.Add(JsonSerializer.Deserialize(ref reader, elementTypeInfo));
                    }
                    catch (TrellisJsonValidationException ex) when (!JsonValidationPathRebase.IsMarked(ex))
                    {
                        throw JsonValidationPathRebase.Rebase(ex);
                    }
                }

                index++;
                reader.Read();
            }
        }

        return Materialize(items);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TCollection? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value!, (JsonTypeInfo<TCollection>)options.GetTypeInfo(typeof(TCollection)));

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