namespace Trellis.Asp.Tests.Validation;

using System.Text.Json;
using Trellis;
using Trellis.Asp;
using Trellis.Generated;
using Trellis.Testing;

/// <summary>
/// Test value object whose <c>TryCreate</c> rejects values shorter than 3 chars.
/// Used by the AOT-generated converter integration tests below. Declared with the
/// explicit <see cref="ScalarValueObject{TSelf,T}"/> + <see cref="IScalarValue{TSelf,T}"/>
/// pattern (instead of <c>RequiredString&lt;T&gt;</c>) because the Trellis.Core
/// PrimitiveValueObjectGenerator analyzer is not transitively wired into this test project.
/// </summary>
public sealed class TestM2Sku : ScalarValueObject<TestM2Sku, string>, IScalarValue<TestM2Sku, string>
{
    private TestM2Sku(string value) : base(value) { }

    public static Result<TestM2Sku> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "testM2Sku";
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Fail<TestM2Sku>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), "validation.required")
                { Detail = "TestM2Sku is required." })));
        }

        if (value!.Length < 3)
        {
            return Result.Fail<TestM2Sku>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), "validation.length.min")
                { Detail = "TestM2Sku must be at least 3 characters long." })));
        }

        return Result.Ok(new TestM2Sku(value));
    }
}

/// <summary>
/// Test Guid-wrapping value object used by the M-2 invalid-token integration test:
/// when the JSON token is a string that doesn't parse as a Guid, the typed
/// <c>Utf8JsonReader.GetGuid()</c> call throws — the AOT-generated converter must catch
/// it and record the failure via <see cref="ValidationErrorsContext"/> instead of letting
/// the exception escape as a <c>JsonException</c>.
/// </summary>
public sealed class TestM2OrderId : ScalarValueObject<TestM2OrderId, System.Guid>, IScalarValue<TestM2OrderId, System.Guid>
{
    private TestM2OrderId(System.Guid value) : base(value) { }

    public static Result<TestM2OrderId> TryCreate(System.Guid value, string? fieldName = null) =>
        Result.Ok(new TestM2OrderId(value));

    public static Result<TestM2OrderId> TryCreate(string? value, string? fieldName = null) =>
        throw new System.NotImplementedException();
}

/// <summary>
/// Regression guard for inspection finding M-2: the AOT-generated JSON converter must
/// participate in <see cref="ValidationErrorsContext"/> so semantic-validation failures
/// surface as a 422 ProblemDetails instead of silently coercing the value to <c>null</c>.
/// </summary>
/// <remarks>
/// The AOT generator scans every scalar value object in the compilation and emits a
/// <c>JsonConverter&lt;T&gt;</c> for each one whose primitive is supported (string here).
/// The generated <c>GeneratedValueObjectConverterFactory</c> is then registered on
/// <see cref="JsonSerializerOptions.Converters"/> so the converters are wired in without
/// needing a <c>[GenerateScalarValueConverters]</c>-marked partial context.
/// </remarks>
public sealed class AotGeneratedConverterValidationTests
{
    private static JsonSerializerOptions BuildOptions() => new()
    {
        Converters = { new GeneratedValueObjectConverterFactory() },
    };

    [Fact]
    public void Aot_generated_converter_records_validation_failure_in_ValidationErrorsContext()
    {
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = JsonSerializer.Deserialize<TestM2Sku>("\"ab\"", options);

            result.Should().BeNull("M-2: AOT-generated converter must return null on validation failure");
            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "M-2: AOT-generated converter must record validation failures via ValidationErrorsContext, " +
                "not silently coerce them to null");
        }
    }

    [Fact]
    public void Aot_generated_converter_records_failure_under_default_field_name_when_no_property_scope_active()
    {
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<TestM2Sku>("\"ab\"", options);

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().Contain(
                f => f.Field.Path.Contains("testM2Sku"),
                "M-2: when no PropertyName scope is set, AOT converter must fall back to the camel-cased type name");
        }
    }

    [Fact]
    public void Aot_generated_converter_uses_CurrentPropertyName_when_set()
    {
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "sku";
            try
            {
                JsonSerializer.Deserialize<TestM2Sku>("\"ab\"", options);
            }
            finally
            {
                ValidationErrorsContext.CurrentPropertyName = null;
            }

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().Contain(
                f => f.Field.Path.Contains("sku"),
                "M-2: AOT converter must consult ValidationErrorsContext.CurrentPropertyName for the field name");
        }
    }

    [Fact]
    public void Aot_generated_converter_records_failure_for_null_token()
    {
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = JsonSerializer.Deserialize<TestM2Sku>("null", options);

            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "M-2: AOT-generated converter must record a validation error when the JSON token is null");
        }
    }

    [Fact]
    public void Aot_generated_converter_returns_value_when_input_is_valid()
    {
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            var result = JsonSerializer.Deserialize<TestM2Sku>("\"ABC\"", options);

            result.Should().NotBeNull();
            result!.Value.Should().Be("ABC");
            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "no errors should be recorded for a valid value");
        }
    }

    [Fact]
    public void Aot_generated_converter_records_failure_for_invalid_primitive_format_instead_of_throwing()
    {
        // M-2 (GPT-5.5 finding): when the JSON token is a syntactically valid string that
        // doesn't parse as a Guid (e.g. "not-a-guid"), reader.GetGuid() throws FormatException.
        // The token type is correct -- it's the *content* of the primitive that's invalid for the
        // wrapped type. Reflection mode catches the FormatException via PrimitiveJsonReader.TryRead
        // and records a 422; the AOT generator must do the same so AOT consumers don't see a
        // JsonException escape into the deserializer.
        var options = BuildOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            TestM2OrderId? result = null;
            var act = () => result = JsonSerializer.Deserialize<TestM2OrderId>("\"not-a-guid\"", options);

            act.Should().NotThrow(
                "M-2: invalid primitive content (e.g. an unparseable Guid string) must surface as a " +
                "ValidationErrorsContext entry, not escape from the converter as a JsonException");
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "M-2: invalid primitive content must be recorded so ScalarValueValidationMiddleware can produce a 422");
        }
    }
}
