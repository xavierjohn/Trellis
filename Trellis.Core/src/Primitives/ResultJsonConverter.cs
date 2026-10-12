namespace Trellis;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Reads validated JSON values as results while preserving rejection by the standalone serializer.
/// </summary>
/// <typeparam name="T">The validated value type.</typeparam>
public abstract class ResultJsonConverter<T> : JsonConverter<T>
{
    /// <summary>
    /// Reads the complete JSON value, returning expected validation failures without throwing.
    /// </summary>
    /// <param name="reader">The reader positioned at the first token of the value.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The validated value or its structured error.</returns>
    /// <remarks>
    /// On either result track, the reader must finish at the value's final token.
    /// Malformed JSON and configuration or implementation defects still throw.
    /// </remarks>
    public abstract Result<T> TryRead(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);

    /// <inheritdoc />
    public sealed override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = TryRead(ref reader, typeToConvert, options);
        if (result.TryGetValue(out var value, out var error))
            return value;

        throw new TrellisJsonValidationException(error.GetDisplayMessage())
        {
            InvalidInput = error as Error.InvalidInput,
        };
    }
}