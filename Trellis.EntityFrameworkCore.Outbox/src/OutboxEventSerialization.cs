namespace Trellis.EntityFrameworkCore;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for outbox event payloads.
/// </summary>
/// <remarks>
/// Registers <see cref="MaybeJsonConverterFactory"/> so domain and integration events may carry
/// <see cref="Maybe{T}"/> members: a present value serializes as the underlying value, an absent value as
/// JSON <c>null</c>, and both read back symmetrically. Without it the default serializer walks a
/// <see cref="Maybe{T}"/>'s value accessor on an absent member and throws while capturing the event. The
/// same options instance is used for capture, integration-row creation, and relay deserialization so a
/// payload written by one side reads back on the other.
/// </remarks>
internal static class OutboxEventSerialization
{
    /// <summary>The serializer options used for every outbox event payload.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MaybeJsonConverterFactory());
        return options;
    }
}

/// <summary>
/// Creates a <see cref="MaybeJsonConverter{T}"/> for each <see cref="Maybe{T}"/> event member.
/// </summary>
internal sealed class MaybeJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Maybe<>);

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(MaybeJsonConverter<>).MakeGenericType(valueType))!;
    }
}

/// <summary>
/// Serializes a present <see cref="Maybe{T}"/> as its underlying value and an absent one as JSON
/// <c>null</c>, reading both back symmetrically.
/// </summary>
/// <typeparam name="T">The wrapped value type.</typeparam>
internal sealed class MaybeJsonConverter<T> : JsonConverter<Maybe<T>>
    where T : notnull
{
    /// <inheritdoc />
    public override Maybe<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Maybe<T>.None;

        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return value is null ? Maybe<T>.None : Maybe<T>.From(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Maybe<T> value, JsonSerializerOptions options)
    {
        if (value.TryGetValue(out var inner))
            JsonSerializer.Serialize(writer, inner, options);
        else
            writer.WriteNullValue();
    }
}