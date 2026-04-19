namespace Trellis.Asp.Tests;

using System;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Edge case tests for ValidatingJsonConverter including null handling, error scenarios, and special cases.
/// </summary>
public class ValidatingJsonConverterEdgeCasesTests
{
    #region Test Value Objects

    public class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
    {
        private Email(string value) : base(value) { }
        public static Result<Email> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.ValidatingJsonConverterEdgeCasesTests.Email>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email is required." })));
            return Result.Ok(new Email(value));
        }
    }

    public class Age : ScalarValueObject<Age, int>, IScalarValue<Age, int>
    {
        private Age(int value) : base(value) { }
        public static Result<Age> TryCreate(int value, string? fieldName = null)
        {
            var field = fieldName ?? "age";
            if (value < 0)
                return Result.Fail<Asp.Tests.ValidatingJsonConverterEdgeCasesTests.Age>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Age cannot be negative." })));
            return Result.Ok(new Age(value));
        }
        public static Result<Age> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class URL : ScalarValueObject<URL, string>, IScalarValue<URL, string>
    {
        private URL(string value) : base(value) { }

        public static Result<URL> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "url";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.ValidatingJsonConverterEdgeCasesTests.URL>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "URL is required." })));

            return Result.Ok(new URL(value));
        }
    }

    public enum ProcessingMode
    {
        Unknown = 0,
        Fast = 1,
        Safe = 2,
    }

    public sealed class ProcessingModeVO : ScalarValueObject<ProcessingModeVO, ProcessingMode>, IScalarValue<ProcessingModeVO, ProcessingMode>
    {
        private ProcessingModeVO(ProcessingMode value) : base(value) { }

        public static Result<ProcessingModeVO> TryCreate(ProcessingMode value, string? fieldName = null) =>
            value == ProcessingMode.Unknown
                ? Result.Fail<ProcessingModeVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "processingMode"), "validation.error") { Detail = "Processing mode is required." })))
                : Result.Ok(new ProcessingModeVO(value));
        public static Result<ProcessingModeVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void Read_NullJsonValue_ReturnsNullAndCollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue("null values should produce a validation error for required value objects");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/email");
            error.Fields[0].Detail.Should().Contain("Email cannot be null.");
        }
    }

    [Fact]
    public void Read_NullPrimitiveValue_CollectsErrorWithPropertyName()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "UserEmail";

            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue("null value should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/UserEmail");
            error.Fields[0].Detail.Should().Contain("Email cannot be null.");
        }
    }

    [Fact]
    public void Write_NullValueObject_WritesNull()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, null, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("null");
    }

    #endregion

    #region Default Field Name Tests

    [Fact]
    public void Read_NoPropertyNameSet_UsesTypeNameAsDefault()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"\""; // Empty - invalid
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Don't set CurrentPropertyName

            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            // Should use "email" (camelCase of type name)
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/email");
        }
    }

    [Fact]
    public void Read_TypeNameStartsWithLowerCase_UsesAsIs()
    {
        // Arrange - Create a type that starts with lowercase (unusual but possible)
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Field.Path.Should().Be("/email");
        }
    }

    [Fact]
    public void Read_AcronymTypeName_UsesCamelCaseDefaultFieldName()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<URL, string>();
        var json = "\"\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(URL), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/url");
        }
    }

    #endregion

    #region Error Without Scope Tests

    [Fact]
    public void Read_InvalidValueNoScope_ReturnsNullWithoutException()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"\""; // Invalid
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        // No scope created!

        // Act
        var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

        // Assert
        result.Should().BeNull("should return null even without scope");
        // No exception should be thrown
    }

    #endregion

    #region Non-Error.UnprocessableContent Handling Tests

    [Fact]
    public void Read_EnumStringValue_BindsSuccessfully()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<ProcessingModeVO, ProcessingMode>();
        var json = "\"Safe\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "mode";

            // Act
            var result = converter.Read(ref reader, typeof(ProcessingModeVO), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be(ProcessingMode.Safe);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_NonValidationError_CollectsAsSimpleError()
    {
        // Arrange - Create a value object that returns non-Error.UnprocessableContent
        var converter = new ValidatingJsonConverter<NonValidationErrorVO, int>();
        var json = "999"; // Will trigger unexpected error
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Test";

            // Act
            var result = converter.Read(ref reader, typeof(NonValidationErrorVO), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle();
            error.Fields[0].Field.Path.Should().Be("/Test");
            error.Fields[0].Detail.Should().Contain("Unexpected error");
        }
    }

    public class NonValidationErrorVO : ScalarValueObject<NonValidationErrorVO, int>, IScalarValue<NonValidationErrorVO, int>
    {
        private NonValidationErrorVO(int value) : base(value) { }
        public static Result<NonValidationErrorVO> TryCreate(int value, string? fieldName = null) =>
            // Return non-validation error
            Result.Fail<NonValidationErrorVO>(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "Unexpected error" });
        public static Result<NonValidationErrorVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region Empty String Tests

    [Fact]
    public void Read_EmptyString_CollectsValidationError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Detail.Should().Contain("Email is required.");
        }
    }

    [Fact]
    public void Read_Whitespace_CollectsValidationError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"   \"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public void Read_InvalidPrimitiveToken_CollectsValidationError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = "\"abc\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Age");
        }
    }

    #endregion

    #region Special Character Tests

    [Fact]
    public void RoundTrip_StringWithSpecialCharacters_PreservesValue()
    {
        // Arrange
        var specialString = "test@example.com<>\"'&\t\n\r";
        var converter = new ValidatingJsonConverter<Email, string>();

        // Create valid email (adjust VO if needed)
        var json = JsonSerializer.Serialize(specialString);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        Email? email;
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";
            email = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());
        }

        // If validation passed, write it back
        if (email != null)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            converter.Write(writer, email, new JsonSerializerOptions());
            writer.Flush();

            var outputJson = Encoding.UTF8.GetString(stream.ToArray());
            outputJson.Should().Be(json);
        }
    }

    #endregion

    #region Unicode Tests

    [Fact]
    public void RoundTrip_UnicodeCharacters_PreservesValue()
    {
        // Arrange
        var unicodeString = "测试@例え.com";  // Chinese and Japanese characters
        var converter = new ValidatingJsonConverter<Email, string>();

        var json = JsonSerializer.Serialize(unicodeString);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        Email? email;
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";
            email = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());
        }

        if (email != null)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            converter.Write(writer, email, new JsonSerializerOptions());
            writer.Flush();

            var outputJson = Encoding.UTF8.GetString(stream.ToArray());
            outputJson.Should().Be(json);
        }
    }

    #endregion

    #region Boundary Value Tests

    [Fact]
    public void Read_IntMinValue_HandledCorrectly()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert - Should collect validation error (negative)
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public void Read_IntMaxValue_HandledCorrectly()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert - Should succeed (not negative)
            result.Should().NotBeNull();
            result!.Value.Should().Be(int.MaxValue);
        }
    }

    [Fact]
    public void Read_IntZero_HandledCorrectly()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = "0";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be(0);
        }
    }

    #endregion

    #region Very Long String Tests

    [Fact]
    public void Read_VeryLongString_HandledCorrectly()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var longString = new string('a', 10000) + "@example.com";
        var json = JsonSerializer.Serialize(longString);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be(longString);
        }
    }

    #endregion

    #region Multiple Errors for Same Field Tests

    [Fact]
    public void Read_MultipleValidationFailures_FirstErrorUsed()
    {
        // Arrange - Value object that can have multiple validation errors
        var converter = new ValidatingJsonConverter<MultiValidationVO, string>();
        var json = "\"bad\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Test";

            // Act
            var result = converter.Read(ref reader, typeof(MultiValidationVO), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            // Only gets the first error (TryCreate returns on first failure)
            error!.Fields.Items.Should().ContainSingle();
        }
    }

    public class MultiValidationVO : ScalarValueObject<MultiValidationVO, string>, IScalarValue<MultiValidationVO, string>
    {
        private MultiValidationVO(string value) : base(value) { }
        public static Result<MultiValidationVO> TryCreate(string? value, string? fieldName = null)
        {
            if (string.IsNullOrEmpty(value))
                return Result.Fail<Asp.Tests.ValidatingJsonConverterEdgeCasesTests.MultiValidationVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Required" })));
            if (value.Length < 5)
                return Result.Fail<Asp.Tests.ValidatingJsonConverterEdgeCasesTests.MultiValidationVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Too short" })));
            if (!value.Contains('@'))
                return Result.Fail<Asp.Tests.ValidatingJsonConverterEdgeCasesTests.MultiValidationVO>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "field"), "validation.error") { Detail = "Must contain @" })));
            return Result.Ok(new MultiValidationVO(value));
        }
    }

    #endregion
}
