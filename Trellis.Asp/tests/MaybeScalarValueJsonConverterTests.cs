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
/// Tests for <see cref="MaybeScalarValueJsonConverter{TValue, TPrimitive}"/> and
/// <see cref="MaybeScalarValueJsonConverterFactory"/>.
/// Validates JSON serialization/deserialization of <see cref="Maybe{T}"/> wrapping scalar value objects.
/// </summary>
public class MaybeScalarValueJsonConverterTests
{
    #region Test Value Objects

    public class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
    {
        private Email(string value) : base(value) { }

        public static Result<Email> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.Email>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Email is required." })));
            if (!value.Contains('@'))
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.Email>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Email must contain @." })));
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
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.Age>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Age cannot be negative." })));
            if (value > 150)
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.Age>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Age must be realistic." })));
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
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.Percentage>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Percentage cannot be negative." })));
            if (value > 100)
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.Percentage>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Percentage cannot exceed 100." })));
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
                return Result.Fail<Asp.Tests.MaybeScalarValueJsonConverterTests.ItemId>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "ItemId cannot be empty." })));
            return Result.Ok(new ItemId(value));
        }

        public static Result<ItemId> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
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
                ? Result.Fail<ProcessingModeVO>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "processingMode"), ValidationCodes.Unspecified) { Detail = "Processing mode is required." })))
                : Result.Ok(new ProcessingModeVO(value));

        public static Result<ProcessingModeVO> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region Read - Enum Value Object

    [Fact]
    public void Read_EnumString_ReturnsMaybeWithValue()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<ProcessingModeVO, ProcessingMode>();
        var json = "\"Safe\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "mode";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<ProcessingModeVO>), new JsonSerializerOptions());

            // Assert
            result.HasValue.Should().BeTrue();
            result.Value.Value.Should().Be(ProcessingMode.Safe);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    #endregion

    #region Read — String Value Object

    [Fact]
    public void Read_ValidString_ReturnsMaybeWithValue()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var json = "\"test@example.com\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Email>), new JsonSerializerOptions());

            // Assert
            result.HasValue.Should().BeTrue();
            result.Value.Value.Should().Be("test@example.com");
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_NullString_ReturnsMaybeNoneWithoutError()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Email>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue("null JSON should produce Maybe.None without errors");
            ValidationErrorsContext.HasErrors.Should().BeFalse("null is valid for Maybe — it means 'not provided'");
        }
    }

    [Fact]
    public void Read_InvalidString_CollectsErrorAndReturnsDefault()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var json = "\"invalid\""; // Missing @
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Email>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue("invalid value returns default (None)");
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
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var json = "\"\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Email>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    #endregion

    #region Read — Int Value Object

    [Fact]
    public void Read_ValidInt_ReturnsMaybeWithValue()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Age, int>();
        var json = "25";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Age>), new JsonSerializerOptions());

            // Assert
            result.HasValue.Should().BeTrue();
            result.Value.Value.Should().Be(25);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_InvalidInt_CollectsError()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Age, int>();
        var json = "-5";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Age>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Detail.Should().Be("Age cannot be negative.");
        }
    }

    [Fact]
    public void Read_NullInt_ReturnsMaybeNoneWithoutError()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Age, int>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Age";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Age>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue();
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    #endregion

    #region Read — Decimal Value Object

    [Fact]
    public void Read_ValidDecimal_ReturnsMaybeWithValue()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Percentage, decimal>();
        var json = "75.5";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Percentage";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Percentage>), new JsonSerializerOptions());

            // Assert
            result.HasValue.Should().BeTrue();
            result.Value.Value.Should().Be(75.5m);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_InvalidDecimal_CollectsError()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Percentage, decimal>();
        var json = "150.0";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Percentage";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<Percentage>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    #endregion

    #region Read — Guid Value Object

    [Fact]
    public void Read_ValidGuid_ReturnsMaybeWithValue()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<ItemId, Guid>();
        var guid = Guid.NewGuid();
        var json = $"\"{guid}\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "ItemId";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<ItemId>), new JsonSerializerOptions());

            // Assert
            result.HasValue.Should().BeTrue();
            result.Value.Value.Should().Be(guid);
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    [Fact]
    public void Read_EmptyGuid_CollectsError()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<ItemId, Guid>();
        var json = $"\"{Guid.Empty}\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "ItemId";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<ItemId>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields[0].Detail.Should().Be("ItemId cannot be empty.");
        }
    }

    [Fact]
    public void Read_NullGuid_ReturnsMaybeNoneWithoutError()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<ItemId, Guid>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "ItemId";

            // Act
            var result = converter.Read(ref reader, typeof(Maybe<ItemId>), new JsonSerializerOptions());

            // Assert
            result.HasNoValue.Should().BeTrue();
            ValidationErrorsContext.HasErrors.Should().BeFalse();
        }
    }

    #endregion

    #region Write Tests

    [Fact]
    public void Write_MaybeWithStringValue_WritesString()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var email = Email.TryCreate("test@example.com", null).Unwrap();
        var maybe = Maybe.From(email);
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, maybe, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"test@example.com\"");
    }

    [Fact]
    public void Write_MaybeNoneString_WritesNull()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        Maybe<Email> maybe = default;
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, maybe, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("null");
    }

    [Fact]
    public void Write_MaybeWithIntValue_WritesNumber()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Age, int>();
        var age = Age.TryCreate(42, null).Unwrap();
        var maybe = Maybe.From(age);
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, maybe, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("42");
    }

    [Fact]
    public void Write_MaybeNoneInt_WritesNull()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Age, int>();
        Maybe<Age> maybe = default;
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, maybe, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("null");
    }

    [Fact]
    public void Write_MaybeWithDecimalValue_WritesNumber()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Percentage, decimal>();
        var percentage = Percentage.TryCreate(99.99m, null).Unwrap();
        var maybe = Maybe.From(percentage);
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, maybe, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("99.99");
    }

    [Fact]
    public void Write_MaybeWithGuidValue_WritesString()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<ItemId, Guid>();
        var guid = Guid.NewGuid();
        var itemId = ItemId.TryCreate(guid, null).Unwrap();
        var maybe = Maybe.From(itemId);
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        converter.Write(writer, maybe, new JsonSerializerOptions());
        writer.Flush();

        // Assert
        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be($"\"{guid}\"");
    }

    #endregion

    #region Round-Trip Tests

    private static readonly JsonSerializerOptions s_roundTripOptions = CreateRoundTripOptions();

    private static JsonSerializerOptions CreateRoundTripOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new MaybeScalarValueJsonConverterFactory());
        return options;
    }

    [Fact]
    public void RoundTrip_MaybeEmail_PreservesValue()
    {
        // Arrange
        var email = Email.TryCreate("user@domain.com", null).Unwrap();
        var maybe = Maybe.From(email);

        // Act
        var json = JsonSerializer.Serialize(maybe, s_roundTripOptions);
        Maybe<Email> deserialized;

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";
            deserialized = JsonSerializer.Deserialize<Maybe<Email>>(json, s_roundTripOptions);
        }

        // Assert
        deserialized.HasValue.Should().BeTrue();
        deserialized.Value.Value.Should().Be(email.Value);
    }

    [Fact]
    public void RoundTrip_MaybeNone_PreservesNone()
    {
        // Arrange
        Maybe<Email> maybe = default;

        // Act
        var json = JsonSerializer.Serialize(maybe, s_roundTripOptions);
        Maybe<Email> deserialized;

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Email";
            deserialized = JsonSerializer.Deserialize<Maybe<Email>>(json, s_roundTripOptions);
        }

        // Assert
        json.Should().Be("null");
        deserialized.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Error Handling Without Scope

    [Fact]
    public void Read_InvalidValueWithoutScope_ReturnsDefault()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var json = "\"invalid\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        // No scope active

        // Act
        var result = converter.Read(ref reader, typeof(Maybe<Email>), new JsonSerializerOptions());

        // Assert
        result.HasNoValue.Should().BeTrue("returns default without throwing when no scope is active");
    }

    [Fact]
    public void Read_NullWithoutScope_ReturnsDefault()
    {
        // Arrange
        var converter = new MaybeScalarValueJsonConverter<Email, string>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        // No scope active

        // Act
        var result = converter.Read(ref reader, typeof(Maybe<Email>), new JsonSerializerOptions());

        // Assert
        result.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Factory Tests

    [Fact]
    public void Factory_CanConvert_MaybeEmail_ReturnsTrue()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var result = factory.CanConvert(typeof(Maybe<Email>));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Factory_CanConvert_MaybeAge_ReturnsTrue()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var result = factory.CanConvert(typeof(Maybe<Age>));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Factory_CanConvert_DirectEmail_ReturnsFalse()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var result = factory.CanConvert(typeof(Email));

        // Assert
        result.Should().BeFalse("direct value objects are handled by ValidatingJsonConverterFactory");
    }

    [Fact]
    public void Factory_CanConvert_MaybeString_ReturnsFalse()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var result = factory.CanConvert(typeof(Maybe<string>));

        // Assert
        result.Should().BeFalse("string is not a scalar value object");
    }

    [Theory]
    [InlineData(typeof(Maybe<int>))]
    [InlineData(typeof(Maybe<long>))]
    [InlineData(typeof(Maybe<Guid>))]
    [InlineData(typeof(Maybe<DateTime>))]
    [InlineData(typeof(Maybe<DateTimeOffset>))]
    [InlineData(typeof(Maybe<bool>))]
    [InlineData(typeof(Maybe<decimal>))]
    public void Factory_CanConvert_MaybePrimitive_ReturnsFalse(Type maybePrimitive)
    {
        // Documents the boundary: MaybeScalarValueJsonConverterFactory only handles
        // Maybe<T> where T implements IScalarValue<,>. Raw C# primitives never satisfy
        // that constraint. A user who declares Maybe<int> / Maybe<Guid> / etc. on a DTO
        // gets no Trellis converter — see the "Maybe<TPrimitive>" pattern in maybe-type.md:
        // use `T?` on the DTO and `.AsMaybe()` at the endpoint/API seam, or wrap the
        // primitive in a scalar value object (e.g., `Age : RequiredInt<Age>`).
        var factory = new MaybeScalarValueJsonConverterFactory();

        factory.CanConvert(maybePrimitive)
            .Should().BeFalse("primitive inner types do not implement IScalarValue<,>");
    }

    [Fact]
    public void Factory_CanConvert_PlainInt_ReturnsFalse()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var result = factory.CanConvert(typeof(int));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Factory_CreateConverter_MaybeEmail_ReturnsConverter()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Maybe<Email>), new JsonSerializerOptions());

        // Assert
        converter.Should().NotBeNull();
        converter.Should().BeOfType<MaybeScalarValueJsonConverter<Email, string>>();
    }

    [Fact]
    public void Factory_CreateConverter_MaybeAge_ReturnsConverter()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Maybe<Age>), new JsonSerializerOptions());

        // Assert
        converter.Should().NotBeNull();
        converter.Should().BeOfType<MaybeScalarValueJsonConverter<Age, int>>();
    }

    [Fact]
    public void Factory_CreateConverter_MaybePercentage_ReturnsConverter()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Maybe<Percentage>), new JsonSerializerOptions());

        // Assert
        converter.Should().NotBeNull();
        converter.Should().BeOfType<MaybeScalarValueJsonConverter<Percentage, decimal>>();
    }

    [Fact]
    public void Factory_CreateConverter_MaybeItemId_ReturnsConverter()
    {
        // Arrange
        var factory = new MaybeScalarValueJsonConverterFactory();

        // Act
        var converter = factory.CreateConverter(typeof(Maybe<ItemId>), new JsonSerializerOptions());

        // Assert
        converter.Should().NotBeNull();
        converter.Should().BeOfType<MaybeScalarValueJsonConverter<ItemId, Guid>>();
    }

    #endregion

    #region DTO Integration Tests

    public record TestDto
    {
        public Email RequiredEmail { get; init; } = null!;
        public Maybe<Age> OptionalAge { get; init; }
    }

    private static readonly JsonSerializerOptions s_dtoOptions = CreateDtoOptions();

    private static JsonSerializerOptions CreateDtoOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValidatingJsonConverterFactory());
        options.Converters.Add(new MaybeScalarValueJsonConverterFactory());
        return options;
    }

    [Fact]
    public void Deserialize_Dto_WithMaybePresent_BindsCorrectly()
    {
        // Arrange
        var json = """{"RequiredEmail":"user@test.com","OptionalAge":30}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var dto = JsonSerializer.Deserialize<TestDto>(json, s_dtoOptions);

            // Assert
            ValidationErrorsContext.HasErrors.Should().BeFalse();
            dto.Should().NotBeNull();
            dto!.RequiredEmail.Value.Should().Be("user@test.com");
            dto.OptionalAge.HasValue.Should().BeTrue();
            dto.OptionalAge.Value.Value.Should().Be(30);
        }
    }

    [Fact]
    public void Deserialize_Dto_WithMaybeNull_BindsMaybeNone()
    {
        // Arrange
        var json = """{"RequiredEmail":"user@test.com","OptionalAge":null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var dto = JsonSerializer.Deserialize<TestDto>(json, s_dtoOptions);

            // Assert
            ValidationErrorsContext.HasErrors.Should().BeFalse();
            dto.Should().NotBeNull();
            dto!.RequiredEmail.Value.Should().Be("user@test.com");
            dto.OptionalAge.HasNoValue.Should().BeTrue();
        }
    }

    [Fact]
    public void Deserialize_Dto_WithMaybeOmitted_DefaultsToNone()
    {
        // Arrange
        var json = """{"RequiredEmail":"user@test.com"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var dto = JsonSerializer.Deserialize<TestDto>(json, s_dtoOptions);

            // Assert
            ValidationErrorsContext.HasErrors.Should().BeFalse();
            dto.Should().NotBeNull();
            dto!.OptionalAge.HasNoValue.Should().BeTrue("omitted property defaults to Maybe.None");
        }
    }

    [Fact]
    public void Deserialize_Dto_WithMaybeInvalid_CollectsError()
    {
        // Arrange
        var json = """{"RequiredEmail":"user@test.com","OptionalAge":-5}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var dto = JsonSerializer.Deserialize<TestDto>(json, s_dtoOptions);

            // Assert
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            dto.Should().NotBeNull();
            dto!.OptionalAge.HasNoValue.Should().BeTrue();
        }
    }

    #endregion

    #region Recommended `Maybe<TPrimitive>` DTO pattern: `T?` on the wire, `.AsMaybe()` at the seam

    // The framework ships no JSON converter for arbitrary primitive Maybe<T> (Maybe<int>,
    // Maybe<string>, Maybe<Guid>, etc.) — `MaybeScalarValueJsonConverterFactory` rejects
    // these inner types because they do not implement `IScalarValue<,>` (see the boundary
    // tests earlier in this file). The supported pattern (documented in maybe-type.md
    // §"Interop with nullable" and "Optional nullable primitives on DTOs, keep using T?")
    // is: declare the DTO property as `T?`, then lift to `Maybe<T>` at the endpoint/API
    // seam via `Trellis.AsMaybeExtensions.AsMaybe()`. These tests document that pattern
    // end-to-end so future framework changes cannot drift it silently.

    private static readonly JsonSerializerOptions OptionalDtoOptions = new();

    public sealed record AgeOptionalRequest(int? Age);

    public sealed record NicknameOptionalRequest(string? Nickname);

    public sealed record OrderIdOptionalRequest(Guid? OrderId);

    [Fact]
    public void NullableInt_PresentValue_RoundTripsAndLiftsToMaybeSome()
    {
        // Wire shape: `int?` on the DTO. Lifted to Maybe<int> at the seam.
        var dto = JsonSerializer.Deserialize<AgeOptionalRequest>("""{"Age":42}""", OptionalDtoOptions);

        dto.Should().NotBeNull();
        dto!.Age.Should().Be(42);
        dto.Age.AsMaybe().Should().HaveValueEqualTo(42);
    }

    [Fact]
    public void NullableInt_NullValue_LiftsToMaybeNone()
    {
        var dto = JsonSerializer.Deserialize<AgeOptionalRequest>("""{"Age":null}""", OptionalDtoOptions);

        dto.Should().NotBeNull();
        dto!.Age.Should().BeNull();
        dto.Age.AsMaybe().Should().BeNone();
    }

    [Fact]
    public void NullableInt_MissingProperty_LiftsToMaybeNone()
    {
        // The wire shape allows the property to be entirely absent; `int?` still
        // deserializes to null, lifting to None.
        var dto = JsonSerializer.Deserialize<AgeOptionalRequest>("""{}""", OptionalDtoOptions);

        dto.Should().NotBeNull();
        dto!.Age.Should().BeNull();
        dto.Age.AsMaybe().Should().BeNone();
    }

    [Fact]
    public void NullableInt_RoundTripsBackToJson()
    {
        var json = JsonSerializer.Serialize(new AgeOptionalRequest(42), OptionalDtoOptions);

        json.Should().Contain("\"Age\":42");
    }

    [Fact]
    public void NullableInt_NoneRoundTripsBackAsJsonNull()
    {
        // A handler that started with Maybe<int>.None converts back to int? via
        // AsNullable() for response shaping; JSON serializes null as the JSON null literal.
        var maybe = Maybe<int>.None;
        var nullable = maybe.AsNullable();

        var json = JsonSerializer.Serialize(new AgeOptionalRequest(nullable), OptionalDtoOptions);

        json.Should().Contain("\"Age\":null");
    }

    [Fact]
    public void NullableString_PresentValue_LiftsToMaybeSome()
    {
        // Same pattern works for reference-typed primitives. AsMaybe() handles
        // `string?` through Maybe.From's nullable overload.
        var dto = JsonSerializer.Deserialize<NicknameOptionalRequest>(
            """{"Nickname":"ada"}""", OptionalDtoOptions);

        var lifted = Maybe.From(dto!.Nickname);
        lifted.Should().HaveValueEqualTo("ada");
    }

    [Fact]
    public void NullableString_NullValue_LiftsToMaybeNone()
    {
        var dto = JsonSerializer.Deserialize<NicknameOptionalRequest>(
            """{"Nickname":null}""", OptionalDtoOptions);

        var lifted = Maybe.From(dto!.Nickname);
        lifted.Should().BeNone();
    }

    [Fact]
    public void NullableGuid_PresentValue_LiftsToMaybeSome()
    {
        // Demonstrates the recommended pattern is uniform across primitive value-types
        // — it does not depend on the inner type being a Trellis scalar value object.
        var guid = Guid.Parse("019e1942-1a4a-796c-856e-95f8996eacb3");

        var dto = JsonSerializer.Deserialize<OrderIdOptionalRequest>(
            $$"""{"OrderId":"{{guid}}"}""", OptionalDtoOptions);

        dto!.OrderId.AsMaybe().Should().HaveValueEqualTo(guid);
    }

    [Fact]
    public void MaybeIntDirectlyOnDto_HasNoFrameworkConverter_DocumentsTheGap()
    {
        // Negative-symptom regression guard. If a future change ever ships a factory
        // for arbitrary primitive Maybe<>, this assertion will flip and the regression
        // guard fires — at which point the cookbook + maybe-type.md guidance ("use T?
        // for primitives") needs to be updated.
        var factory = new MaybeScalarValueJsonConverterFactory();

        factory.CanConvert(typeof(Maybe<int>))
            .Should().BeFalse("primitive Maybe<int> is not factory-handled; use `int?` + `.AsMaybe()` on DTOs instead");
    }

    #endregion
}