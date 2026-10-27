namespace Trellis.Asp.Tests;

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Trellis;
using Trellis.Asp.Validation;
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
            ValidationErrorsContext.GetUnprocessableContent().Should().BeNull();
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
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/TestField");

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
    public async Task InvokeAsync_UnrelatedProtocolFailureWithCollectedErrors_Preserves400()
    {
        var context = CreateHttpContextWithServices();
        var middleware = new ScalarValueValidationMiddleware(_ =>
        {
            ValidationErrorsContext.AddError("field", "Invalid field.");
            throw new BadHttpRequestException("Protocol failure.", StatusCodes.Status400BadRequest);
        });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ValidatedRoot_UnrelatedProtocolFailure_Preserves400(bool optionalBody)
    {
        var context = CreateHttpContextWithServices();
        var accepts = new Mock<IAcceptsMetadata>();
        accepts.Setup(m => m.RequestType).Returns(typeof(Trellis.Primitives.Money));
        accepts.Setup(m => m.IsOptional).Returns(optionalBody);
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(accepts.Object), "Root value object"));
        var middleware = new ScalarValueValidationMiddleware(_ =>
        {
            ValidationErrorsContext.RecordReadFailure(string.Empty);
            ValidationErrorsContext.AddBodyError("amount", ValidationCodes.ValueNotNull, "Invalid amount.");
            throw new BadHttpRequestException("Protocol failure.", StatusCodes.Status400BadRequest);
        });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
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

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error!.Fields.Items.Should().ContainSingle("each request should have isolated scope");
            error.Fields[0].Field.Path.Should().Be($"/Field{requestCount}");

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
            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().ContainSingle("scope should be isolated per async context");
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
                var innerError = ValidationErrorsContext.GetUnprocessableContent();
                innerError!.Fields.Items.Should().ContainSingle()
                    .Which.Field.Path.Should().Be("/InnerField");

                await Task.CompletedTask;
            });

            await innerMiddleware.InvokeAsync(httpContext);

            // After inner scope, outer scope should still have its error
            var outerError = ValidationErrorsContext.GetUnprocessableContent();
            outerError!.Fields.Items.Should().ContainSingle()
                .Which.Field.Path.Should().Be("/OuterField");
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

            var error = ValidationErrorsContext.GetUnprocessableContent();
            error.Should().NotBeNull();
            error!.Fields.Items.Should().HaveCount(4);

            // Field1 should appear twice (2 distinct messages on Field1)
            var field1Errors = error.Fields.Items.Where(f => f.Field.Path == "/Field1").ToList();
            field1Errors.Should().HaveCount(2);

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
                return Result.Fail<Asp.Tests.ScalarValueValidationMiddlewareTests.OrderCode>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Order code is required." })));
            if (!value.StartsWith("ORD-", StringComparison.Ordinal))
                return Result.Fail<Asp.Tests.ScalarValueValidationMiddlewareTests.OrderCode>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Order code must start with 'ORD-'." })));
            return Result.Ok(new OrderCode(value));
        }
    }

    public sealed class StructuredOrderCode : ScalarValueObject<StructuredOrderCode, string>, IScalarValue<StructuredOrderCode, string>
    {
        private StructuredOrderCode(string value) : base(value) { }

        public static Result<StructuredOrderCode> TryCreate(string? value, string? fieldName = null)
        {
            if (value is null || !value.StartsWith("ORD-", StringComparison.Ordinal))
                return Result.Fail<StructuredOrderCode>(Error.InvalidInput.ForField(
                    "order.code-prefix", fieldName ?? "code",
                    args: ValidationArgs.Of("prefix", "ORD-"),
                    detail: "Order code must start with 'ORD-'."));

            return Result.Ok(new StructuredOrderCode(value));
        }

        public static bool TryParse(string? value, [MaybeNullWhen(false)] out StructuredOrderCode result) =>
            TryCreate(value).TryGetValue(out result);
    }

    // Helper method signatures used to obtain ParameterInfo via reflection
    private static void ScalarValueParam(OrderCode code) { }
    private static void StructuredScalarValueParam(StructuredOrderCode code) { }
    private static void MaybeScalarValueParam(Maybe<OrderCode> code) { }
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
            value > 0 ? Result.Ok(new IntOnlyScalarValue(value)) : Result.Fail<IntOnlyScalarValue>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "value"), ValidationCodes.Unspecified) { Detail = "Must be positive." })));
        public static Result<IntOnlyScalarValue> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForScalarValue_Returns422WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code", "INVALID");
        context.Request.Path = "/middleware-binding-422";

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "should contain the rich validation error from TryCreate");

        // RFC 9457 §3.1: instance is populated from the request path on the binder-level
        // 422 branch (WriteValidationProblemAsync). Pins the middleware emission point
        // independently of the JSON-deserialization branches.
        problem.GetProperty("instance").GetString().Should().Be("/middleware-binding-422");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_ScalarBindingFailure_StructuredError_PreservesCodeArgsAndLocation(bool fromRoute)
    {
        var source = StructuredOrderCode.TryCreate("INVALID", "code");
        source.TryGetError(out var sourceError).Should().BeTrue();
        var sourceViolation = sourceError.Should().BeOfType<Error.InvalidInput>().Which
            .Fields.Items.Should().ContainSingle().Which;
        sourceViolation.ReasonCode.Should().Be("order.code-prefix");
        sourceViolation.Args!["prefix"].Should().Be(new ValidationArgValue.Text("ORD-"));

        var parameter = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(StructuredScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];
        var context = CreateContextWithEndpointMetadata(parameter, "code", fromRoute ? "INVALID" : null);
        context.Request.Path = "/middleware-binding-structure";
        context.Request.QueryString = new QueryString(fromRoute ? "?code=ORD-123" : "?code=INVALID");
        BoundViolationCollector.GetError(context).Should().BeNull();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("Parameter binding failed.", 400));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(422);
        using var problem = JsonDocument.Parse(await ReadResponseBodyAsync(context));
        AssertStructuredOrderCodeProblem(problem.RootElement, fromRoute ? "path" : "query",
            context.Request.Path + context.Request.QueryString);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task InvokeAsync_MinimalApiScalarBinding_ExceptionPolicy_PreservesStructuredFailureOrBare400(
        bool fromRoute, bool throwOnBadRequest)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddTrellisAspWithScalarValidation();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = throwOnBadRequest);
        await using var app = builder.Build();
        app.UseScalarValueValidation();

        List<int> bindingFailureStatuses = [];
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (BadHttpRequestException exception)
            {
                bindingFailureStatuses.Add(exception.StatusCode);
                throw;
            }
        });

        var handlerCalls = 0;
        app.MapGet(fromRoute ? "/middleware-binding/route/{code}" : "/middleware-binding/query",
            (StructuredOrderCode code) =>
            {
                handlerCalls++;
                return Results.Ok(code.Value);
            }).WithScalarValueValidation();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        var invalidPath = fromRoute
            ? "/middleware-binding/route/INVALID?code=ORD-123"
            : "/middleware-binding/query?code=INVALID";

        using var response = await client.GetAsync(invalidPath, TestContext.Current.CancellationToken);
        handlerCalls.Should().Be(0);

        var validPath = fromRoute
            ? "/middleware-binding/route/ORD-123?code=INVALID"
            : "/middleware-binding/query?code=ORD-123";
        using var validResponse = await client.GetAsync(validPath, TestContext.Current.CancellationToken);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonSerializer.Deserialize<string>(await validResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("ORD-123");
        handlerCalls.Should().Be(1);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (!throwOnBadRequest)
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            bindingFailureStatuses.Should().BeEmpty();
            body.Should().BeEmpty();
            return;
        }

        bindingFailureStatuses.Should().Equal([400]);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(body);
        AssertStructuredOrderCodeProblem(problem.RootElement, fromRoute ? "path" : "query", invalidPath);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task InvokeAsync_MinimalApiScalarBinding_DeclaredSource_PreservesBoundValueAndName(
        bool fromRoute, bool aliased)
    {
        List<int> bindingFailureStatuses = [];
        await using var app = CreateScalarBindingApp(bindingFailureStatuses);
        var handlerCalls = 0;
        Microsoft.AspNetCore.Http.IResult Handle(StructuredOrderCode code)
        {
            handlerCalls++;
            return Results.Ok(code.Value);
        }

        var parameterName = aliased ? "orderCode" : "code";
        Delegate handler = (fromRoute, aliased) switch
        {
            (true, true) => ([FromRoute(Name = "orderCode")] StructuredOrderCode code) => Handle(code),
            (true, false) => ([FromRoute] StructuredOrderCode code) => Handle(code),
            (false, true) => ([FromQuery(Name = "orderCode")] StructuredOrderCode code) => Handle(code),
            (false, false) => ([FromQuery] StructuredOrderCode code) => Handle(code),
        };
        app.MapGet($"/middleware-metadata/{{{(fromRoute ? parameterName : "code")}}}", handler)
            .WithScalarValueValidation();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        var validPath = fromRoute
            ? "/middleware-metadata/ORD-123?code=INVALID&orderCode=INVALID"
            : $"/middleware-metadata/INVALID?{parameterName}=ORD-123{(aliased ? "&code=INVALID" : string.Empty)}";
        using var validResponse = await client.GetAsync(validPath, TestContext.Current.CancellationToken);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonSerializer.Deserialize<string>(await validResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("ORD-123");
        handlerCalls.Should().Be(1);
        handlerCalls = 0;

        var invalidPath = fromRoute
            ? "/middleware-metadata/INVALID?code=ORD-123&orderCode=ORD-123"
            : $"/middleware-metadata/ORD-123?{parameterName}=INVALID{(aliased ? "&code=ORD-123" : string.Empty)}";
        using var response = await client.GetAsync(invalidPath, TestContext.Current.CancellationToken);

        handlerCalls.Should().Be(0);
        bindingFailureStatuses.Should().Equal([400]);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertStructuredOrderCodeProblem(problem.RootElement, fromRoute ? "path" : "query", invalidPath, parameterName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_MinimalApiScalarBinding_InheritedSource_PreservesBoundValueAndName(bool fromRoute)
    {
        List<int> bindingFailureStatuses = [];
        await using var app = CreateScalarBindingApp(bindingFailureStatuses);
        var handler = new OverridingScalarBindingHandler();
        Func<StructuredOrderCode, Microsoft.AspNetCore.Http.IResult> endpointHandler =
            fromRoute ? handler.BindRoute : handler.BindQuery;
        app.MapGet(fromRoute ? "/middleware-metadata/inherited/{orderCode}" : "/middleware-metadata/inherited",
            endpointHandler).WithScalarValueValidation();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        var validPath = fromRoute
            ? "/middleware-metadata/inherited/ORD-123?code=INVALID&orderCode=INVALID"
            : "/middleware-metadata/inherited?code=ORD-123";
        using var validResponse = await client.GetAsync(validPath, TestContext.Current.CancellationToken);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonSerializer.Deserialize<string>(await validResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("ORD-123");
        handler.HandlerCalls.Should().Be(1);
        handler.HandlerCalls = 0;

        var invalidPath = fromRoute
            ? "/middleware-metadata/inherited/INVALID?code=ORD-123&orderCode=ORD-123"
            : "/middleware-metadata/inherited?code=INVALID";
        using var response = await client.GetAsync(invalidPath, TestContext.Current.CancellationToken);

        handler.HandlerCalls.Should().Be(0);
        bindingFailureStatuses.Should().Equal([400]);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertStructuredOrderCodeProblem(problem.RootElement, fromRoute ? "path" : "query", invalidPath,
            fromRoute ? "orderCode" : "code");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_MinimalApiScalarBinding_NonQuerySource_DoesNotRevalidateQuery(bool fromHeader)
    {
        List<int> bindingFailureStatuses = [];
        await using var app = CreateScalarBindingApp(bindingFailureStatuses);
        var handlerCalls = 0;
        Microsoft.AspNetCore.Http.IResult Handle(StructuredOrderCode code, int count)
        {
            count.Should().Be(1);
            handlerCalls++;
            return Results.Ok(code.Value);
        }

        Delegate handler = fromHeader
            ? ([FromHeader(Name = "X-Order-Code")] StructuredOrderCode code, int count) => Handle(code, count)
            : ([FromBody] StructuredOrderCode code, int count) => Handle(code, count);
        app.MapPost("/middleware-metadata/other-source", handler).WithScalarValueValidation();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Order-Code", "ORD-123");

        using var validResponse = await client.PostAsJsonAsync(
            "/middleware-metadata/other-source?code=INVALID&count=1", "ORD-123", TestContext.Current.CancellationToken);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonSerializer.Deserialize<string>(await validResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("ORD-123");
        handlerCalls.Should().Be(1);
        handlerCalls = 0;

        var act = async () => await client.PostAsJsonAsync(
            "/middleware-metadata/other-source?code=INVALID&count=INVALID", "ORD-123", TestContext.Current.CancellationToken);
        var failure = await act.Should().ThrowAsync<BadHttpRequestException>();

        failure.Which.StatusCode.Should().Be(400);
        handlerCalls.Should().Be(0);
        bindingFailureStatuses.Should().Equal([400]);
    }

    [Fact]
    public async Task InvokeAsync_MinimalApiScalarBinding_InferredQuery_IgnoresUnboundRouteValue()
    {
        List<int> bindingFailureStatuses = [];
        await using var app = CreateScalarBindingApp(bindingFailureStatuses);
        app.Use((context, next) =>
        {
            context.Request.RouteValues["code"] = "ORD-ROUTE";
            return next(context);
        });
        var handlerCalls = 0;
        app.MapGet("/middleware-metadata/inferred", (StructuredOrderCode code) =>
        {
            handlerCalls++;
            return Results.Ok(code.Value);
        }).WithScalarValueValidation();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        using var validResponse = await client.GetAsync(
            "/middleware-metadata/inferred?code=ORD-123", TestContext.Current.CancellationToken);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonSerializer.Deserialize<string>(await validResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be("ORD-123");
        handlerCalls.Should().Be(1);
        handlerCalls = 0;

        const string invalidPath = "/middleware-metadata/inferred?code=INVALID";
        using var response = await client.GetAsync(invalidPath, TestContext.Current.CancellationToken);

        handlerCalls.Should().Be(0);
        bindingFailureStatuses.Should().Equal([400]);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertStructuredOrderCodeProblem(problem.RootElement, "query", invalidPath);
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForScalarValueWithQualifiedTypeName_Returns422WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code", "INVALID");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "Trellis.Asp.Tests.OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "qualified type names should still resolve the real scalar parameter");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForScalarValueWithNestedTypeName_Returns422WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code", "INVALID");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "ScalarValueValidationMiddlewareTests+OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "nested type names should still resolve the real scalar parameter");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForMaybeScalarValue_Returns422WithRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(MaybeScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code", "INVALID");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "Maybe<OrderCode> code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "Maybe<T> scalar parameters should surface the wrapped scalar validation error");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ForNonScalarValue_Rethrows()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(NonScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "id", "abc");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "Int32 id" from "abc".""", 400));

        // Act
        var act = async () => await middleware.InvokeAsync(context);

        // Assert
        await act.Should().ThrowAsync<BadHttpRequestException>();
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_NoEndpoint_ReturnsGeneric400()
    {
        // Arrange - no endpoint set on context
        var context = CreateHttpContextWithServices();
        context.Request.Path = "/middleware-generic-400";

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "OrderCode code" from "INVALID".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()
            .Should().Be("The request was invalid.");

        // RFC 9457 §3.1: instance is populated from the request path on the generic-400
        // fallback branch (WriteGenericBadRequestAsync). Pins the last of the four
        // middleware emission points so a regression is caught independently of the other
        // three branches and of the MVC filter and ResponseFailureWriter coverage.
        problem.GetProperty("instance").GetString().Should().Be("/middleware-generic-400");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_DoesNotDependOnExceptionMessageFormat_ReturnsRichErrors()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code", "INVALID");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("Proxy error: Failed to bind parameter happened upstream before request processing.", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("code")[0].GetString()
            .Should().Contain("ORD-", "endpoint metadata and raw route values should drive validation");
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
        problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()
            .Should().Be("The request body contains invalid JSON.");
    }

    [Fact]
    public async Task InvokeAsync_ReadParameterFailure_DoesNotLeakJsonExceptionInternals()
    {
        var innerException = new JsonException(
            "The JSON value could not be converted to System.Int32. Path: $.age | LineNumber: 0 | BytePositionInLine: 14");
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException(
                """Failed to read parameter "CreateOrderRequest order" from the request body as JSON.""",
                400,
                innerException));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        var errorMessage = problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()!;
        errorMessage.Should().NotContain("System.Int32",
            "internal type names from JsonException should not be exposed to clients");
    }

    [Fact]
    public async Task InvokeAsync_ReadParameterFailure_TrellisJsonValidationException_SurfacesMessageAndPath()
    {
        // Trellis converters (e.g. MoneyJsonConverter) throw TrellisJsonValidationException with
        // a curated, client-safe message. The middleware MUST surface that message and the JsonPath
        // so callers can see *why* their structured-VO payload was rejected.
        // Note: JsonException.Path is auto-populated by System.Text.Json based on the reader
        // state when the exception bubbles up through the deserialization stack — no need to
        // pass it explicitly from this converter.
        var innerException = new Trellis.TrellisJsonValidationException("Amount cannot be negative.");
        var context = CreateHttpContextWithServices();
        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException(
                """Failed to read parameter "OpenAccountRequest request" from the request body as JSON.""",
                400,
                innerException));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        var errors = problem.GetProperty("errors");
        // No JSON path was set on the exception (STJ would normally fill it during deserialization),
        // so the middleware falls back to the empty-string MVC root key — but the message MUST
        // come from the exception.
        errors.GetProperty(string.Empty)[0].GetString()
            .Should().Be("Amount cannot be negative.");
    }

    [Fact]
    public async Task InvokeAsync_ReadParameterFailure_NoInnerException_UsesGenericMessage()
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
        problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()
            .Should().Be("The request was invalid.");
    }

    [Fact]
    public async Task InvokeAsync_ReadFailure_MessageContainsSubstringButDoesNotMatchFormat_ReturnsGeneric400()
    {
        // Arrange
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("Gateway failure: Failed to read parameter after the request body was already rejected.", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()
            .Should().Be("The request was invalid.", "should not leak ex.Message to clients");
    }

    [Fact]
    public async Task InvokeAsync_OtherBadHttpRequestException_ReturnsGeneric400()
    {
        // Arrange
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("Some other bad request error", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()
            .Should().Be("The request was invalid.", "should not leak ex.Message to clients");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ScopeStillDisposed()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(ScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "code", "BAD");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "OrderCode code" from "BAD".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        ValidationErrorsContext.Current.Should().BeNull("scope should be disposed after handling the exception");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ScalarValueWithoutStringTryCreate_ReturnsRichValidationError()
    {
        // Arrange - IntOnlyScalarValue has TryCreate(string, string) that throws NotImplementedException,
        // so GetUnprocessableContents returns null and the middleware falls back to CreateFallbackErrors.
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(IntOnlyScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "val", "0");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "IntOnlyScalarValue val" from "0".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert - falls through from the unusable string overload to primitive TryCreate.
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("val")[0].GetString()
            .Should().Be("Must be positive.");
    }

    [Fact]
    public async Task InvokeAsync_BindingFailure_ScalarValueWithoutStringTryCreate_EmptyValue_ReturnsFallbackRequiredError()
    {
        // Arrange - empty value triggers the "required" branch of CreateFallbackErrors
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(IntOnlyScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "val", "");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "IntOnlyScalarValue val" from "".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty("val")[0].GetString()
            .Should().Contain("is required", "should use the fallback 'required' message for empty values");
    }

    #endregion

    #region Security Tests — Information Exposure Prevention

    [Fact]
    public async Task InvokeAsync_GenericBadRequest_DoesNotLeakExceptionMessage()
    {
        // Arrange — unrecognized 400 format triggers WriteGenericBadRequestAsync
        var context = CreateHttpContextWithServices();

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("Proxy error: internal binding detail about System.Int32 parameter.", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(400);
        var body = await ReadResponseBodyAsync(context);
        body.Should().NotContain("Proxy error", "ex.Message should not be exposed to clients");
        body.Should().NotContain("System.Int32", "internal type info should not be exposed");
        body.Should().NotContain("binding detail", "internal details should not be exposed");

        var problem = JsonSerializer.Deserialize<JsonElement>(body);
        problem.GetProperty("errors").GetProperty(string.Empty)[0].GetString()
            .Should().Be("The request was invalid.");
    }

    [Fact]
    public async Task InvokeAsync_FallbackErrors_DoesNotReflectInvalidValue()
    {
        // Arrange — IntOnlyScalarValue has no TryCreate(string), so fallback path is used
        // The invalidValue should NOT be reflected back in the error message
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(IntOnlyScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "val", "<script>alert('xss')</script>");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "IntOnlyScalarValue val" from "<script>alert('xss')</script>".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        body.Should().NotContain("<script>", "user input should not be reflected in error responses");
        body.Should().NotContain("alert", "user input should not be reflected in error responses");
    }

    [Fact]
    public async Task InvokeAsync_FallbackErrors_DoesNotLeakInternalTypeName()
    {
        // Arrange
        var paramInfo = typeof(ScalarValueValidationMiddlewareTests)
            .GetMethod(nameof(IntOnlyScalarValueParam), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];

        var context = CreateContextWithEndpointMetadata(paramInfo, "val", "bad");

        var middleware = new ScalarValueValidationMiddleware(_ =>
            throw new BadHttpRequestException("""Failed to bind parameter "IntOnlyScalarValue val" from "bad".""", 400));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422);
        var body = await ReadResponseBodyAsync(context);
        body.Should().NotContain("IntOnlyScalarValue", "internal type names should not be exposed to clients");
    }

    #endregion

    #region Helper Methods

    private class BaseScalarBindingHandler
    {
        public int HandlerCalls { get; set; }

        public virtual Microsoft.AspNetCore.Http.IResult BindRoute([FromRoute(Name = "orderCode")] StructuredOrderCode code) =>
            Handle(code);

        public virtual Microsoft.AspNetCore.Http.IResult BindQuery([FromQuery] StructuredOrderCode code) =>
            Handle(code);

        private Microsoft.AspNetCore.Http.IResult Handle(StructuredOrderCode code)
        {
            HandlerCalls++;
            return Results.Ok(code.Value);
        }
    }

    private sealed class OverridingScalarBindingHandler : BaseScalarBindingHandler
    {
        public override Microsoft.AspNetCore.Http.IResult BindRoute(StructuredOrderCode code) => base.BindRoute(code);
        public override Microsoft.AspNetCore.Http.IResult BindQuery(StructuredOrderCode code) => base.BindQuery(code);
    }

    private static WebApplication CreateScalarBindingApp(List<int> bindingFailureStatuses)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddTrellisAspWithScalarValidation();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        var app = builder.Build();
        app.UseScalarValueValidation();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (BadHttpRequestException exception)
            {
                bindingFailureStatuses.Add(exception.StatusCode);
                throw;
            }
        });
        return app;
    }

    private static void AssertStructuredOrderCodeProblem(
        JsonElement problem, string location, string instance, string parameterName = "code")
    {
        problem.GetProperty("status").GetInt32().Should().Be(422);
        problem.GetProperty("instance").GetString().Should().Be(instance);
        problem.GetProperty("code").GetString().Should().Be("error.unspecified");
        problem.GetProperty("kind").GetString().Should().Be("unprocessable-content");
        problem.GetProperty("errors").GetProperty(parameterName).EnumerateArray().Should().ContainSingle()
            .Which.GetString().Should().Be("Order code must start with 'ORD-'.");
        problem.TryGetProperty("fieldViolations", out var fields).Should().BeTrue(
            "the scalar factory supplied a coded field violation with typed arguments");
        var violation = fields.EnumerateArray().Should().ContainSingle().Which;
        violation.GetProperty("code").GetString().Should().Be("order.code-prefix");
        violation.GetProperty("detail").GetString().Should().Be("Order code must start with 'ORD-'.");
        var prefix = violation.GetProperty("args").GetProperty("prefix");
        prefix.ValueKind.Should().Be(JsonValueKind.String);
        prefix.GetString().Should().Be("ORD-");
        violation.GetProperty("location").GetProperty("in").GetString().Should().Be(location);
        violation.GetProperty("location").GetProperty("name").GetString().Should().Be(parameterName);
        violation.GetProperty("location").TryGetProperty("pointer", out _).Should().BeFalse();
    }

    private static DefaultHttpContext CreateContextWithEndpointMetadata(
        ParameterInfo paramInfo,
        string paramName,
        string? rawValue = null)
    {
        var mockMetadata = new Mock<IParameterBindingMetadata>();
        mockMetadata.Setup(m => m.Name).Returns(paramName);
        mockMetadata.Setup(m => m.ParameterInfo).Returns(paramInfo);

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(mockMetadata.Object),
            displayName: "TestEndpoint");

        var context = CreateHttpContextWithServices();
        if (rawValue is not null)
            context.Request.RouteValues[paramName] = rawValue;

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