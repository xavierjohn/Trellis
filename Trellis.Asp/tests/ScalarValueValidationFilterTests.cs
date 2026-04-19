namespace Trellis.Asp.Tests;

using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Tests for the ScalarValueValidationFilter MVC action filter.
/// </summary>
public class ScalarValueValidationFilterTests
{
    private static readonly string[] ExpectedEmailErrors =
    [
        "Email is required.",
        "Email must contain @.",
        "Email domain is invalid."
    ];

    [Fact]
    public void Filter_HasCorrectOrder()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();

        // Assert
        filter.Order.Should().Be(-2000); // Should run early
    }

    [Fact]
    public void OnActionExecuting_NoValidationErrors_DoesNotShortCircuit()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            // No errors added

            // Act
            filter.OnActionExecuting(context);

            // Assert
            context.Result.Should().BeNull(); // Should not short-circuit
            context.ModelState.IsValid.Should().BeTrue();
        }
    }

    [Fact]
    public void OnActionExecuting_WithValidationErrors_ShortCircuitsWithBadRequest()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Email", "Email is required.");
            ValidationErrorsContext.AddError("Name", "Name cannot be empty.");

            // Act
            filter.OnActionExecuting(context);

            // Assert
            context.Result.Should().NotBeNull();
            context.Result.Should().BeOfType<BadRequestObjectResult>();

            var badRequestResult = (BadRequestObjectResult)context.Result!;
            badRequestResult.StatusCode.Should().Be(400);
            badRequestResult.Value.Should().BeOfType<ValidationProblemDetails>();

            var problemDetails = (ValidationProblemDetails)badRequestResult.Value!;
            problemDetails.Status.Should().Be(400);
            problemDetails.Title.Should().Be("One or more validation errors occurred.");
            problemDetails.Errors.Should().HaveCount(2);
            problemDetails.Errors["Email"].Should().Contain("Email is required.");
            problemDetails.Errors["Name"].Should().Contain("Name cannot be empty.");
        }
    }

    [Fact]
    public void OnActionExecuting_SingleValidationError_AddsToModelState()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Email", "Email must contain @.");

            // Act
            filter.OnActionExecuting(context);

            // Assert
            var problemDetails = GetValidationProblemDetails(context);
            problemDetails.Errors.Should().ContainKey("Email");
            problemDetails.Errors["Email"].Should().Contain("Email must contain @.");
        }
    }

    [Fact]
    public void OnActionExecuting_MultipleErrorsForSameField_AddsAllErrors()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Email", "Email is required.");
            ValidationErrorsContext.AddError("Email", "Email must contain @.");
            ValidationErrorsContext.AddError("Email", "Email domain is invalid.");

            // Act
            filter.OnActionExecuting(context);

            // Assert
            var problemDetails = GetValidationProblemDetails(context);
            problemDetails.Errors.Should().ContainKey("Email");
            problemDetails.Errors["Email"].Should().HaveCount(3);
            problemDetails.Errors["Email"].Should().Contain(ExpectedEmailErrors);
        }
    }

    [Fact]
    public void OnActionExecuting_ClearsExistingModelStateForValidationErrorFields()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        // Add pre-existing ModelState error (e.g., from ASP.NET Core's "field is required")
        context.ModelState.AddModelError("Email", "The Email field is required.");

        using (ValidationErrorsContext.BeginScope())
        {
            // Add our custom validation error
            ValidationErrorsContext.AddError("Email", "Email must contain @.");

            // Act
            filter.OnActionExecuting(context);

            // Assert - uses fresh ModelState, so pre-existing errors are excluded
            var problemDetails = GetValidationProblemDetails(context);
            problemDetails.Errors.Should().ContainKey("Email");
            problemDetails.Errors["Email"].Should().ContainSingle()
                .Which.Should().Be("Email must contain @.");
        }
    }

    [Fact]
    public void OnActionExecuting_HandlesCaseInsensitiveFieldNames()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        // Add pre-existing error with different casing
        context.ModelState.AddModelError("email", "Default error");

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Email", "Email is invalid.");

            // Act
            filter.OnActionExecuting(context);

            // Assert - fresh ModelState is used, so pre-existing errors are excluded
            var problemDetails = GetValidationProblemDetails(context);
            problemDetails.Errors.Should().ContainKey("Email");
            problemDetails.Errors["Email"].Should().ContainSingle()
                .Which.Should().Be("Email is invalid.");
        }
    }

    [Fact]
    public void OnActionExecuting_HandlesNestedPropertyNames()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        // Add pre-existing error with DTO prefix
        context.ModelState.AddModelError("dto.Email", "Default error");

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Email", "Email is invalid.");

            // Act
            filter.OnActionExecuting(context);

            // Assert - fresh ModelState is used, so pre-existing "dto.Email" is excluded
            var problemDetails = GetValidationProblemDetails(context);
            problemDetails.Errors.Keys.Should().NotContain("dto.Email");
            problemDetails.Errors.Should().ContainKey("Email");
            problemDetails.Errors["Email"].Should().ContainSingle()
                .Which.Should().Be("Email is invalid.");
        }
    }

    [Fact]
    public void OnActionExecuted_DoesNothing()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutedContext();

        // Act
        filter.OnActionExecuted(context);

        // Assert - no exception thrown, nothing modified
        context.Result.Should().BeNull();
    }

    [Fact]
    public void OnActionExecuting_EmptyValidationErrorsContext_DoesNotShortCircuit()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            // Scope exists but no errors added

            // Act
            filter.OnActionExecuting(context);

            // Assert
            context.Result.Should().BeNull();
            context.ModelState.IsValid.Should().BeTrue();
        }
    }

    [Fact]
    public void OnActionExecuting_ValidationProblemDetails_HasCorrectFormat()
    {
        // Arrange
        var filter = new ScalarValueValidationFilter();
        var context = CreateActionExecutingContext();

        using (ValidationErrorsContext.BeginScope())
        {
            ValidationErrorsContext.AddError("Field1", "Error 1");
            ValidationErrorsContext.AddError("Field2", "Error 2");

            // Act
            filter.OnActionExecuting(context);

            // Assert
            var badRequestResult = context.Result as BadRequestObjectResult;
            badRequestResult.Should().NotBeNull();

            var problemDetails = badRequestResult!.Value as ValidationProblemDetails;
            problemDetails.Should().NotBeNull();
            problemDetails!.Type.Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.1");
            problemDetails.Title.Should().Be("One or more validation errors occurred.");
            problemDetails.Status.Should().Be(400);
            problemDetails.Errors.Should().HaveCount(2);
            problemDetails.Errors.Should().ContainKey("Field1");
            problemDetails.Errors.Should().ContainKey("Field2");
        }
    }

    #region ValidateScalarValueParameters Tests

    [Fact]
    public void OnActionExecuting_NullScalarValueParam_WithRouteValue_ReturnsRichValidationError()
    {
        // Arrange - parameter is IScalarValue type, value is null (binding failed), raw route value present
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["code"] = null },
            routeValues: new RouteValueDictionary { ["code"] = "INVALID" });

        // Act
        filter.OnActionExecuting(context);

        // Assert - should get rich error from TryCreate
        context.Result.Should().NotBeNull().And.BeOfType<BadRequestObjectResult>();
        var problemDetails = GetValidationProblemDetails(context);
        problemDetails.Errors.Should().ContainKey("code");
        problemDetails.Errors["code"].Should().Contain(e => e.Contains("ORD-"));
    }

    [Fact]
    public void OnActionExecuting_NullScalarValueParam_WithQueryValue_ReturnsRichValidationError()
    {
        // Arrange - raw value from query string instead of route
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["code"] = null },
            routeValues: new RouteValueDictionary(),
            queryString: "?code=BAD");

        // Act
        filter.OnActionExecuting(context);

        // Assert
        context.Result.Should().NotBeNull().And.BeOfType<BadRequestObjectResult>();
        var problemDetails = GetValidationProblemDetails(context);
        problemDetails.Errors.Should().ContainKey("code");
        problemDetails.Errors["code"].Should().Contain(e => e.Contains("ORD-"));
    }

    [Fact]
    public void OnActionExecuting_NullScalarValueParam_NoRawValue_DoesNotShortCircuit()
    {
        // Arrange - no raw value in route or query means the parameter was simply not provided
        // (e.g., optional query param like OrderState? state). Should not validate.
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["code"] = null },
            routeValues: new RouteValueDictionary());

        // Act
        filter.OnActionExecuting(context);

        // Assert - no raw value means parameter was not provided, not a binding failure
        context.Result.Should().BeNull("absent optional parameter should not trigger validation");
    }

    [Fact]
    public void OnActionExecuting_NonNullScalarValueParam_DoesNotShortCircuit()
    {
        // Arrange - parameter is IScalarValue type but non-null (successfully bound)
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var testCode = TestOrderCode.Create("ORD-123");
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["code"] = testCode },
            routeValues: new RouteValueDictionary { ["code"] = "ORD-123" });

        // Act
        filter.OnActionExecuting(context);

        // Assert
        context.Result.Should().BeNull("non-null IScalarValue should not trigger validation");
    }

    [Fact]
    public void OnActionExecuting_NonScalarValueParam_Null_DoesNotShortCircuit()
    {
        // Arrange - parameter is NOT an IScalarValue type, even though null
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "name", ParameterType = typeof(string) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["name"] = null },
            routeValues: new RouteValueDictionary());

        // Act
        filter.OnActionExecuting(context);

        // Assert
        context.Result.Should().BeNull("non-IScalarValue params should be skipped");
    }

    [Fact]
    public void OnActionExecuting_NullableScalarValueParam_Null_ReturnsValidationError()
    {
        // Arrange - Nullable<> wrapping of IScalarValue is handled by Nullable.GetUnderlyingType
        // In practice, ScalarValueObjects are reference types so Nullable<> doesn't apply,
        // but the code handles it defensively. Test with the direct type + null value.
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["code"] = null },
            routeValues: new RouteValueDictionary { ["code"] = "X" });

        // Act
        filter.OnActionExecuting(context);

        // Assert
        context.Result.Should().NotBeNull().And.BeOfType<BadRequestObjectResult>();
        var problemDetails = GetValidationProblemDetails(context);
        problemDetails.Errors.Should().ContainKey("code");
    }

    [Fact]
    public void OnActionExecuting_MixedParams_OnlyValidatesScalarValues()
    {
        // Arrange - multiple params, only the IScalarValue one should be validated
        var filter = new ScalarValueValidationFilter();
        var codeParam = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var idParam = new ParameterDescriptor { Name = "id", ParameterType = typeof(int) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [codeParam, idParam],
            arguments: new Dictionary<string, object?> { ["code"] = null, ["id"] = null },
            routeValues: new RouteValueDictionary { ["code"] = "BAD" });

        // Act
        filter.OnActionExecuting(context);

        // Assert - only the IScalarValue param should have an error
        context.Result.Should().NotBeNull().And.BeOfType<BadRequestObjectResult>();
        var problemDetails = GetValidationProblemDetails(context);
        problemDetails.Errors.Should().ContainKey("code");
        problemDetails.Errors.Keys.Should().NotContain("id");
    }

    [Fact]
    public void OnActionExecuting_ParamNotInArguments_DoesNotShortCircuit()
    {
        // Arrange - parameter declared but not present in ActionArguments
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "code", ParameterType = typeof(TestOrderCode) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?>(), // code not present
            routeValues: new RouteValueDictionary { ["code"] = "BAD" });

        // Act
        filter.OnActionExecuting(context);

        // Assert - TryGetValue returns false, so no validation error
        context.Result.Should().BeNull();
    }

    [Fact]
    public void OnActionExecuting_OptionalNullableScalarValueParam_NotProvided_DoesNotShortCircuit()
    {
        // Arrange - simulates: FilterOrders([FromQuery] OrderState? state) called without ?state=
        // The parameter is in ActionArguments as null, but no raw value exists in route or query.
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = new ParameterDescriptor { Name = "state", ParameterType = typeof(TestOrderCode) };
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["state"] = null },
            routeValues: new RouteValueDictionary());

        // Act
        filter.OnActionExecuting(context);

        // Assert - parameter was not provided, not a binding failure
        context.Result.Should().BeNull("optional parameter not provided should not trigger validation");
    }

    [Fact]
    public void OnActionExecuting_OptionalNullableScalarValueParam_EmptyQueryValue_DoesNotShortCircuit()
    {
        // Arrange - simulates: FilterOrders([FromQuery] OrderState? state) called with ?state=
        // ASP.NET Core treats empty query string input for nullable simple values as null, not a validation error.
        var filter = new ScalarValueValidationFilter();
        var paramDescriptor = CreateControllerParameterDescriptor(nameof(OptionalNullableScalarValueParam), "state");
        var context = CreateActionExecutingContextWithParams(
            parameters: [paramDescriptor],
            arguments: new Dictionary<string, object?> { ["state"] = null },
            routeValues: new RouteValueDictionary(),
            queryString: "?state=");

        // Act
        filter.OnActionExecuting(context);

        // Assert - empty query string for an optional nullable parameter should behave like no value
        context.Result.Should().BeNull("optional nullable parameter with empty query input should not trigger validation");
    }

    #endregion

    private static void OptionalNullableScalarValueParam(TestOrderCode? state) { }

    #region Test Value Objects

    /// <summary>
    /// Test value object that validates order codes start with "ORD-".
    /// </summary>
    public class TestOrderCode : ScalarValueObject<TestOrderCode, string>, IScalarValue<TestOrderCode, string>
    {
        private TestOrderCode(string value) : base(value) { }
        public static Result<TestOrderCode> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "orderCode";
            if (string.IsNullOrEmpty(value) || !value.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase))
                return Result.Fail<Asp.Tests.ScalarValueValidationFilterTests.TestOrderCode>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = $"Order code must start with 'ORD-'. Got: '{value}'." })));
            return Result.Ok(new TestOrderCode(value));
        }
    }

    #endregion

    #region Helper Methods

    private static ValidationProblemDetails GetValidationProblemDetails(ActionExecutingContext context)
    {
        context.Result.Should().NotBeNull().And.BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)context.Result!;
        badRequest.Value.Should().NotBeNull().And.BeOfType<ValidationProblemDetails>();
        return (ValidationProblemDetails)badRequest.Value!;
    }

    private static ActionExecutingContext CreateActionExecutingContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);
    }

    private static ActionExecutedContext CreateActionExecutedContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());

        return new ActionExecutedContext(
            actionContext,
            new List<IFilterMetadata>(),
            controller: null!);
    }

    private static ActionExecutingContext CreateActionExecutingContextWithParams(
        IList<ParameterDescriptor> parameters,
        Dictionary<string, object?> arguments,
        RouteValueDictionary routeValues,
        string? queryString = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        if (queryString is not null)
            httpContext.Request.QueryString = new QueryString(queryString);

        var actionDescriptor = new ActionDescriptor { Parameters = parameters };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(routeValues),
            actionDescriptor,
            new ModelStateDictionary());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            arguments,
            controller: null!);
    }

    private static ControllerParameterDescriptor CreateControllerParameterDescriptor(string methodName, string parameterName)
    {
        var parameterInfo = typeof(ScalarValueValidationFilterTests)
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        return new ControllerParameterDescriptor
        {
            Name = parameterName,
            ParameterType = parameterInfo.ParameterType,
            ParameterInfo = parameterInfo
        };
    }

    #endregion
}
