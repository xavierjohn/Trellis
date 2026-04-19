namespace Trellis.Asp.Tests;

using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;
using Trellis.Testing;

/// <summary>
/// Tests for PropertyNameAwareConverter to ensure proper property name tracking.
/// </summary>
public class PropertyNameAwareConverterTests
{
    #region Test Value Objects

    public class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
    {
        private Email(string value) : base(value) { }

        public static Result<Email> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.PropertyNameAwareConverterTests.Email>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email is required." })));
            if (!value.Contains('@'))
                return Result.Fail<Asp.Tests.PropertyNameAwareConverterTests.Email>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email must contain @." })));
            return Result.Ok(new Email(value));
        }
    }

    public class DomainOnlyEmail : ScalarValueObject<DomainOnlyEmail, string>, IScalarValue<DomainOnlyEmail, string>
    {
        private DomainOnlyEmail(string value) : base(value) { }

        public static Result<DomainOnlyEmail> TryCreate(string? value, string? fieldName = null)
        {
            if (value == "ok@example.com")
                return Result.Ok(new DomainOnlyEmail(value));

            return Result.Fail<Asp.Tests.PropertyNameAwareConverterTests.DomainOnlyEmail>(new Error.Conflict(null, "email.domain.invalid") { Detail = string.Empty });
        }
    }

    public class TestDto
    {
        public Email? Email { get; set; }
        public Email? BackupEmail { get; set; }
    }

    #endregion

    [Fact]
    public void Read_SetsPropertyNameInContext()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "UserEmail");

        var json = "\"test@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be("test@example.com");
        }
    }

    [Fact]
    public void Read_PropertyNameUsedInValidationErrors()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "PrimaryEmail");

        var json = "\"invalid\""; // No @
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Should().NotBeNull();
                        error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/PrimaryEmail", "wrapper should set property name");
        }
    }

    [Fact]
    public void Read_RestoresPreviousPropertyName()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "Email");

        var json = "\"test@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Set a property name before calling wrapper
            ValidationErrorsContext.CurrentPropertyName = "OuterProperty";

            // Act
            var result = wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            // Property name should be restored to previous value
            ValidationErrorsContext.CurrentPropertyName.Should().Be("OuterProperty");
        }
    }

    [Fact]
    public void Read_NestedPropertyNames_ProperlyRestored()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var outerWrapper = new PropertyNameAwareConverter<Email>(innerConverter, "Outer");
        var middleWrapper = new PropertyNameAwareConverter<Email>(outerWrapper, "Middle");
        var innerWrapper = new PropertyNameAwareConverter<Email>(middleWrapper, "Inner");

        var json = "\"test@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Initial";

            // Act - nested wrappers
            innerWrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert - should restore to initial
            ValidationErrorsContext.CurrentPropertyName.Should().Be("Initial");
        }
    }

    [Fact]
    public void Read_ExceptionInInnerConverter_StillRestoresPropertyName()
    {
        // Arrange
        var throwingConverter = new ThrowingConverter<Email>();
        var wrapper = new PropertyNameAwareConverter<Email>(throwingConverter, "Email");

        var json = "\"test@example.com\"";

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Previous";

            // Act & Assert
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
            reader.Read();

            try
            {
                wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());
                Assert.Fail("Should have thrown InvalidOperationException");
            }
            catch (InvalidOperationException)
            {
                // Expected - property name should still be restored
                ValidationErrorsContext.CurrentPropertyName.Should().Be("Previous");
            }
        }
    }

    [Fact]
    public void Read_NullPropertyNameBefore_NullAfter()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "Email");

        var json = "\"test@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Don't set CurrentPropertyName (null by default)

            // Act
            wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert - should be null again
            ValidationErrorsContext.CurrentPropertyName.Should().BeNull();
        }
    }

    [Fact]
    public void Write_DelegatesToInnerConverter()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "Email");

        var email = Email.TryCreate("test@example.com", null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        wrapper.Write(writer, email, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"test@example.com\"");
    }

    [Fact]
    public void Write_NullValue_WritesNull()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "Email");

        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        wrapper.Write(writer, null, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("null");
    }

    [Fact]
    public void Read_EmptyPropertyName_StillWorks()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, "");

        var json = "\"invalid\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Should().NotBeNull();
                        // Empty string should still be set as property name
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("");
        }
    }

    [Fact]
    public void Read_VeryLongPropertyName_HandledCorrectly()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var longName = new string('A', 1000); // 1000 character property name
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, longName);

        var json = "\"invalid\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/" + longName);
        }
    }

    [Fact]
    public void Read_SpecialCharactersInPropertyName_Preserved()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<Email, string>();
        var specialName = "Email.Address[0].Value";
        var wrapper = new PropertyNameAwareConverter<Email>(innerConverter, specialName);

        var json = "\"invalid\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = wrapper.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/" + specialName);
        }
    }

    [Fact]
    public void Read_NonValidationErrorWithoutDetail_UsesFallbackMessage()
    {
        // Arrange
        var innerConverter = new ValidatingJsonConverter<DomainOnlyEmail, string>();
        var wrapper = new PropertyNameAwareConverter<DomainOnlyEmail>(innerConverter, "Email");

        var json = "\"invalid@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = wrapper.Read(ref reader, typeof(DomainOnlyEmail), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Should().NotBeNull();
                        error!.Fields.Items.Should().ContainSingle();
            error.Fields.Items[0].Field.Path.Should().Be("/Email");
            error.Fields.Items[0].Detail.Should().Be("DomainOnlyEmail is invalid.");
        }
    }

    #region Helper Classes

    private class ThrowingConverter<T> : JsonConverter<T?>
        where T : class
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new InvalidOperationException("Test exception");

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("Test exception");
    }

    #endregion
}
