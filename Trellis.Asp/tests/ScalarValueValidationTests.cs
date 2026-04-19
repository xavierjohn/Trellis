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
                return Result.Fail<Asp.Tests.ScalarValueValidationTests.Name>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Name cannot be empty." })));
            return Result.Ok(new Name(value.Trim()));
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
                return Result.Fail<Asp.Tests.ScalarValueValidationTests.TestEmail>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email is required." })));
            if (!value.Contains('@'))
                return Result.Fail<Asp.Tests.ScalarValueValidationTests.TestEmail>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email must contain @." })));
            return Result.Ok(new TestEmail(value));
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

            var error = ValidationErrorsContext.GetUnprocessableContent();

            // Assert
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(2);
            error.Fields[0].Field.Path.Should().Be("/field1");
            error.Fields[0].Detail.Should().Be("Error 1");
            error.Fields[1].Field.Path.Should().Be("/field2");
            error.Fields[1].Detail.Should().Be("Error 2");
        }
    }

    [Fact]
    public void ValidationErrorsContext_ReturnsNull_WhenNoScopeIsActive()
    {
        // Act
        var error = ValidationErrorsContext.GetUnprocessableContent();

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
        var error = ValidationErrorsContext.GetUnprocessableContent();

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
            ValidationErrorsContext.GetUnprocessableContent().Should().BeNull();
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
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(1);
            error.Fields[0].Field.Path.Should().Be("/FirstName");
            error.Fields[0].Detail.Should().Be("Name cannot be empty.");
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
            var error = ValidationErrorsContext.GetUnprocessableContent();

            // Assert
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(2);

            // Both errors should have their respective property names, not "name"
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/FirstName");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/LastName");

            // Neither should have the default type-based name
            error.Fields.Items.Should().NotContain(e => e.Field.Path == "/name");
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
            var error = ValidationErrorsContext.GetUnprocessableContent();

            // Assert
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(3);
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/FirstName" && e.Detail == "Name cannot be empty.");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/LastName" && e.Detail == "Name cannot be empty.");
            error.Fields.Items.Should().Contain(e => e.Field.Path == "/Email" && e.Detail == "Email must contain @.");
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

    #region Error.UnprocessableContent ToDictionary Tests

    [Fact]
    public void ValidationError_ToDictionary_ReturnsCorrectDictionary()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("Email"), "validation.error") { Detail = "Email is required" },
            new FieldViolation(InputPointer.ForProperty("Password"), "validation.error") { Detail = "Password is too short" },
            new FieldViolation(InputPointer.ForProperty("Email"), "validation.error") { Detail = "Email format is invalid" }));

        // Act - convert to dictionary by grouping field violations
        var dict = error.Fields.Items.GroupBy(f => f.Field.Path)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Detail ?? f.ReasonCode).ToArray());

        // Assert
        dict.Should().HaveCount(2);
        dict["/Email"].Should().Contain("Email is required");
        dict["/Email"].Should().Contain("Email format is invalid");
        dict["/Password"].Should().Contain("Password is too short");
    }

    [Fact]
    public void ValidationError_ToDictionary_SingleFieldError()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("Name"), "validation.error") { Detail = "Name cannot be empty" }));

        // Act
        var dict = error.Fields.Items.GroupBy(f => f.Field.Path)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Detail ?? f.ReasonCode).ToArray());

        // Assert
        dict.Should().HaveCount(1);
        dict["/Name"].Should().Contain("Name cannot be empty");
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
            scopeWasActive = ValidationErrorsContext.HasErrors || ValidationErrorsContext.GetUnprocessableContent() is null;
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
        ValidationErrorsContext.GetUnprocessableContent().Should().BeNull();
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
        ValidationErrorsContext.GetUnprocessableContent().Should().BeNull();
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
