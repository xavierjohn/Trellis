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
            var fieldName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
            return OnNullToken(fieldName);
        }

        var primitiveValue = JsonSerializer.Deserialize<TPrimitive>(ref reader, options);

        if (primitiveValue is null)
        {
            var fieldName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
            ValidationErrorsContext.AddError(fieldName, $"Cannot deserialize null to {typeof(TValue).Name}");
            return OnValidationFailure();
        }

        var propertyName = ValidationErrorsContext.CurrentPropertyName ?? GetDefaultFieldName();
        var result = TValue.TryCreate(primitiveValue, propertyName);

        if (result.IsSuccess)
            return WrapSuccess(result.Value);

        if (result.Error is ValidationError validationError)
            ValidationErrorsContext.AddError(validationError);
        else
            ValidationErrorsContext.AddError(propertyName, result.Error.Detail);

        return OnValidationFailure();
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