namespace Trellis.Asp.Tests;

using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Trellis;
using Xunit;

/// <summary>
/// Tests for ScalarValueValidationMiddleware to ensure proper scope management.
/// </summary>
public class ScalarValueValidationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_CreatesValidationScope()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            // Inside the middleware scope
            ValidationErrorsContext.Current.Should().NotBeNull("scope should be active");
            await Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        // After middleware completes, scope should be cleaned up
        ValidationErrorsContext.Current.Should().BeNull("scope should be disposed");
    }

    [Fact]
    public async Task InvokeAsync_ScopeContainsNoErrorsInitially()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            // Verify scope is clean
            ValidationErrorsContext.HasErrors.Should().BeFalse();
            ValidationErrorsContext.GetValidationError().Should().BeNull();
            await Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        // Act & Assert
        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_ErrorsAddedInScopeAreAccessible()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            // Add an error
            ValidationErrorsContext.AddError("TestField", "Test error message");

            // Verify it's accessible
            ValidationErrorsContext.HasErrors.Should().BeTrue();
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().ContainSingle()
                .Which.FieldName.Should().Be("TestField");

            await Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_ScopeDisposedAfterException()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new InvalidOperationException("Test exception"));

        var context = new DefaultHttpContext();

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Scope should still be disposed even after exception
        ValidationErrorsContext.Current.Should().BeNull("scope should be disposed even after exception");
    }

    [Fact]
    public async Task InvokeAsync_MultipleSequentialRequests_IsolatedScopes()
    {
        // Arrange
        var requestCount = 0;
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            requestCount++;
            ValidationErrorsContext.AddError($"Field{requestCount}", $"Error {requestCount}");

            var error = ValidationErrorsContext.GetValidationError();
            error!.FieldErrors.Should().ContainSingle("each request should have isolated scope");
            error.FieldErrors[0].FieldName.Should().Be($"Field{requestCount}");

            await Task.CompletedTask;
        });

        // Act - Process 3 sequential requests
        await middleware.InvokeAsync(new DefaultHttpContext());
        await middleware.InvokeAsync(new DefaultHttpContext());
        await middleware.InvokeAsync(new DefaultHttpContext());

        // Assert
        requestCount.Should().Be(3);
        ValidationErrorsContext.Current.Should().BeNull("all scopes should be disposed");
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentRequests_IsolatedScopes()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            // Add a unique error based on thread
            var threadId = Environment.CurrentManagedThreadId;
            ValidationErrorsContext.AddError($"Field{threadId}", $"Error from thread {threadId}");

            // Small delay to increase chance of concurrent execution
            await Task.Delay(10);

            // Verify this scope only has this thread's error
            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().ContainSingle("scope should be isolated per async context");
        });

        // Act - Process 10 concurrent requests
        var tasks = new Task[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = middleware.InvokeAsync(new DefaultHttpContext());
        }

        // Assert - All should complete without interference
        await Task.WhenAll(tasks);
        ValidationErrorsContext.Current.Should().BeNull("all scopes should be disposed");
    }

    [Fact]
    public async Task InvokeAsync_NestedMiddleware_ScopesNested()
    {
        // Arrange
        var outerMiddleware = new ScalarValueValidationMiddleware(async httpContext =>
        {
            ValidationErrorsContext.Current.Should().NotBeNull("outer scope active");
            ValidationErrorsContext.AddError("OuterField", "Outer error");

            // Call inner middleware
            var innerMiddleware = new ScalarValueValidationMiddleware(async _ =>
            {
                ValidationErrorsContext.Current.Should().NotBeNull("inner scope active");

                // Inner scope should not see outer errors (new scope)
                ValidationErrorsContext.AddError("InnerField", "Inner error");
                var innerError = ValidationErrorsContext.GetValidationError();
                innerError!.FieldErrors.Should().ContainSingle()
                    .Which.FieldName.Should().Be("InnerField");

                await Task.CompletedTask;
            });

            await innerMiddleware.InvokeAsync(httpContext);

            // After inner scope, outer scope should still have its error
            var outerError = ValidationErrorsContext.GetValidationError();
            outerError!.FieldErrors.Should().ContainSingle()
                .Which.FieldName.Should().Be("OuterField");
        });

        var context = new DefaultHttpContext();

        // Act
        await outerMiddleware.InvokeAsync(context);

        // Assert
        ValidationErrorsContext.Current.Should().BeNull("all scopes disposed");
    }

    [Fact]
    public async Task InvokeAsync_NextDelegateCalledWithSameContext()
    {
        // Arrange
        HttpContext? capturedContext = null;
        var middleware = new ScalarValueValidationMiddleware(ctx =>
        {
            capturedContext = ctx;
            return Task.CompletedTask;
        });

        var originalContext = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(originalContext);

        // Assert
        capturedContext.Should().BeSameAs(originalContext);
    }

    [Fact]
    public async Task InvokeAsync_PropertyNameContextPreserved()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            // Set a property name
            ValidationErrorsContext.CurrentPropertyName = "TestProperty";

            // Verify it's set
            ValidationErrorsContext.CurrentPropertyName.Should().Be("TestProperty");

            await Task.CompletedTask;

            // Verify it's still set (not cleared by scope)
            ValidationErrorsContext.CurrentPropertyName.Should().Be("TestProperty");
        });

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_MultipleErrors_AllCollected()
    {
        // Arrange
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            // Add multiple errors
            ValidationErrorsContext.AddError("Field1", "Error 1");
            ValidationErrorsContext.AddError("Field2", "Error 2");
            ValidationErrorsContext.AddError("Field3", "Error 3");
            ValidationErrorsContext.AddError("Field1", "Error 1b"); // Another error for Field1

            var error = ValidationErrorsContext.GetValidationError();
            error.Should().NotBeNull();
            error!.FieldErrors.Should().HaveCount(3);

            // Field1 should have 2 errors
            var field1Errors = error.FieldErrors.First(f => f.FieldName == "Field1");
            field1Errors.Details.Should().HaveCount(2);

            await Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);
    }

    [Fact]
    public async Task InvokeAsync_TaskCancellation_ScopeStillDisposed()
    {
        // Arrange
        var cts = new System.Threading.CancellationTokenSource();
        var middleware = new ScalarValueValidationMiddleware(async _ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            await Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.RequestAborted = cts.Token;

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        ValidationErrorsContext.Current.Should().BeNull("scope should be disposed even after cancellation");
    }

    #region BadHttpRequestException Handling Tests

    /// <summary>
    /// Test scalar value type for middleware tests.
    /// Validates that the value starts with "ORD-".
    /// </summary>
    public class OrderCode : ScalarValueObject<OrderCode, string>, IScalarValue<OrderCode, string>
    {
        private OrderCode(string value) : base(value) { }
        public static Result<OrderCode> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "orderCode";
            if (string.IsNullOrWhiteSpace(value))
                return Error.Validation("Order code is required.", field);
            if (!value.StartsWith("ORD-", StringComparison.Ordinal))
                return Error.Validation("Order code must start with 'ORD-'.", field);
            return new OrderCode(value);
        }
    }

    // Helper method signatures used to obtain ParameterInfo via reflection
    private static void ScalarValueParam(OrderCode code) { }
    private static void NonScalarValueParam(int id) { }
    private static void IntOnlyScalarValueParam(IntOnlyScalarValue val) { }

    /// <summary>
    /// An IScalarValue type with only TryCreate(int, string?) — no TryCreate(string, string) overload.
    /// Used to test the CreateFallbackErrors path.
    /// </summary>
    public class IntOnlyScalarValue : IScalarValue<IntOnlyScalarValue, int>
    {
        public int Value { get; }
        private IntOnlyScalarValue(int value) => Value = value;
        public static Result<IntOnlyScalarValue> TryCreate(int value, string? fieldName = null) =>
            value > 0 ? new IntOnlyScalarValue(value) : Error.Validation("Must be positive.", fieldName ?? "value");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForScalarValue_Returns400WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "should contain the rich validation error from TryCreate");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForScalarValueWithQualifiedTypeName_Returns400WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "Trellis.Asp.Tests.OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "qualified type names should still resolve the real scalar parameter");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForScalarValueWithNestedTypeName_Returns400WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "ScalarValueValidationMiddlewareTests+OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "nested type names should still resolve the real scalar parameter");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForNonScalarValue_Rethrows()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(NonScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "id");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "Int32 id" from "abc".""", 400));

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_NoEndpoint_Rethrows()
    {
        // Arrange - no endpoint set on context
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "OrderCode code" from "INVALID".""", 400));

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task InvokeAsync_ReadParameterFailure_Returns400WithErrorDetails()
    {
        // Arrange
        var innerException = new JsonException("JSON deserialization for type 'CreateOrderRequest' was missing required properties including: 'state'.");
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException(
                """Failed to read parameter "CreateOrderRequest order" from the request body as JSON.""",
                400,
                innerException));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("$")[0].GetString()
            .Should().Contain("missing required properties");
    }

    [Fact]
    public async Task InvokeAsync_ReadParameterFailure_NoInnerException_UsesExceptionMessage()
    {
        // Arrange
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException(
                """Failed to read parameter "CreateOrderRequest order" from the request body as JSON.""",
                400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("$")[0].GetString()
            .Should().Contain("Failed to read parameter");
    }

    [Fact]
    public async Task InvokeAsync_OtherBadHttpRequestException_Rethrows()
    {
        // Arrange
        var context = new DefaultHttpContext();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("Some other bad request error", 400));

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ScopeStillDisposed()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "OrderCode code" from "BAD".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        ValidationErrorsContext.Current.Should().BeNull("scope should be disposed after handling the exception");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ScalarValueWithoutStringTryCreate_ReturnsFallbackError()
    {
        // Arrange - IntOnlyScalarValue has no TryCreate(string, string), so GetValidationErrors returns null
        // and the middleware falls back to CreateFallbackErrors
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(IntOnlyScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "val");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "IntOnlyScalarValue val" from "abc".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("val")[0].GetString()
            .Should().Contain("not a valid IntOnlyScalarValue", "should use the fallback error message");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ScalarValueWithoutStringTryCreate_EmptyValue_ReturnsFallbackRequiredError()
    {
        // Arrange - empty value triggers the "required" branch of CreateFallbackErrors
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(IntOnlyScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "val");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "IntOnlyScalarValue val" from "".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("val")[0].GetString()
            .Should().Contain("is required", "should use the fallback 'required' message for empty values");
    }

    #endregion

    #region Helper Methods

    private static DefaultHttpContext CreateContextWithEndpointMetadata(ParameterInfo paramInfo, string paramName)
    {
        var mockMetadata = new Mock<IParameterBindingMetadata>();
        mockMetadata.Setup(m => m.Name).Returns(paramName);
        mockMetadata.Setup(m => m.ParameterInfo).Returns(paramInfo);

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(mockMetadata.Object),
            displayName: "TestEndpoint");

        var context = CreateHttpContextWithServices();
        context.SetEndpoint(endpoint);
        return context;
    }

    private static DefaultHttpContext CreateHttpContextWithServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> ReadResponseBodyAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    #endregion
}