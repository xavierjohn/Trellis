namespace Trellis.Asp.Tests;

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Asp.Validation;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

/// <summary>
/// Tests for service collection extension methods that configure value object validation.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    #region Test Value Objects

    public sealed class TestName : ScalarValueObject<TestName, string>, IScalarValue<TestName, string>
    {
        private TestName(string value) : base(value) { }

        public static Result<TestName> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "name";
            if (string.IsNullOrWhiteSpace(value))
                return Error.Validation("Name is required.", field);
            return new TestName(value);
        }
    }

    public sealed class TestEmail : ScalarValueObject<TestEmail, string>, IScalarValue<TestEmail, string>
    {
        private TestEmail(string value) : base(value) { }

        public static Result<TestEmail> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "email";
            if (string.IsNullOrWhiteSpace(value))
                return Error.Validation("Email is required.", field);
            if (!value.Contains('@'))
                return Error.Validation("Email must contain @.", field);
            return new TestEmail(value);
        }
    }

    public sealed class TestAge : ScalarValueObject<TestAge, int>, IScalarValue<TestAge, int>
    {
        private TestAge(int value) : base(value) { }

        public static Result<TestAge> TryCreate(int value, string? fieldName = null) =>
            value is < 0 or > 150
                ? Error.Validation("Age must be between 0 and 150.", fieldName ?? "age")
                : new TestAge(value);
        public static Result<TestAge> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region Test DTOs

    public record SingleValueObjectDto(TestName Name);

    public record MultipleValueObjectsDto(TestName Name, TestEmail Email, TestAge Age);

    public record MixedDto(TestName Name, string Description, int Count);

    public record NestedDto(TestName Name, AddressDto Address);

    public record AddressDto(TestName Street, TestName City);

    public record NullableValueObjectDto(TestName? Name, TestEmail? Email);

    #endregion

    #region AddScalarValueValidation Tests

    [Fact]
    public void AddScalarValueValidation_RegistersMvcJsonOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers()
            .AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>();

        // Assert
        var hasFactory = mvcOptions.Value.JsonSerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        hasFactory.Should().BeTrue();
    }

    [Fact]
    public void AddScalarValueValidation_RegistersModelBinderProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers()
            .AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>();

        // Assert
        var hasProvider = mvcOptions.Value.ModelBinderProviders
            .Any(p => p.GetType() == typeof(ScalarValueModelBinderProvider));
        hasProvider.Should().BeTrue();

        // Should be at the start (highest priority)
        mvcOptions.Value.ModelBinderProviders.FirstOrDefault()
            .Should().BeOfType<ScalarValueModelBinderProvider>();
    }

    [Fact]
    public void AddScalarValueValidation_RegistersValidationFilter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers()
            .AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>();

        // Assert
        var hasFilter = mvcOptions.Value.Filters.Any(f =>
            f is TypeFilterAttribute tfa && tfa.ImplementationType == typeof(ScalarValueValidationFilter));
        hasFilter.Should().BeTrue();
    }

    [Fact]
    public void AddScalarValueValidation_SuppressesModelStateInvalidFilter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers()
            .AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var apiBehaviorOptions = serviceProvider.GetRequiredService<IOptions<ApiBehaviorOptions>>();

        // Assert
        apiBehaviorOptions.Value.SuppressModelStateInvalidFilter.Should().BeTrue();
    }

    [Fact]
    public void AddScalarValueValidation_ConfiguresTypeInfoResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers()
            .AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>();

        // Assert
        mvcOptions.Value.JsonSerializerOptions.TypeInfoResolver.Should().NotBeNull();
    }

    #endregion

    #region AddScalarValueValidation Tests

    [Fact]
    public void AddScalarValueValidation_ConfiguresMvcJsonOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>();

        // Assert
        var hasFactory = mvcOptions.Value.JsonSerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        hasFactory.Should().BeTrue();
    }

    [Fact]
    public void AddScalarValueValidation_ConfiguresHttpJsonOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        // Assert
        var hasFactory = httpOptions.Value.SerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        hasFactory.Should().BeTrue();
    }

    [Fact]
    public void AddScalarValueValidation_ConfiguresBothMvcAndMinimalApi()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        // Assert
        // Both should have the factory
        var mvcHasFactory = mvcOptions.Value.JsonSerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        mvcHasFactory.Should().BeTrue();

        var httpHasFactory = httpOptions.Value.SerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        httpHasFactory.Should().BeTrue();

        // Both should have type info resolver
        mvcOptions.Value.JsonSerializerOptions.TypeInfoResolver.Should().NotBeNull();
        httpOptions.Value.SerializerOptions.TypeInfoResolver.Should().NotBeNull();
    }

    [Fact]
    public void AddScalarValueValidation_DoesNotAddModelBindingOrFilters()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddScalarValueValidation();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcOptions>>();

        // Assert
        // Unified method doesn't add MVC-specific features
        var hasProvider = mvcOptions.Value.ModelBinderProviders
            .Any(p => p.GetType() == typeof(ScalarValueModelBinderProvider));
        hasProvider.Should().BeFalse();

        var hasFilter = mvcOptions.Value.Filters.Any(f =>
            f is TypeFilterAttribute tfa && tfa.ImplementationType == typeof(ScalarValueValidationFilter));
        hasFilter.Should().BeFalse();
    }

    #endregion

    #region AddScalarValueValidationForMinimalApi Tests

    [Fact]
    public void AddScalarValueValidationForMinimalApi_ConfiguresHttpJsonOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        // Assert
        var hasFactory = httpOptions.Value.SerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        hasFactory.Should().BeTrue();
        httpOptions.Value.SerializerOptions.TypeInfoResolver.Should().NotBeNull();
    }

    [Fact]
    public void AddScalarValueValidationForMinimalApi_DoesNotAffectMvcOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddControllers();
        services.AddScalarValueValidationForMinimalApi();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var mvcOptions = serviceProvider.GetRequiredService<IOptions<MvcJsonOptions>>();

        // Assert
        // MVC options should not be affected
        var hasFactory = mvcOptions.Value.JsonSerializerOptions.Converters
            .Any(c => c.GetType() == typeof(ValidatingJsonConverterFactory));
        hasFactory.Should().BeFalse();
    }

    #endregion

    #region UseScalarValueValidation Tests

    [Fact]
    public void UseScalarValueValidation_AddsMiddleware()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(serviceProvider);

        // Act
        var result = app.UseScalarValueValidation();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeSameAs(app);
    }

    #endregion

    #region Integration Tests - JSON Deserialization

    [Fact]
    public void ConfiguredJsonOptions_DeserializeValidValueObject_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = "\"John\"";

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Name";

            // Act
            var result = JsonSerializer.Deserialize<TestName>(json, httpOptions.Value.SerializerOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be("John");
            ValidationErrorsContext.GetValidationError().Should().BeNull();
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeInvalidValueObject_CollectsErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = "\"\""; // Empty string - invalid

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "Name";

            // Act
            var result = JsonSerializer.Deserialize<TestName>(json, httpOptions.Value.SerializerOptions);

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().ContainSingle()
                .Which.FieldName.Should().Be("Name");
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithMultipleValueObjects_AllValid_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "John", "Email": "john@example.com", "Age": 30}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<MultipleValueObjectsDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Value.Should().Be("John");
            result.Email.Value.Should().Be("john@example.com");
            result.Age.Value.Should().Be(30);
            ValidationErrorsContext.GetValidationError().Should().BeNull();
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithMultipleValueObjects_MultipleInvalid_CollectsAllErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "", "Email": "invalid-email", "Age": 200}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<MultipleValueObjectsDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(3);
            error.FieldErrors.Should().Contain(e => e.FieldName == "name");
            error.FieldErrors.Should().Contain(e => e.FieldName == "email");
            error.FieldErrors.Should().Contain(e => e.FieldName == "age");
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithMixedProperties_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "Product", "Description": "A great product", "Count": 5}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<MixedDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Value.Should().Be("Product");
            result.Description.Should().Be("A great product");
            result.Count.Should().Be(5);
            ValidationErrorsContext.GetValidationError().Should().BeNull();
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithNestedValueObjects_AllValid_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "John", "Address": {"Street": "123 Main St", "City": "New York"}}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<NestedDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Value.Should().Be("John");
            result.Address.Street.Value.Should().Be("123 Main St");
            result.Address.City.Value.Should().Be("New York");
            ValidationErrorsContext.GetValidationError().Should().BeNull();
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithNestedValueObjects_NestedInvalid_CollectsErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "John", "Address": {"Street": "", "City": ""}}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<NestedDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCountGreaterOrEqualTo(2);
            // Nested properties get their field names from the JSON property names (lowercase)
            error.FieldErrors.Should().Contain(e => e.FieldName == "street");
            error.FieldErrors.Should().Contain(e => e.FieldName == "city");
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithNullableValueObjects_NullValues_CollectsValidationErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": null, "Email": null}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<NullableValueObjectDto>(json, httpOptions.Value.SerializerOptions);

            // Assert — explicit JSON null for scalar VOs produces validation errors
            result.Should().NotBeNull();
            result!.Name.Should().BeNull();
            result.Email.Should().BeNull();
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull("explicit JSON null for scalar VOs should produce validation errors");
            error!.FieldErrors.Should().HaveCount(2);
            error.FieldErrors.Should().Contain(e => e.FieldName == "name");
            error.FieldErrors.Should().Contain(e => e.FieldName == "email");
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithNullableValueObjects_ValidValues_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "John", "Email": "john@example.com"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<NullableValueObjectDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Should().NotBeNull();
            result.Name!.Value.Should().Be("John");
            result.Email.Should().NotBeNull();
            result.Email!.Value.Should().Be("john@example.com");
            ValidationErrorsContext.GetValidationError().Should().BeNull();
        }
    }

    [Fact]
    public void ConfiguredJsonOptions_DeserializeDto_WithNullableValueObjects_InvalidValues_CollectsErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScalarValueValidation();
        var serviceProvider = services.BuildServiceProvider();
        var httpOptions = serviceProvider.GetRequiredService<IOptions<HttpJsonOptions>>();

        var json = """{"Name": "", "Email": "invalid"}""";

        using (ValidationErrorsContext.BeginScope())
        {
            // Act
            var result = JsonSerializer.Deserialize<NullableValueObjectDto>(json, httpOptions.Value.SerializerOptions);

            // Assert
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(2);
        }
    }

    #endregion
}