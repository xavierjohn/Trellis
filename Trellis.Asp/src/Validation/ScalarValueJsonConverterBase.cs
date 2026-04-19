namespace Trellis.Asp.Validation;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Base JSON converter for scalar value objects that collects validation errors
/// instead of throwing exceptions during deserialization.
/// </summary>
/// <typeparam name="TResult">The result type of deserialization (e.g., <c>TValue?</c> or <c>Maybe&lt;TValue&gt;</c>).</typeparam>
/// <typeparam name="TValue">The scalar value object type.</typeparam>
/// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
public abstract class ScalarValueJsonConverterBase<TResult, TValue, TPrimitive> : JsonConverter<TResult>
    where TValue : class, IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
{
    /// <summary>
    /// Tells System.Text.Json to call <see cref="JsonConverter{T}.Read"/> even when the JSON
    /// token is <c>null</c>. Without this, the serializer bypasses the converter for null tokens
    /// on reference-type results, preventing <see cref="OnNullToken"/> from firing.
    /// </summary>
    public override bool HandleNull => true;

    /// <summary>
    /// Returns the result when a JSON null token is read.
    /// </summary>
    /// <param name="fieldName">The resolved field name for error reporting.</param>
    protected abstract TResult OnNullToken(string fieldName);

    /// <summary>
    /// Wraps a successfully validated value object into the result type.
    /// </summary>
    protected abstract TResult WrapSuccess(TValue value);

    /// <summary>
    /// Returns the result when validation fails.
    /// </summary>
    protected abstract TResult OnValidationFailure();

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TPrimitive type parameter is preserved by JSON serialization infrastructure")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON deserialization of primitive types is compatible with AOT")]
    public override TResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            var nullFieldName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
            return OnNullToken(nullFieldName);
        }

        var fieldName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
        if (!TryReadPrimitiveValue(ref reader, options, fieldName, out var primitiveValue))
            return OnValidationFailure();

        if (primitiveValue is null)
        {
            ValidationErrorsContext.AddError(fieldName, $"Cannot deserialize null to {typeof(TValue).Name}");
            return OnValidationFailure();
        }

        return TValue.TryCreate(primitiveValue, fieldName).Match(
            onSuccess: WrapSuccess,
            onFailure: createError =>
            {
                if (createError is Error.UnprocessableContent unprocessable)
                    ValidationErrorsContext.AddError(unprocessable);
                else
                    ValidationErrorsContext.AddError(
                        fieldName,
                        string.IsNullOrWhiteSpace(createError.Detail)
                            ? $"{typeof(TValue).Name} is invalid."
                            : createError.Detail);

                return OnValidationFailure();
            });
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TPrimitive type parameter is preserved by JSON serialization infrastructure")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON deserialization of primitive types is compatible with AOT")]
    private static bool TryReadPrimitiveValue(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        string fieldName,
        out TPrimitive? primitiveValue)
    {
        if (typeof(TPrimitive).IsEnum && reader.TokenType == JsonTokenType.String)
        {
            var rawValue = reader.GetString();
            if (TryParseEnumValue(rawValue, out primitiveValue))
                return true;

            ValidationErrorsContext.AddError(fieldName, $"'{rawValue}' is not a valid {typeof(TPrimitive).Name}.");
            return false;
        }

        try
        {
            primitiveValue = JsonSerializer.Deserialize<TPrimitive>(ref reader, options);
        }
        catch (JsonException)
        {
            primitiveValue = default;
            ValidationErrorsContext.AddError(fieldName, $"{typeof(TValue).Name} is invalid.");
            return false;
        }

        if (typeof(TPrimitive).IsEnum && primitiveValue is not null && !IsValidEnumValue(primitiveValue))
        {
            ValidationErrorsContext.AddError(fieldName, $"'{primitiveValue}' is not a valid {typeof(TPrimitive).Name}.");
            primitiveValue = default;
            return false;
        }

        return true;
    }

    private static bool TryParseEnumValue(string? rawValue, out TPrimitive? primitiveValue)
    {
        primitiveValue = default;
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        if (!Enum.TryParse(typeof(TPrimitive), rawValue, ignoreCase: true, out var enumValue))
            return false;

        if (!IsValidEnumValue(enumValue))
            return false;

        primitiveValue = (TPrimitive)enumValue;
        return true;
    }

    private static bool IsValidEnumValue(object enumValue)
    {
        if (!typeof(TPrimitive).IsEnum)
            return true;

        return typeof(TPrimitive).IsDefined(typeof(FlagsAttribute), inherit: false)
            || Enum.IsDefined(typeof(TPrimitive), enumValue);
    }

    /// <summary>
    /// Gets the default field name derived from the value object type name.
    /// </summary>
    protected static string GetDefaultFieldName()
    {
        var name = typeof(TValue).Name;
        return JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}