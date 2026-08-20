namespace Trellis.Asp.Validation;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Reads a string-keyed JSON object entry-by-entry, pushing the dictionary property name and each key
/// onto the validation ancestor path, so a value object nested inside a dictionary reports a
/// key-precise field path (e.g. <c>/prices/USD/amount</c>) instead of just the leaf property name.
/// </summary>
/// <typeparam name="TDictionary">The dictionary property type (e.g. <c>Dictionary&lt;string, TValue&gt;</c>).</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
/// <remarks>
/// <para>
/// Only string-keyed dictionaries are wrapped. A non-string key has no faithful RFC 6901 rendering:
/// the pointer segment would be the key's <em>serialised</em> form, which for a composite or numeric
/// key is a lossy round-trip that a client cannot reliably map back to the input it sent. Those shapes
/// are left to System.Text.Json, which yields leaf-only paths — incomplete, but never wrong.
/// </para>
/// <para>
/// The key is pushed verbatim; <see cref="ValidationErrorsContext.PushPathSegment"/> owns RFC 6901
/// escaping, so a key containing <c>~</c> or <c>/</c> is escaped rather than silently splitting the
/// pointer into extra segments. This is the concrete reason the ancestor stack, and not
/// <see cref="System.Text.Json.JsonException.Path"/>, is the authoritative base path: arbitrary
/// user-supplied dictionary keys are exactly where a parseable path string stops round-tripping.
/// </para>
/// </remarks>
internal sealed class PathTrackingDictionaryConverter<TDictionary, TValue> : JsonConverter<TDictionary?>
{
    private readonly string _propertyName;

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <summary>
    /// Creates a new path-tracking dictionary wrapper.
    /// </summary>
    /// <param name="propertyName">The JSON property name to push onto the ancestor path during reads.</param>
    public PathTrackingDictionaryConverter(string propertyName) => _propertyName = propertyName;

    /// <inheritdoc />
    public override TDictionary? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected the start of an object for '{_propertyName}'.");

        var valueTypeInfo = (JsonTypeInfo<TValue?>)options.GetTypeInfo(typeof(TValue));
        var entries = new Dictionary<string, TValue?>(StringComparer.Ordinal);

        using (ValidationErrorsContext.PushPathSegment(_propertyName))
        {
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();

                using (ValidationErrorsContext.PushPathSegment(key))
                {
                    // Re-root composite-relative pointers while the ancestor stack is still live.
                    // Marked exceptions are already absolute and pass straight through — see
                    // JsonValidationPathRebase for why the marker, and not a prefix check, decides.
                    try
                    {
                        entries[key] = JsonSerializer.Deserialize(ref reader, valueTypeInfo);
                    }
                    catch (TrellisJsonValidationException ex) when (!JsonValidationPathRebase.IsMarked(ex))
                    {
                        throw JsonValidationPathRebase.Rebase(ex);
                    }
                }

                reader.Read();
            }
        }

        // The modifier only installs this converter when Dictionary<string, TValue> is assignable to
        // TDictionary, so this cast is always valid.
        return (TDictionary)(object)entries;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TDictionary? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value!, (JsonTypeInfo<TDictionary>)options.GetTypeInfo(typeof(TDictionary)));
}
