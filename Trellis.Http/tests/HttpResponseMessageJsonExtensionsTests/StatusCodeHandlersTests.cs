using Trellis.Testing;
namespace Trellis.Http.Tests.HttpResponseMessageJsonExtensionsTests;

using System.Net;
using System.Threading.Tasks;
using Trellis;

/// <summary>
/// Tests for specific status code handlers and range handlers.
/// These extensions provide functional error handling for common HTTP status codes.
/// </summary>
public class StatusCodeHandlersTests
{
    #region HandleUnauthorized Tests

    [Fact]
    public void HandleUnauthorized_with_401_status_should_return_failure()
    {
        // Arrange
        var unauthorizedError = new Error.Unauthorized() { Detail = "Authentication required" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Unauthorized);

        // Act
        var result = httpResponseMessage.HandleUnauthorized(unauthorizedError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(unauthorizedError);
    }

    [Fact]
    public void HandleUnauthorized_with_non_401_status_should_return_success()
    {
        // Arrange
        var unauthorizedError = new Error.Unauthorized() { Detail = "Authentication required" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);

        // Act
        var result = httpResponseMessage.HandleUnauthorized(unauthorizedError);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HandleUnauthorizedAsync_with_401_status_should_return_failure()
    {
        // Arrange
        var unauthorizedError = new Error.Unauthorized() { Detail = "Authentication required" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Unauthorized);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleUnauthorizedAsync(unauthorizedError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(unauthorizedError);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HandleUnauthorizedAsync_with_non_401_status_should_return_success(HttpStatusCode statusCode)
    {
        // Arrange
        var unauthorizedError = new Error.Unauthorized() { Detail = "Authentication required" };
        using HttpResponseMessage httpResponseMessage = new(statusCode);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleUnauthorizedAsync(unauthorizedError);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(statusCode);
    }

    #endregion

    #region HandleForbidden Tests

    [Fact]
    public void HandleForbidden_with_403_status_should_return_failure()
    {
        // Arrange
        var forbiddenError = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Forbidden);

        // Act
        var result = httpResponseMessage.HandleForbidden(forbiddenError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(forbiddenError);
    }

    [Fact]
    public void HandleForbidden_with_non_403_status_should_return_success()
    {
        // Arrange
        var forbiddenError = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);

        // Act
        var result = httpResponseMessage.HandleForbidden(forbiddenError);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HandleForbiddenAsync_with_403_status_should_return_failure()
    {
        // Arrange
        var forbiddenError = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Forbidden);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleForbiddenAsync(forbiddenError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(forbiddenError);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task HandleForbiddenAsync_with_non_403_status_should_return_success(HttpStatusCode statusCode)
    {
        // Arrange
        var forbiddenError = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };
        using HttpResponseMessage httpResponseMessage = new(statusCode);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleForbiddenAsync(forbiddenError);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(statusCode);
    }

    #endregion

    #region HandleConflict Tests

    [Fact]
    public void HandleConflict_with_409_status_should_return_failure()
    {
        // Arrange
        var conflictError = new Error.Conflict(null, "conflict") { Detail = "Resource already exists" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Conflict);

        // Act
        var result = httpResponseMessage.HandleConflict(conflictError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(conflictError);
    }

    [Fact]
    public void HandleConflict_with_non_409_status_should_return_success()
    {
        // Arrange
        var conflictError = new Error.Conflict(null, "conflict") { Detail = "Resource already exists" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);

        // Act
        var result = httpResponseMessage.HandleConflict(conflictError);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HandleConflictAsync_with_409_status_should_return_failure()
    {
        // Arrange
        var conflictError = new Error.Conflict(null, "conflict") { Detail = "Resource already exists" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Conflict);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleConflictAsync(conflictError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(conflictError);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task HandleConflictAsync_with_non_409_status_should_return_success(HttpStatusCode statusCode)
    {
        // Arrange
        var conflictError = new Error.Conflict(null, "conflict") { Detail = "Resource already exists" };
        using HttpResponseMessage httpResponseMessage = new(statusCode);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleConflictAsync(conflictError);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(statusCode);
    }

    #endregion

    #region HandleClientError Tests

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void HandleClientError_with_4xx_status_should_return_failure(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.HandleClientError(code => new Error.BadRequest("bad.request") { Detail = $"Client error: {code}" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain(statusCode.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void HandleClientError_with_non_4xx_status_should_return_success(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.HandleClientError(code => new Error.BadRequest("bad.request") { Detail = $"Client error: {code}" });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task HandleClientErrorAsync_with_4xx_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleClientErrorAsync(
            code => new Error.BadRequest("bad.request") { Detail = $"Client error: {code}" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("BadRequest");
    }

    [Fact]
    public void HandleClientError_should_invoke_error_factory_with_correct_status_code()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.NotFound);
        HttpStatusCode? capturedStatusCode = null;

        // Act
        var result = httpResponseMessage.HandleClientError(code =>
        {
            capturedStatusCode = code;
            return new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" };
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        capturedStatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region HandleServerError Tests

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported)]
    public void HandleServerError_with_5xx_status_should_return_failure(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.HandleServerError(code => new Error.ServiceUnavailable() { Detail = $"Server error: {code}" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain(statusCode.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public void HandleServerError_with_non_5xx_status_should_return_success(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.HandleServerError(code => new Error.ServiceUnavailable() { Detail = $"Server error: {code}" });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task HandleServerErrorAsync_with_5xx_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.InternalServerError);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleServerErrorAsync(
            code => new Error.ServiceUnavailable() { Detail = $"Server error: {code}" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("InternalServerError");
    }

    [Fact]
    public void HandleServerError_should_invoke_error_factory_with_correct_status_code()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadGateway);
        HttpStatusCode? capturedStatusCode = null;

        // Act
        var result = httpResponseMessage.HandleServerError(code =>
        {
            capturedStatusCode = code;
            return new Error.ServiceUnavailable() { Detail = "Service unavailable" };
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        capturedStatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    #endregion

    #region EnsureSuccess Tests

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public void EnsureSuccess_with_success_status_should_return_success(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.EnsureSuccess();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public void EnsureSuccess_with_error_status_should_return_failure(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.EnsureSuccess();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain(statusCode.ToString());
    }

    [Fact]
    public void EnsureSuccess_with_custom_error_factory_should_use_factory()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest);

        // Act
        var result = httpResponseMessage.EnsureSuccess(code => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"Custom error for {code}" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.UnprocessableContent>();
        result.UnwrapError().Detail.Should().Contain("Custom error");
        result.UnwrapError().Detail.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task EnsureSuccessAsync_with_success_status_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.EnsureSuccessAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnsureSuccessAsync_with_error_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.InternalServerError);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.EnsureSuccessAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task EnsureSuccessAsync_with_custom_error_factory_should_use_factory()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.ServiceUnavailable);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.EnsureSuccessAsync(
            code => new Error.ServiceUnavailable() { Detail = $"Service down: {code}" });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain("Service down");
        result.UnwrapError().Detail.Should().Contain("ServiceUnavailable");
    }

    #endregion

    #region Composition Tests

    [Fact]
    public void Multiple_status_handlers_can_be_chained()
    {
        // Arrange
        var unauthorizedError = new Error.Unauthorized() { Detail = "Auth required" };
        var forbiddenError = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Forbidden);

        // Act
        var result = httpResponseMessage
            .HandleUnauthorized(unauthorizedError)
            .Bind(r => r.HandleForbidden(forbiddenError));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(forbiddenError);
    }

    [Fact]
    public async Task Status_handlers_can_be_chained_with_async()
    {
        // Arrange
        var unauthorizedError = new Error.Unauthorized() { Detail = "Auth required" };
        var forbiddenError = new Error.Forbidden("authorization.forbidden") { Detail = "Access denied" };
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Unauthorized);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage
            .HandleUnauthorizedAsync(unauthorizedError);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(unauthorizedError);
    }

    #endregion
}
