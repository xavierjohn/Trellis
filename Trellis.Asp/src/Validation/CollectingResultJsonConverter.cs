namespace Trellis.Asp.Validation;

using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class CollectingResultJsonConverter<T>(
    ResultJsonConverter<T> inner,
    bool scalar) : JsonConverter<T>
{
    public override bool HandleNull => inner.HandleNull;

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonValidationReader.Read(ref reader, inner, typeToConvert, options, scalar);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        inner.Write(writer, value, options);
}

internal static class JsonValidationReader
{
    internal static T? Read<T>(
        ref Utf8JsonReader reader,
        ResultJsonConverter<T> converter,
        Type typeToConvert,
        JsonSerializerOptions options,
        bool scalar)
    {
        if (ValidationErrorsContext.Current is null)
            return converter.Read(ref reader, typeToConvert, options);

        if (reader.TokenType == JsonTokenType.Null && !converter.HandleNull)
            return default;

        var result = converter.TryRead(ref reader, typeToConvert, options);
        if (result.TryGetValue(out var value, out var error))
            return value;

        if (error is not Error.InvalidInput invalidInput)
            throw new TrellisJsonValidationException(error.GetDisplayMessage());

        var propertyPointer = scalar && ValidationErrorsContext.CurrentPropertyName is { Length: > 0 } propertyName
            ? InputPointer.Root.AppendProperty(propertyName).Path
            : string.Empty;
        ValidationErrorsContext.RecordReadFailure(propertyPointer);
        ValidationErrorsContext.AddBodyError(JsonValidationPathRebase.RebaseTo(invalidInput, propertyPointer, scalar));
        return default;
    }
}