using Trellis.Testing;
namespace Trellis.Http.Tests.HttpResponseMessageJsonExtensionsTests;

using System.Net;
using System.Threading.Tasks;
using Trellis;

/// <summary>
/// Tests for Result&lt;HttpResponseMessage&gt; and Task&lt;Result&lt;HttpResponseMessage&gt;&gt; overloads
/// defined in HttpResponseResultExtensions.cs.
/// These overloads enable fluent chaining without explicit Bind calls.
/// </summary>
public class ResultOverloadTests
{
    #region HandleNotFound — Result overloads

    [Fact]
    public void HandleNotFound_Result_with_404_should_return_failure()
    {
        // Arrange
        var notFoundError = Error.NotFound("Not found");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.NotFound);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleNotFound(notFoundError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(notFoundError);
    }

    [Fact]
    public void HandleNotFound_Result_with_200_should_return_success()
    {
        // Arrange
        var notFoundError = Error.NotFound("Not found");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleNotFound(notFoundError);

        // Assert
        actual.IsSuccess.Should().BeTrue();
        actual.Unwrap().StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void HandleNotFound_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.Unauthorized("Auth required");
        var notFoundError = Error.NotFound("Not found");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.HandleNotFound(notFoundError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task HandleNotFoundAsync_TaskResult_with_404_should_return_failure()
    {
        // Arrange
        var notFoundError = Error.NotFound("Not found");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.NotFound);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleNotFoundAsync(notFoundError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(notFoundError);
    }

    [Fact]
    public async Task HandleNotFoundAsync_TaskResult_with_200_should_return_success()
    {
        // Arrange
        var notFoundError = Error.NotFound("Not found");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleNotFoundAsync(notFoundError);

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleNotFoundAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.Unauthorized("Auth required");
        var notFoundError = Error.NotFound("Not found");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.HandleNotFoundAsync(notFoundError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region HandleUnauthorized — Result overloads

    [Fact]
    public void HandleUnauthorized_Result_with_401_should_return_failure()
    {
        // Arrange
        var unauthorizedError = Error.Unauthorized("Auth required");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Unauthorized);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleUnauthorized(unauthorizedError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(unauthorizedError);
    }

    [Fact]
    public void HandleUnauthorized_Result_with_200_should_return_success()
    {
        // Arrange
        var unauthorizedError = Error.Unauthorized("Auth required");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleUnauthorized(unauthorizedError);

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HandleUnauthorized_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var unauthorizedError = Error.Unauthorized("Auth required");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.HandleUnauthorized(unauthorizedError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task HandleUnauthorizedAsync_TaskResult_with_401_should_return_failure()
    {
        // Arrange
        var unauthorizedError = Error.Unauthorized("Auth required");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Unauthorized);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleUnauthorizedAsync(unauthorizedError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(unauthorizedError);
    }

    [Fact]
    public async Task HandleUnauthorizedAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var unauthorizedError = Error.Unauthorized("Auth required");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.HandleUnauthorizedAsync(unauthorizedError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region HandleForbidden — Result overloads

    [Fact]
    public void HandleForbidden_Result_with_403_should_return_failure()
    {
        // Arrange
        var forbiddenError = Error.Forbidden("Access denied");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Forbidden);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleForbidden(forbiddenError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(forbiddenError);
    }

    [Fact]
    public void HandleForbidden_Result_with_200_should_return_success()
    {
        // Arrange
        var forbiddenError = Error.Forbidden("Access denied");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleForbidden(forbiddenError);

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HandleForbidden_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var forbiddenError = Error.Forbidden("Access denied");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.HandleForbidden(forbiddenError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task HandleForbiddenAsync_TaskResult_with_403_should_return_failure()
    {
        // Arrange
        var forbiddenError = Error.Forbidden("Access denied");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Forbidden);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleForbiddenAsync(forbiddenError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(forbiddenError);
    }

    [Fact]
    public async Task HandleForbiddenAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var forbiddenError = Error.Forbidden("Access denied");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.HandleForbiddenAsync(forbiddenError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region HandleConflict — Result overloads

    [Fact]
    public void HandleConflict_Result_with_409_should_return_failure()
    {
        // Arrange
        var conflictError = Error.Conflict("Already exists");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Conflict);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleConflict(conflictError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(conflictError);
    }

    [Fact]
    public void HandleConflict_Result_with_200_should_return_success()
    {
        // Arrange
        var conflictError = Error.Conflict("Already exists");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleConflict(conflictError);

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HandleConflict_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var conflictError = Error.Conflict("Already exists");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.HandleConflict(conflictError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task HandleConflictAsync_TaskResult_with_409_should_return_failure()
    {
        // Arrange
        var conflictError = Error.Conflict("Already exists");
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Conflict);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleConflictAsync(conflictError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(conflictError);
    }

    [Fact]
    public async Task HandleConflictAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var conflictError = Error.Conflict("Already exists");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.HandleConflictAsync(conflictError);

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region HandleClientError — Result overloads

    [Fact]
    public void HandleClientError_Result_with_4xx_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleClientError(code => Error.BadRequest($"Client error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Detail.Should().Contain("BadRequest");
    }

    [Fact]
    public void HandleClientError_Result_with_200_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleClientError(code => Error.BadRequest($"Client error: {code}"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HandleClientError_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.Unauthorized("Auth required");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.HandleClientError(code => Error.BadRequest($"Client error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task HandleClientErrorAsync_TaskResult_with_4xx_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleClientErrorAsync(code => Error.BadRequest($"Client error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Detail.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task HandleClientErrorAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.Unauthorized("Auth required");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.HandleClientErrorAsync(code => Error.BadRequest($"Client error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region HandleServerError — Result overloads

    [Fact]
    public void HandleServerError_Result_with_5xx_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.InternalServerError);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleServerError(code => Error.ServiceUnavailable($"Server error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Detail.Should().Contain("InternalServerError");
    }

    [Fact]
    public void HandleServerError_Result_with_200_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.HandleServerError(code => Error.ServiceUnavailable($"Server error: {code}"));

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HandleServerError_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.HandleServerError(code => Error.ServiceUnavailable($"Server error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task HandleServerErrorAsync_TaskResult_with_5xx_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.InternalServerError);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.HandleServerErrorAsync(code => Error.ServiceUnavailable($"Server error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Detail.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task HandleServerErrorAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.HandleServerErrorAsync(code => Error.ServiceUnavailable($"Server error: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region EnsureSuccess — Result overloads

    [Fact]
    public void EnsureSuccess_Result_with_200_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.EnsureSuccess();

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureSuccess_Result_with_error_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.InternalServerError);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.EnsureSuccess();

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Detail.Should().Contain("InternalServerError");
    }

    [Fact]
    public void EnsureSuccess_Result_with_custom_factory_should_use_factory()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest);
        var result = Result.Ok(httpResponseMessage);

        // Act
        var actual = result.EnsureSuccess(code => Error.Validation($"Custom: {code}"));

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().BeOfType<ValidationError>();
        actual.UnwrapError().Detail.Should().Contain("Custom");
    }

    [Fact]
    public void EnsureSuccess_Result_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var result = Result.Fail<HttpResponseMessage>(originalError);

        // Act
        var actual = result.EnsureSuccess();

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    [Fact]
    public async Task EnsureSuccessAsync_TaskResult_with_200_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.EnsureSuccessAsync();

        // Assert
        actual.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureSuccessAsync_TaskResult_with_error_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.InternalServerError);
        var resultTask = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var actual = await resultTask.EnsureSuccessAsync();

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Detail.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task EnsureSuccessAsync_TaskResult_already_failed_should_preserve_original_error()
    {
        // Arrange
        var originalError = Error.NotFound("Not found");
        var resultTask = Task.FromResult(Result.Fail<HttpResponseMessage>(originalError));

        // Act
        var actual = await resultTask.EnsureSuccessAsync();

        // Assert
        actual.IsFailure.Should().BeTrue();
        actual.UnwrapError().Should().Be(originalError);
    }

    #endregion

    #region Fluent Chaining — composition tests

    [Fact]
    public void Multiple_handlers_chain_fluently_without_Bind()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Forbidden);

        // Act — no Bind required
        var result = httpResponseMessage
            .HandleUnauthorized(Error.Unauthorized("Auth required"))
            .HandleForbidden(Error.Forbidden("Access denied"))
            .HandleConflict(Error.Conflict("Already exists"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public void Multiple_handlers_chain_fluently_success_passes_through()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);

        // Act
        var result = httpResponseMessage
            .HandleUnauthorized(Error.Unauthorized("Auth required"))
            .HandleForbidden(Error.Forbidden("Access denied"))
            .HandleConflict(Error.Conflict("Already exists"))
            .HandleClientError(code => Error.BadRequest($"Client error: {code}"))
            .HandleServerError(code => Error.ServiceUnavailable($"Server error: {code}"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void First_matching_handler_wins_in_chain()
    {
        // Arrange — 401 should be caught by HandleUnauthorized, not HandleClientError
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Unauthorized);

        // Act
        var result = httpResponseMessage
            .HandleUnauthorized(Error.Unauthorized("Auth required"))
            .HandleForbidden(Error.Forbidden("Access denied"))
            .HandleClientError(code => Error.BadRequest($"Client error: {code}"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<UnauthorizedError>();
        result.UnwrapError().Detail.Should().Be("Auth required");
    }

    [Fact]
    public async Task Async_chain_works_end_to_end()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.Conflict);
        var responseTask = Task.FromResult(httpResponseMessage);

        // Act
        var result = await responseTask
            .HandleUnauthorizedAsync(Error.Unauthorized("Auth required"))
            .HandleForbiddenAsync(Error.Forbidden("Access denied"))
            .HandleConflictAsync(Error.Conflict("Already exists"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<ConflictError>();
    }

    #endregion
}
