namespace Trellis.Asp.Validation;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// JSON converter for <c>Maybe&lt;T&gt;</c> where <typeparamref name="T"/> is an STJ-native
/// primitive in the allowed list enforced by <see cref="MaybePrimitiveJsonConverterFactory"/>.
/// </summary>
/// <typeparam name="T">
/// One of: <see cref="string"/>, <see cref="decimal"/>, <see cref="int"/>, <see cref="long"/>,
/// <see cref="short"/>, <see cref="byte"/>, <see cref="double"/>, <see cref="float"/>,
/// <see cref="bool"/>, <see cref="System.Guid"/>, <see cref="System.DateTime"/>,
/// <see cref="System.DateTimeOffset"/>.
/// </typeparam>
/// <remarks>
/// <list type="bullet">
///   <item>JSON <c>null</c> → <c>Maybe&lt;T&gt;.None</c> (no error; absent or null are both "not provided").</item>
///   <item>Primitive JSON value → <c>Maybe.From(T)</c> using STJ's typed primitive reader.</item>
///   <item>Wrong JSON shape (e.g. number for a string field) throws the standard <see cref="JsonException"/>.</item>
///   <item><see cref="Maybe{T}.None"/> writes as JSON <c>null</c>.</item>
/// </list>
/// <para>
/// Reads and writes dispatch on the closed primitive allowed list via typed
/// <see cref="Utf8JsonReader"/> / <see cref="Utf8JsonWriter"/> methods — no reflection, no
/// <see cref="JsonSerializer"/> round-trip, no AOT/trim warnings.
/// </para>
/// </remarks>
public sealed class MaybePrimitiveJsonConverter<T> : JsonConverter<Maybe<T>>
    where T : notnull
{
    /// <inheritdoc />
    public override Maybe<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Maybe<T>.None;

        if (typeof(T) == typeof(string))
        {
            var v = reader.GetString();
            return v is null ? Maybe<T>.None : Maybe.From((T)(object)v);
        }

        if (typeof(T) == typeof(decimal)) return Maybe.From((T)(object)reader.GetDecimal());
        if (typeof(T) == typeof(int)) return Maybe.From((T)(object)reader.GetInt32());
        if (typeof(T) == typeof(long)) return Maybe.From((T)(object)reader.GetInt64());
        if (typeof(T) == typeof(short)) return Maybe.From((T)(object)reader.GetInt16());
        if (typeof(T) == typeof(byte)) return Maybe.From((T)(object)reader.GetByte());
        if (typeof(T) == typeof(double)) return Maybe.From((T)(object)reader.GetDouble());
        if (typeof(T) == typeof(float)) return Maybe.From((T)(object)reader.GetSingle());
        if (typeof(T) == typeof(bool)) return Maybe.From((T)(object)reader.GetBoolean());
        if (typeof(T) == typeof(Guid)) return Maybe.From((T)(object)reader.GetGuid());
        if (typeof(T) == typeof(DateTime)) return Maybe.From((T)(object)reader.GetDateTime());
        if (typeof(T) == typeof(DateTimeOffset)) return Maybe.From((T)(object)reader.GetDateTimeOffset());

        // Unreachable: the factory's CanConvert allowed list gates type creation; this guards
        // against a hand-constructed instance with an out-of-set T.
        throw new JsonException($"MaybePrimitiveJsonConverter does not support T = '{typeof(T)}'.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Maybe<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.HasNoValue)
        {
            writer.WriteNullValue();
            return;
        }

        var v = value.Value!;
        switch (v)
        {
            case string s: writer.WriteStringValue(s); return;
            case decimal d: writer.WriteNumberValue(d); return;
            case int i: writer.WriteNumberValue(i); return;
            case long l: writer.WriteNumberValue(l); return;
            case short sh: writer.WriteNumberValue(sh); return;
            case byte b: writer.WriteNumberValue(b); return;
            case double dbl: writer.WriteNumberValue(dbl); return;
            case float f: writer.WriteNumberValue(f); return;
            case bool bo: writer.WriteBooleanValue(bo); return;
            case Guid g: writer.WriteStringValue(g); return;
            case DateTime dt: writer.WriteStringValue(dt); return;
            case DateTimeOffset dto: writer.WriteStringValue(dto); return;
        }

        throw new JsonException($"MaybePrimitiveJsonConverter does not support T = '{typeof(T)}'.");
    }
}