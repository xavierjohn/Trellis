namespace Trellis.Asp.Tests;

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Tests that verify positional record scalar value properties are validated via the
/// OnDeserialized-based null check, not by wiring converters to constructor parameters.
/// Positional records like <c>record Dto(ProductName Name)</c> are still deserialized via
/// constructor parameters by System.Text.Json, but validation is enforced after materialization
/// by detecting missing required properties. Explicit <c>null</c> tokens are handled by the
/// converter path; the OnDeserialized callback skips fields that already have a collected error
/// so that validation errors are not reported twice.
///
/// Bug: positional records previously bypassed scalar value validation — constructor parameters
/// were silently null when the JSON property was missing, with no validation error collected.
/// These tests guard the post-deserialization missing-property detection and duplicate
/// suppression for explicit <c>null</c>.
/// </summary>
public class PositionalRecordValidationTests
{
    #region Test Value Objects

    public sealed class ProductName : ScalarValueObject<ProductName, string>, IScalarValue<ProductName, string>
    {
        private ProductName(string value) : base(value) { }
        public static Result<ProductName> TryCreate(string? value, string? fieldName = null) =>
            string.IsNullOrWhiteSpace(value)
                ? Result.Fail<ProductName>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "productName"), "validation.error") { Detail = "Product name is required." })))
                : Result.Ok(new ProductName(value));
    }

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

    #endregion

    #region Test DTOs — Positional vs Property Syntax

    /// <summary>Positional record — deserialized via constructor parameters.</summary>
    public record PositionalDto(ProductName Name, Quantity Quantity, Price Price);

    /// <summary>Property-syntax record — deserialized via property setters.</summary>
    public record PropertyDto
    {
        public ProductName Name { get; init; } = null!;
        public Quantity Quantity { get; init; } = null!;
        public Price Price { get; init; } = null!;
    }

    /// <summary>Positional record with a single string value object.</summary>
    public record SinglePositionalDto(ProductName Name);

    /// <summary>Mixed: one VO constructor param + one plain primitive.</summary>
    public record MixedPositionalDto(ProductName Name, int Count);

    /// <summary>Positional record with a nullable VO — absence is intentional.</summary>
    public record NullablePositionalDto(ProductName Name, Price? OptionalPrice);

    /// <summary>Property-syntax record with a nullable VO — absence is intentional.</summary>
    public record NullablePropertyDto
    {
        public ProductName Name { get; init; } = null!;
        public Price? OptionalPrice { get; init; }
    }

    #endregion

    #region Explicit null — positional record should collect validation errors

    [Fact]
    public void Deserialize_PositionalRecord_ExplicitNullStringVO_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": null, "Quantity": 5, "Price": 9.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "explicit JSON null for a required string VO in a positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            var fieldError = error!.Fields.Items.Should().ContainSingle(e => e.Field.Path == "/name").Subject;
            fieldError.Detail.Should().Contain("ProductName cannot be null.",
                "explicit null should be caught by the converter, not the OnDeserialized fallback");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_ExplicitNullIntVO_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Quantity": null, "Price": 9.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "explicit JSON null for a required int VO in a positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            var fieldError = error!.Fields.Items.Should().ContainSingle(e => e.Field.Path == "/quantity").Subject;
            fieldError.Detail.Should().Contain("Quantity cannot be null.",
                "explicit null should be caught by the converter, not the OnDeserialized fallback");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_AllNullVOs_CollectsAllErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": null, "Quantity": null, "Price": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "all null VO properties in a positional record should produce validation errors");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().HaveCount(3,
                "each null scalar VO constructor parameter should produce a separate field error");
            error.Fields.Items.Should().Contain(e =>
                e.Field.Path == "/name" && e.Detail!.Contains("ProductName cannot be null."));
            error.Fields.Items.Should().Contain(e =>
                e.Field.Path == "/quantity" && e.Detail!.Contains("Quantity cannot be null."));
            error.Fields.Items.Should().Contain(e =>
                e.Field.Path == "/price" && e.Detail!.Contains("Price cannot be null."));
        }
    }

    #endregion

    #region Missing properties — positional record should collect validation errors

    [Fact]
    public void Deserialize_PositionalRecord_MissingStringVO_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Quantity": 5, "Price": 9.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            // Name is missing entirely — the constructor parameter should not silently be null
            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "a missing required string VO in a positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().Contain(e => e.Field.Path == "/name");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_MissingIntVO_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Price": 9.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "a missing required int VO in a positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().Contain(e => e.Field.Path == "/quantity");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_EmptyJsonObject_CollectsAllErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = "{}";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "an empty JSON object for a positional record with required VOs should produce validation errors");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().HaveCount(3);
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/name");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/quantity");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/price");
        }
    }

    #endregion

    #region Valid values — positional record should work identically to property syntax

    [Fact]
    public void Deserialize_PositionalRecord_AllValidValues_NoValidationErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "Quantity": 10, "Price": 9.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Value.Should().Be("Widget");
            dto.Quantity.Value.Should().Be(10);
            dto.Price.Value.Should().Be(9.99m);

            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "all valid VO values in a positional record should not produce any validation errors");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_InvalidValue_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "", "Quantity": 10, "Price": 9.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<PositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "an invalid value object in a positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/name");
        }
    }

    #endregion

    #region Parity — positional and property syntax should behave identically

    [Fact]
    public void Deserialize_PositionalAndPropertySyntax_ExplicitNull_ProduceSameValidationErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": null, "Quantity": null, "Price": null}""";

        // Deserialize property-syntax record
        Error.UnprocessableContent? propertyError;
        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<PropertyDto>(json, options);
            propertyError = ValidationErrorsContext.GetUnprocessableContent();
        }

        // Deserialize positional record
        Error.UnprocessableContent? positionalError;
        using (ValidationErrorsContext.BeginScope())
        {
            JsonSerializer.Deserialize<PositionalDto>(json, options);
            positionalError = ValidationErrorsContext.GetUnprocessableContent();
        }

        // Both should produce the same validation errors
        propertyError.Should().NotBeNull("property-syntax record should collect validation errors");
        positionalError.Should().NotBeNull("positional record should collect validation errors");

        positionalError!.Fields.Items.Should().HaveSameCount(propertyError!.Fields.Items,
            "positional and property-syntax records should produce the same number of validation errors");
    }

    [Fact]
    public void Deserialize_PositionalAndPropertySyntax_MissingProperty_ProduceSameValidationErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Quantity": 5, "Price": 9.99}""";

        // Deserialize property-syntax record — Name is missing
        bool propertyHasErrors;
        using (ValidationErrorsContext.BeginScope())
        {
            var propertyDto = JsonSerializer.Deserialize<PropertyDto>(json, options);
            propertyHasErrors = ValidationErrorsContext.HasErrors;
        }

        // Deserialize positional record — Name is missing
        bool positionalHasErrors;
        using (ValidationErrorsContext.BeginScope())
        {
            var positionalDto = JsonSerializer.Deserialize<PositionalDto>(json, options);
            positionalHasErrors = ValidationErrorsContext.HasErrors;
        }

        // Both should behave identically
        positionalHasErrors.Should().Be(propertyHasErrors,
            "positional and property-syntax records should behave identically for missing VO properties");
    }

    #endregion

    #region Single parameter positional record

    [Fact]
    public void Deserialize_SinglePositionalRecord_ExplicitNull_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<SinglePositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "explicit null for a single-param positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/name");
        }
    }

    [Fact]
    public void Deserialize_SinglePositionalRecord_MissingProperty_CollectsValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = "{}";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<SinglePositionalDto>(json, options);

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "missing property for a single-param positional record should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/name");
        }
    }

    #endregion

    #region Nullable VO — missing property should NOT produce validation errors

    [Fact]
    public void Deserialize_PositionalRecord_NullableVOMissing_NoValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<NullablePositionalDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Value.Should().Be("Widget");
            dto.OptionalPrice.Should().BeNull("nullable VO should be null when missing from JSON");

            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "a missing nullable VO (Price?) in a positional record should NOT produce a validation error");
        }
    }

    [Fact]
    public void Deserialize_PropertySyntaxRecord_NullableVOMissing_NoValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<NullablePropertyDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Value.Should().Be("Widget");
            dto.OptionalPrice.Should().BeNull("nullable VO should be null when missing from JSON");

            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "a missing nullable VO (Price?) in a property-syntax record should NOT produce a validation error");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_NullableVOExplicitNull_CollectsValidationError()
    {
        // Even a nullable VO should validate when explicitly provided as null in JSON,
        // because the developer sent a value — it just happens to be null.
        // This is consistent with existing behavior for explicit null tokens.
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "OptionalPrice": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<NullablePositionalDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Value.Should().Be("Widget");

            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "explicit JSON null for a scalar VO should produce a validation error even when the property is nullable");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            var fieldError = error!.Fields.Items.Should().ContainSingle()
                .Which;
            fieldError.Field.Path.Should().Be("/optionalPrice");
            fieldError.Detail.Should().Contain("Price cannot be null.",
                "explicit null should be caught by the converter, not the OnDeserialized fallback");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_NullableVOWithValidValue_NoValidationError()
    {
        var options = CreateConfiguredJsonOptions();
        var json = """{"Name": "Widget", "OptionalPrice": 19.99}""";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<NullablePositionalDto>(json, options);

            dto.Should().NotBeNull();
            dto!.Name.Value.Should().Be("Widget");
            dto.OptionalPrice.Should().NotBeNull();
            dto.OptionalPrice!.Value.Should().Be(19.99m);

            ValidationErrorsContext.HasErrors.Should().BeFalse(
                "a valid nullable VO value should not produce any validation errors");
        }
    }

    [Fact]
    public void Deserialize_PositionalRecord_RequiredVOMissingAndNullableVOMissing_OnlyRequiredErrors()
    {
        var options = CreateConfiguredJsonOptions();
        var json = "{}";

        using (ValidationErrorsContext.BeginScope())
        {
            var dto = JsonSerializer.Deserialize<NullablePositionalDto>(json, options);

            dto.Should().NotBeNull();

            // Name (non-nullable) should produce a validation error
            // OptionalPrice (nullable) should NOT produce a validation error
            ValidationErrorsContext.HasErrors.Should().BeTrue(
                "missing non-nullable VO should produce a validation error");
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle(
                "only the non-nullable required VO should produce a validation error, not the nullable one")
                .Which.Field.Path.Should().Be("/name");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates JsonSerializerOptions configured via the production DI pipeline,
    /// ensuring the same ModifyTypeInfo and OnDeserialized logic used at runtime.
    /// </summary>
    private static JsonSerializerOptions CreateConfiguredJsonOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        return httpOptions.Value.SerializerOptions;
    }

    #endregion
}
