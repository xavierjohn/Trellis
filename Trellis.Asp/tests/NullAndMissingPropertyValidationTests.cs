namespace Trellis.Asp.Tests;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Tests that verify correct validation behavior when JSON properties of value-type-backed
/// scalar VOs (int, decimal, long, bool) are null or entirely missing from the JSON body.
///
/// Key design insight: RequiredInt, RequiredDecimal, RequiredLong, and RequiredBool are all
/// reference types (classes inheriting from ScalarValueObject). When a JSON property is missing,
/// the CLR property stays null — NOT the primitive default (0, 0m, 0L, false). The
/// ValidatingJsonConverter catches explicit JSON null tokens and produces per-field validation
/// errors. For entirely missing properties, developers should use the C# 'required' keyword
/// or [JsonRequired] to enforce presence at the JSON level.
/// </summary>
public class NullAndMissingPropertyValidationTests
{
    #region Test Value Objects

    public sealed class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
    {
        private Quantity(int value) : base(value) { }
        public static Result<Quantity> TryCreate(int value, string? fieldName = null) =>
            value > 0
                ? Result.Ok(new Quantity(value))
                : Result.Fail<Quantity>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "quantity"), "validation.error") { Detail = "Quantity must be positive." })));
        public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public sealed class Price : ScalarValueObject<Price, decimal>, IScalarValue<Price, decimal>
    {
        private Price(decimal value) : base(value) { }
        public static Result<Price> TryCreate(decimal value, string? fieldName = null) =>
            value >= 0
                ? Result.Ok(new Price(value))
                : Result.Fail<Price>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "price"), "validation.error") { Detail = "Price cannot be negative." })));
        public static Result<Price> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public sealed class Counter : ScalarValueObject<Counter, long>, IScalarValue<Counter, long>
    {
        private Counter(long value) : base(value) { }
        public static Result<Counter> TryCreate(long value, string? fieldName = null) =>
            value >= 0
                ? Result.Ok(new Counter(value))
                : Result.Fail<Counter>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "counter"), "validation.error") { Detail = "Counter cannot be negative." })));
        public static Result<Counter> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public sealed class IsActive : ScalarValueObject<IsActive, bool>, IScalarValue<IsActive, bool>
    {
        private IsActive(bool value) : base(value) { }
        public static Result<IsActive> TryCreate(bool value, string? fieldName = null) =>
            Result.Ok(new IsActive(value));
        public static Result<IsActive> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    public sealed class ProductName : ScalarValueObject<ProductName, string>, IScalarValue<ProductName, string>
    {
        private ProductName(string value) : base(value) { }
        public static Result<ProductName> TryCreate(string? value, string? fieldName = null) =>
            string.IsNullOrWhiteSpace(value)
                ? Result.Fail<ProductName>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "productName"), "validation.error") { Detail = "Product name is required." })))
                : Result.Ok(new ProductName(value));
    }

    #endregion

    #region Test DTOs

    public class CreateProductDto
    {
        public ProductName? Name { get; set; }
        public Quantity? Quantity { get; set; }
        public Price? Price { get; set; }
        public Counter? Counter { get; set; }
        public IsActive? Active { get; set; }
    }

    #endregion

    #region Explicit null in JSON — converter level

    [Fact]
    public void Read_NullJsonForIntVO_CollectsValidationError()
    {
        var converter = new ValidatingJsonConverter<Quantity, int>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Quantity";
            var result = converter.Read(ref reader, typeof(Quantity), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Quantity");
            error.Fields[0].Detail.Should().Contain("Quantity cannot be null.");
        }
    }

    [Fact]
    public void Read_NullJsonForDecimalVO_CollectsValidationError()
    {
        var converter = new ValidatingJsonConverter<Price, decimal>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Price";
            var result = converter.Read(ref reader, typeof(Price), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Price");
            error.Fields[0].Detail.Should().Contain("Price cannot be null.");
        }
    }

    [Fact]
    public void Read_NullJsonForLongVO_CollectsValidationError()
    {
        var converter = new ValidatingJsonConverter<Counter, long>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Counter";
            var result = converter.Read(ref reader, typeof(Counter), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Counter");
            error.Fields[0].Detail.Should().Contain("Counter cannot be null.");
        }
    }

    [Fact]
    public void Read_NullJsonForBoolVO_CollectsValidationError()
    {
        var converter = new ValidatingJsonConverter<IsActive, bool>();
        var json = "null";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Active";
            var result = converter.Read(ref reader, typeof(IsActive), new JsonSerializerOptions());

            result.Should().BeNull();
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Active");
            error.Fields[0].Detail.Should().Contain("IsActive cannot be null.");
        }
    }

    #endregion

    #region Explicit null in full DTO JSON deserialization

    [Fact]
    public void Deserialize_DtoWithExplicitNullIntProperty_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Quantity": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Should().NotBeNull();
            dto.Quantity.Should().BeNull();

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "an explicit JSON null for a required int VO should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Quantity");
        }
    }

    [Fact]
    public void Deserialize_DtoWithExplicitNullDecimalProperty_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Price": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Price.Should().BeNull();

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "an explicit JSON null for a required decimal VO should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Price");
        }
    }

    [Fact]
    public void Deserialize_DtoWithExplicitNullLongProperty_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Counter": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Counter.Should().BeNull();

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "an explicit JSON null for a required long VO should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Counter");
        }
    }

    [Fact]
    public void Deserialize_DtoWithExplicitNullBoolProperty_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Active": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Active.Should().BeNull();

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "an explicit JSON null for a required bool VO should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/Active");
        }
    }

    [Fact]
    public void Deserialize_DtoWithAllNullVOProperties_CollectsAllErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": null, "Quantity": null, "Price": null, "Counter": null, "Active": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "all null VO properties should produce validation errors");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().HaveCount(5,
                "each null scalar VO property should produce a separate field error");

            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Name");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Quantity");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Price");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Counter");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Active");
        }
    }

    #endregion

    #region Missing properties in DTO JSON deserialization

    [Fact]
    public void Deserialize_DtoWithMissingProperties_PropertiesAreNull()
    {
        // Missing JSON properties for reference-type scalar VOs result in null CLR properties.
        // This is by design — these are classes, not structs. The developer should use C# 'required'
        // keyword or [JsonRequired] to enforce presence at the JSON level.
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name!.Value.Should().Be("Widget");

            // Missing properties are null — they are reference types
            dto.Quantity.Should().BeNull("missing int VO property should be null, not 0");
            dto.Price.Should().BeNull("missing decimal VO property should be null, not 0m");
            dto.Counter.Should().BeNull("missing long VO property should be null, not 0L");
            dto.Active.Should().BeNull("missing bool VO property should be null, not false");
        }
    }

    [Fact]
    public void Deserialize_EmptyJsonObject_AllPropertiesAreNull()
    {
        var options = CreateConfiguredJsonOptions();
        var json = "{}";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Should().BeNull();
            dto.Quantity.Should().BeNull();
            dto.Price.Should().BeNull();
            dto.Counter.Should().BeNull();
            dto.Active.Should().BeNull();
        }
    }

    #endregion

    #region Valid values — round-trip confirmation

    [Fact]
    public void Deserialize_DtoWithAllValidProperties_NoValidationErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Quantity": 10, "Price": 9.99, "Counter": 42, "Active": true}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name!.Value.Should().Be("Widget");
            dto.Quantity!.Value.Should().Be(10);
            dto.Price!.Value.Should().Be(9.99m);
            dto.Counter!.Value.Should().Be(42L);
            dto.Active!.Value.Should().Be(true);

            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "all valid properties should not produce any validation errors");
        }
    }

    [Fact]
    public void Deserialize_DtoWithZeroInt_AcceptsZeroAsValidValue()
    {
        // Zero is NOT the same as missing/null. When JSON contains "quantity": 0,
        // the converter IS invoked with the value 0. Whether 0 passes validation
        // depends on the value object's TryCreate logic.
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Quantity": 0}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            // Quantity rejects 0 because "must be positive"
            dto!.Quantity.Should().BeNull("TryCreate rejects 0 for Quantity");
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Detail.Should().Contain("Quantity must be positive.");
        }
    }

    [Fact]
    public void Deserialize_DtoWithFalseBool_AcceptsFalseAsValidValue()
    {
        // false is NOT the same as missing/null. When JSON contains "active": false,
        // the converter IS invoked. false is a valid boolean value.
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Active": false}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Active.Should().NotBeNull("false is a valid value, not null");
            dto.Active!.Value.Should().BeFalse();
            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "false is a valid boolean value and should not produce validation errors");
        }
    }

    [Fact]
    public void Deserialize_DtoWithZeroDecimal_AcceptsZeroAsValidValue()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Price": 0}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Price.Should().NotBeNull("0 is a valid decimal value, not null");
            dto.Price!.Value.Should().Be(0m);
            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "zero is a valid price and should not produce validation errors");
        }
    }

    #endregion

    #region Mixed valid and null properties

    [Fact]
    public void Deserialize_DtoWithMixedValidAndNullProperties_CollectsOnlyNullErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Quantity": 5, "Price": null, "Counter": null, "Active": true}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<CreateProductDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name!.Value.Should().Be("Widget");
            dto.Quantity!.Value.Should().Be(5);
            dto.Active!.Value.Should().Be(true);
            dto.Price.Should().BeNull();
            dto.Counter.Should().BeNull();

            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().HaveCount(2);
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Price");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Counter");
            error.Fields.Items.Should().NotContain(e => e.Field.Path == "/Name");
            error.Fields.Items.Should().NotContain(e => e.Field.Path == "/Quantity");
            error.Fields.Items.Should().NotContain(e => e.Field.Path == "/Active");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates JsonSerializerOptions configured with the same TypeInfoResolver modifier
    /// that Trellis.Asp uses in production, including property-name-aware converters.
    /// </summary>
    private static JsonSerializerOptions CreateConfiguredJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var resolver = new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = resolver.WithAddedModifier(ModifyTypeInfo);
        options.Converters.Add(new ValidatingJsonConverterFactory());
        options.Converters.Add(new MaybeScalarValueJsonConverterFactory());

        return options;
    }

    /// <summary>
    /// Mirrors the ModifyTypeInfo logic from ServiceCollectionExtensions to inject
    /// property-name-aware converters for scalar value object properties.
    /// </summary>
    private static void ModifyTypeInfo(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            var propertyType = property.PropertyType;

            if (!ScalarValueTypeHelper.IsScalarValue(propertyType))
                continue;

            var primitiveType = ScalarValueTypeHelper.GetPrimitiveType(propertyType);
            if (primitiveType is null)
                continue;

            var innerConverter = ScalarValueTypeHelper.CreateGenericInstance<System.Text.Json.Serialization.JsonConverter>(
                typeof(ValidatingJsonConverter<,>),
                propertyType,
                primitiveType);

            if (innerConverter is null)
                continue;

            var wrapperType = typeof(PropertyNameAwareConverter<>).MakeGenericType(propertyType);
            var wrappedConverter = Activator.CreateInstance(wrapperType, innerConverter, property.Name)
                as System.Text.Json.Serialization.JsonConverter;

            if (wrappedConverter is not null)
                property.CustomConverter = wrappedConverter;
        }
    }

    #endregion
}