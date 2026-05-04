namespace Trellis.Asp.Validation;

using System.Globalization;
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
    public override TResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            var nullFieldName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
            return OnNullToken(nullFieldName);
        }

        var fieldName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
        if (!TryReadPrimitiveValue(ref reader, fieldName, out var primitiveValue))
            return OnValidationFailure();

        if (primitiveValue is null)
        {
            ValidationErrorsContext.AddError(fieldName, $"Cannot deserialize null to {ResourceRef.FormatTypeName(typeof(TValue))}");
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
                            ? $"{ResourceRef.FormatTypeName(typeof(TValue))} is invalid."
                            : createError.Detail);

                return OnValidationFailure();
            });
    }

    private static bool TryReadPrimitiveValue(
        ref Utf8JsonReader reader,
        string fieldName,
        out TPrimitive? primitiveValue)
    {
        if (typeof(TPrimitive).IsEnum)
            return TryReadEnumValue(ref reader, fieldName, out primitiveValue);

        if (!PrimitiveJsonReader.TryRead(ref reader, fieldName, out primitiveValue))
            return false;

        return true;
    }

    private static bool TryReadEnumValue(
        ref Utf8JsonReader reader,
        string fieldName,
        out TPrimitive? primitiveValue)
    {
        primitiveValue = default;

        if (reader.TokenType == JsonTokenType.String)
        {
            var rawValue = reader.GetString();
            if (TryParseEnumValue(rawValue, out primitiveValue))
                return true;

            ValidationErrorsContext.AddError(fieldName, $"'{rawValue}' is not a valid {ResourceRef.FormatTypeName(typeof(TPrimitive))}.");
            return false;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            try
            {
                var enumValue = ReadNumericEnumValue(ref reader);
                if (!IsValidEnumValue(enumValue))
                {
                    ValidationErrorsContext.AddError(fieldName, $"'{enumValue}' is not a valid {ResourceRef.FormatTypeName(typeof(TPrimitive))}.");
                    return false;
                }

                primitiveValue = (TPrimitive)enumValue;
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                ValidationErrorsContext.AddError(fieldName, $"JSON number is not a valid {ResourceRef.FormatTypeName(typeof(TPrimitive))}.");
                return false;
            }
        }

        ValidationErrorsContext.AddError(fieldName, $"JSON token '{reader.TokenType}' is not a valid {ResourceRef.FormatTypeName(typeof(TPrimitive))}.");
        return false;
    }

    private static object ReadNumericEnumValue(ref Utf8JsonReader reader)
    {
        var underlyingType = Enum.GetUnderlyingType(typeof(TPrimitive));
        var rawValue = underlyingType == typeof(ulong)
            ? reader.GetUInt64()
            : Convert.ChangeType(reader.GetInt64(), underlyingType, CultureInfo.InvariantCulture);

        return Enum.ToObject(typeof(TPrimitive), rawValue);
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
    /// Returns the default field name used when no <see cref="ValidationErrorsContext.CurrentPropertyName"/>
    /// is set for the current async-local scope (e.g. AOT consumers that haven't wired the
    /// reflection-based <c>PropertyNameAwareConverter&lt;T&gt;</c>). The CLR simple name is
    /// sanitized via <see cref="ResourceRef.FormatTypeName"/> so closed-generic types such as
    /// <c>Maybe&lt;EmailAddress&gt;</c> produce <c>"maybe"</c> rather than <c>"maybe`1"</c>.
    /// </summary>
    protected static string GetDefaultFieldName() =>
        JsonNamingPolicy.CamelCase.ConvertName(ResourceRef.FormatTypeName(typeof(TValue)));
}