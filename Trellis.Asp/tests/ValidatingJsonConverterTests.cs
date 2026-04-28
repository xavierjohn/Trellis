namespace Trellis.Asp.Tests;

using System;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Trellis.Testing;
using Xunit;

/// <summary>
/// Direct tests for ValidatingJsonConverter to ensure proper JSON serialization/deserialization.
/// </summary>
public class ValidatingJsonConverterTests
{
    #region Test Value Objects

    public class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
    {
        private Email(string value) : base(value) { }

        public static Result<Email> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.Email>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email is required." })));
            if (!value.Contains('@'))
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.Email>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email must contain @." })));
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
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.Age>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Age cannot be negative." })));
            if (value > 150)
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.Age>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Age must be realistic." })));
            return Result.Ok(new Age(value));
        }
        public static Result<Age> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class Percentage : ScalarValueObject<Percentage, decimal>, IScalarValue<Percentage, decimal>
    {
        private Percentage(decimal value) : base(value) { }

        public static Result<Percentage> TryCreate(decimal value, string? fieldName = null)
        {
            var field = fieldName ?? "percentage";
            if (value < 0)
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.Percentage>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Percentage cannot be negative." })));
            if (value > 100)
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.Percentage>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Percentage cannot exceed 100." })));
            return Result.Ok(new Percentage(value));
        }
        public static Result<Percentage> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public class ItemId : ScalarValueObject<ItemId, Guid>, IScalarValue<ItemId, Guid>
    {
        private ItemId(Guid value) : base(value) { }

        public static Result<ItemId> TryCreate(Guid value, string? fieldName = null)
        {
            var field = fieldName ?? "itemId";
            if (value == Guid.Empty)
                return Result.Fail<Asp.Tests.ValidatingJsonConverterTests.ItemId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "ItemId cannot be empty." })));
            return Result.Ok(new ItemId(value));
        }
        public static Result<ItemId> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region String Value Object Tests

    [Fact]
    public void Read_ValidString_ReturnsValueObject()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"test@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be("test@example.com");
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_InvalidString_CollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"invalid\""; // Missing @
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Field.Path.Should().Be("/Email");
            error.Fields[0].Detail.Should().Be("Email must contain @.");
        }
    }

    [Fact]
    public void Read_EmptyString_CollectsError()
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
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public void Read_NullString_ReturnsNullAndCollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue("null values should produce a validation error for required value objects");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Email");
            error.Fields[0].Detail.Should().Contain("Email cannot be null.");
        }
    }

    [Fact]
    public void Write_ValidValueObject_WritesString()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var email = Email.TryCreate("test@example.com", null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, email, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"test@example.com\"");
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

    #region Int Value Object Tests

    [Fact]
    public void Read_ValidInt_ReturnsValueObject()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = "25";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be(25);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_InvalidInt_CollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = "-5"; // Negative age
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Detail.Should().Be("Age cannot be negative.");
        }
    }

    [Fact]
    public void Read_IntOutOfRange_CollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var json = "200"; // Unrealistic age
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Age), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public void Write_IntValueObject_WritesNumber()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Age, int>();
        var age = Age.TryCreate(42, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, age, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("42");
    }

    #endregion

    #region Decimal Value Object Tests

    [Fact]
    public void Read_ValidDecimal_ReturnsValueObject()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Percentage, decimal>();
        var json = "75.5";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Percentage";

            // Act
            var result = converter.Read(ref reader, typeof(Percentage), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be(75.5m);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_DecimalBelowRange_CollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Percentage, decimal>();
        var json = "-10.5";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Percentage";

            // Act
            var result = converter.Read(ref reader, typeof(Percentage), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public void Read_DecimalAboveRange_CollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Percentage, decimal>();
        var json = "150.0";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Percentage";

            // Act
            var result = converter.Read(ref reader, typeof(Percentage), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    [Fact]
    public void Write_DecimalValueObject_WritesNumber()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Percentage, decimal>();
        var percentage = Percentage.TryCreate(99.99m, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, percentage, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("99.99");
    }

    #endregion

    #region Guid Value Object Tests

    [Fact]
    public void Read_ValidGuid_ReturnsValueObject()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<ItemId, Guid>();
        var guid = Guid.NewGuid();
        var json = $"\"{guid}\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "ItemId";

            // Act
            var result = converter.Read(ref reader, typeof(ItemId), new JsonSerializerOptions());

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be(guid);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_EmptyGuid_CollectsError()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<ItemId, Guid>();
        var json = $"\"{Guid.Empty}\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "ItemId";

            // Act
            var result = converter.Read(ref reader, typeof(ItemId), new JsonSerializerOptions());

            // Assert
            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Detail.Should().Be("ItemId cannot be empty.");
        }
    }

    [Fact]
    public void Write_GuidValueObject_WritesString()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<ItemId, Guid>();
        var guid = Guid.NewGuid();
        var itemId = ItemId.TryCreate(guid, null).Unwrap();
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, itemId, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be($"\"{guid}\"");
    }

    #endregion

    #region Error Handling Without Scope

    [Fact]
    public void Read_InvalidValueWithoutScope_ReturnsNull()
    {
        // Arrange
        var converter = new ValidatingJsonConverter<Email, string>();
        var json = "\"invalid\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        // No scope active

        // Act
        var result = converter.Read(ref reader, typeof(Email), new JsonSerializerOptions());

        // Assert
        result.Should().BeNull(); // Returns null without throwing
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_Email_PreservesValue()
    {
        // Arrange
        var email = Email.TryCreate("user@domain.com", null).Unwrap();
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValidatingJsonConverterFactory());

        // Act
        var json = JsonSerializer.Serialize(email, options);
        Email? deserialized;

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";
            deserialized = JsonSerializer.Deserialize<Email>(json, options);
        }

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be(email.Value);
    }

    [Fact]
    public void RoundTrip_Age_PreservesValue()
    {
        // Arrange
        var age = Age.TryCreate(30, null).Unwrap();
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValidatingJsonConverterFactory());

        // Act
        var json = JsonSerializer.Serialize(age, options);
        Age? deserialized;

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";
            deserialized = JsonSerializer.Deserialize<Age>(json, options);
        }

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be(age.Value);
    }

    #endregion
}