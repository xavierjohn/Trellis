namespace Trellis.Asp.Tests;

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Tests for scalar value validation during JSON deserialization.
/// Verifies that validation errors are correctly attributed to property names,
/// not type names, especially when the same scalar value type is used for multiple properties.
/// </summary>
public class ScalarValueValidationTests
{
    #region Test Value Objects

    /// <summary>
    /// A generic name value object used for testing.
    /// Can be used for multiple properties (FirstName, LastName) to verify field name attribution.
    /// </summary>
    public class Name : ScalarValueObject<Name, string>, IScalarValue<Name, string>
    {
        private Name(string value) : base(value) { }

        public static Result<Name> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "name";
            if (string.IsNullOrWhiteSpace(value))
                return Error.Validation("Name cannot be empty.", field);
            return new Name(value.Trim());
        }
    }

    /// <summary>
    /// Test email value object.
    /// </summary>
    public class TestEmail : ScalarValueObject<TestEmail, string>, IScalarValue<TestEmail, string>
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

    /// <summary>
    /// DTO using the same Name type for multiple properties.
    /// </summary>
    public class PersonDto
    {
        public Name? FirstName { get; set; }
        public Name? LastName { get; set; }
        public TestEmail? Email { get; set; }
    }

    #endregion

    #region Validation Error Context Tests

    [Fact]
    public void ValidationErrorsContext_CollectsErrors_WhenScopeIsActive()
    {
        // Arrange & Act
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("field1", "Error 1");
            ValidationErrorsContext.AddError("field2", "Error 2");

            var error = ValidationErrorsContext.GetValidationError();

            // Assert
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(2);
            error.FieldErrors[0].FieldName.Should().Be("field1");
            error.FieldErrors[0].Details[0].Should().Be("Error 1");
            error.FieldErrors[1].FieldName.Should().Be("field2");
            error.FieldErrors[1].Details[0].Should().Be("Error 2");
        }
    }

    [Fact]
    public void ValidationErrorsContext_ReturnsNull_WhenNoScopeIsActive()
    {
        // Act
        var error = ValidationErrorsContext.GetValidationError();

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void ValidationErrorsContext_ClearsErrors_WhenScopeIsDisposed()
    {
        // Arrange
        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("field", "Error");
        }

        // Act
        var error = ValidationErrorsContext.GetValidationError();

        // Assert
        error.Should().BeNull();
    }

    [Fact]
    public void ValidationErrorsContext_TracksCurrentPropertyName()
    {
        // Arrange & Act
        ValidationErrorsContext.CurrentPropertyName = "TestProperty";
        var propertyName = ValidationErrorsContext.CurrentPropertyName;

        // Assert
        propertyName.Should().Be("TestProperty");

        // Cleanup
        ValidationErrorsContext.CurrentPropertyName = null;
    }

    #endregion

    #region JSON Converter Factory Tests

    [Fact]
    public void ValidatingJsonConverterFactory_CanConvert_IScalarValue()
    {
        // Arrange
        var factory = new ValidatingJsonConverterFactory();

        // Act & Assert
        factory.CanConvert(typeof(Name)).Should().BeTrue();
        factory.CanConvert(typeof(TestEmail)).Should().BeTrue();
    }

    [Fact]
    public void ValidatingJsonConverterFactory_CannotConvert_NonValueObjectTypes()
    {
        // Arrange
        var factory = new ValidatingJsonConverterFactory();

        // Act & Assert
        factory.CanConvert(typeof(string)).Should().BeFalse();
        factory.CanConvert(typeof(int)).Should().BeFalse();
        factory.CanConvert(typeof(PersonDto)).Should().BeFalse();
    }

    #endregion

    #region JSON Deserialization Tests

    [Fact]
    public void Deserialize_ValidData_ReturnsValueObject()
    {
        // Arrange
        var options = CreateJsonOptions();
        var json = "\"John\"";

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "FirstName";

            // Act
            var result = JsonSerializer.Deserialize<Name>(json, options);

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be("John");
            ValidationErrorsContext.GetValidationError().Should().BeNull();
        }
    }

    [Fact]
    public void Deserialize_InvalidData_CollectsValidationError()
    {
        // Arrange
        var options = CreateJsonOptions();
        var json = "\"\""; // Empty string - invalid for Name

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "FirstName";

            // Act
            var result = JsonSerializer.Deserialize<Name>(json, options);

            // Assert
            result.Should().BeNull();
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(1);
            error.FieldErrors[0].FieldName.Should().Be("FirstName");
            error.FieldErrors[0].Details[0].Should().Be("Name cannot be empty.");
        }
    }

    [Fact]
    public void Deserialize_SameTypeForMultipleProperties_UsesCorrectFieldNames()
    {
        // Arrange
        var options = CreateJsonOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            // Simulate deserializing FirstName (invalid)
            ValidationErrorsContext.CurrentPropertyName = "FirstName";
            JsonSerializer.Deserialize<Name>("\"\"", options);

            // Simulate deserializing LastName (invalid)
            ValidationErrorsContext.CurrentPropertyName = "LastName";
            JsonSerializer.Deserialize<Name>("\"\"", options);

            // Act
            var error = ValidationErrorsContext.GetValidationError();

            // Assert
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(2);

            // Both errors should have their respective property names, not "name"
            error.FieldErrors.Should().Contain(e => e.FieldName == "FirstName");
            error.FieldErrors.Should().Contain(e => e.FieldName == "LastName");

            // Neither should have the default type-based name
            error.FieldErrors.Should().NotContain(e => e.FieldName == "name");
        }
    }

    [Fact]
    public void Deserialize_MultipleInvalidFields_CollectsAllErrors()
    {
        // Arrange
        var options = CreateJsonOptions();

        using (ValidationErrorsContext.BeginScope())
        {
            // Invalid FirstName
            ValidationErrorsContext.CurrentPropertyName = "FirstName";
            JsonSerializer.Deserialize<Name>("\"\"", options);

            // Invalid LastName
            ValidationErrorsContext.CurrentPropertyName = "LastName";
            JsonSerializer.Deserialize<Name>("\"\"", options);

            // Invalid Email
            ValidationErrorsContext.CurrentPropertyName = "Email";
            JsonSerializer.Deserialize<TestEmail>("\"not-an-email\"", options);

            // Act
            var error = ValidationErrorsContext.GetValidationError();

            // Assert
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(3);
            error.FieldErrors.Should().Contain(e => e.FieldName == "FirstName" && e.Details[0] == "Name cannot be empty.");
            error.FieldErrors.Should().Contain(e => e.FieldName == "LastName" && e.Details[0] == "Name cannot be empty.");
            error.FieldErrors.Should().Contain(e => e.FieldName == "Email" && e.Details[0] == "Email must contain @.");
        }
    }

    [Fact]
    public void Deserialize_NullJson_ReturnsNull()
    {
        // Arrange
        var options = CreateJsonOptions();
        var json = "null";

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.CurrentPropertyName = "FirstName";

            // Act
            var result = JsonSerializer.Deserialize<Name>(json, options);

            // Assert
            result.Should().BeNull();
            // Null values DO produce validation errors via ValidatingJsonConverter.OnNullToken.
            // The endpoint filter or action filter will return 400 before the handler runs.
            ValidationErrorsContext.HasErrors.Should().BeTrue();
        }
    }

    #endregion

    #region Property Name Aware Converter Tests

    [Fact]
    public void PropertyNameAwareConverter_SetsAndRestoresPropertyName()
    {
        // Arrange
        var options = CreateJsonOptions();
        var json = "\"test@example.com\"";

        using (ValidationErrorsContext.BeginScope())
        {
            // Set an outer property name (simulating nested object)
            ValidationErrorsContext.CurrentPropertyName = "OuterProperty";

            // Create a property-aware converter
            var innerConverter = new ValidatingJsonConverter<TestEmail, string>();
            var propertyAwareConverter = new PropertyNameAwareConverter<TestEmail>(innerConverter, "InnerEmail");

            // Act
            var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
            reader.Read(); // Move to first token
            var result = propertyAwareConverter.Read(ref reader, typeof(TestEmail), options);

            // Assert
            result.Should().NotBeNull();
            result!.Value.Should().Be("test@example.com");
            // Property name should be restored to outer value
            ValidationErrorsContext.CurrentPropertyName.Should().Be("OuterProperty");
        }
    }

    #endregion

    #region ScalarValueTypeHelper Tests

    [Fact]
    public void ScalarValueTypeHelper_IsScalarValueObject_ReturnsTrueForValueObjects()
    {
        // Act & Assert
        ScalarValueTypeHelper.IsScalarValue(typeof(Name)).Should().BeTrue();
        ScalarValueTypeHelper.IsScalarValue(typeof(TestEmail)).Should().BeTrue();
    }

    [Fact]
    public void ScalarValueTypeHelper_IsScalarValueObject_ReturnsFalseForNonValueObjects()
    {
        // Act & Assert
        ScalarValueTypeHelper.IsScalarValue(typeof(string)).Should().BeFalse();
        ScalarValueTypeHelper.IsScalarValue(typeof(int)).Should().BeFalse();
        ScalarValueTypeHelper.IsScalarValue(typeof(PersonDto)).Should().BeFalse();
    }

    [Fact]
    public void ScalarValueTypeHelper_GetPrimitiveType_ReturnsCorrectPrimitiveType()
    {
        // Act & Assert
        ScalarValueTypeHelper.GetPrimitiveType(typeof(Name)).Should().Be<string>();
        ScalarValueTypeHelper.GetPrimitiveType(typeof(TestEmail)).Should().Be<string>();
    }

    [Fact]
    public void ScalarValueTypeHelper_GetPrimitiveType_ReturnsNullForNonValueObjects()
    {
        // Act & Assert
        ScalarValueTypeHelper.GetPrimitiveType(typeof(string)).Should().BeNull();
        ScalarValueTypeHelper.GetPrimitiveType(typeof(PersonDto)).Should().BeNull();
    }

    #endregion

    #region ValidationError ToDictionary Tests

    [Fact]
    public void ValidationError_ToDictionary_ReturnsCorrectDictionary()
    {
        // Arrange
        var error = ValidationError.For("Email", "Email is required")
            .And("Password", "Password is too short")
            .And("Email", "Email format is invalid");

        // Act
        var dict = error.ToDictionary();

        // Assert
        dict.Should().HaveCount(2);
        dict["Email"].Should().Contain("Email is required");
        dict["Email"].Should().Contain("Email format is invalid");
        dict["Password"].Should().Contain("Password is too short");
    }

    [Fact]
    public void ValidationError_ToDictionary_SingleFieldError()
    {
        // Arrange
        var error = ValidationError.For("Name", "Name cannot be empty");

        // Act
        var dict = error.ToDictionary();

        // Assert
        dict.Should().HaveCount(1);
        dict["Name"].Should().Contain("Name cannot be empty");
    }

    #endregion

    #region ScalarValueValidationEndpointFilter Tests

    [Fact]
    public async Task EndpointFilter_WithValidationErrors_ReturnsValidationProblem()
    {
        // Arrange
        var filter = new ScalarValueValidationEndpointFilter();
        var nextCalled = false;

        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        };

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Email", "Email is required");

            // Act
            var result = await filter.InvokeAsync(null!, next);

            // Assert
            nextCalled.Should().BeFalse();
            // Results.ValidationProblem() returns ProblemHttpResult
            result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IResult>();
            var problemResult = result as Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult;
            problemResult.Should().NotBeNull();
            problemResult!.StatusCode.Should().Be(400);
        }
    }

    [Fact]
    public async Task EndpointFilter_WithoutValidationErrors_CallsNext()
    {
        // Arrange
        var filter = new ScalarValueValidationEndpointFilter();
        var nextCalled = false;

        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        };

        using (ValidationErrorsContext.BeginScope())
        {
            // No validation errors added

            // Act
            var result = await filter.InvokeAsync(null!, next);

            // Assert
            nextCalled.Should().BeTrue();
            result.Should().Be("success");
        }
    }

    #endregion

    #region ScalarValueValidationMiddleware Tests

    [Fact]
    public async Task Middleware_CreatesScopeForRequest()
    {
        // Arrange
        var scopeWasActive = false;
        RequestDelegate next = _ =>
        {
            scopeWasActive = ValidationErrorsContext.HasErrors || ValidationErrorsContext.GetValidationError() is null;
            // Add an error to verify scope is active
            ValidationErrorsContext.AddError("Test", "TestError");
            scopeWasActive = ValidationErrorsContext.HasErrors;
            return Task.CompletedTask;
        };

        var middleware = new ScalarValueValidationMiddleware(next);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert - scope was active during request
        scopeWasActive.Should().BeTrue();

        // Assert - scope is cleaned up after request
        ValidationErrorsContext.GetValidationError().Should().BeNull();
    }

    [Fact]
    public async Task Middleware_CleansUpScopeEvenOnException()
    {
        // Arrange
        RequestDelegate next = _ =>
        {
            ValidationErrorsContext.AddError("Test", "TestError");
            throw new InvalidOperationException("Test exception");
        };

        var middleware = new ScalarValueValidationMiddleware(next);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        ValidationErrorsContext.GetValidationError().Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ValidatingJsonConverterFactory());
        return options;
    }

    #endregion
}