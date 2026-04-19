namespace Trellis.Results.Tests;

using FluentAssertions;
using Trellis.Testing;
using Xunit;

public class MatchErrorTests
{
    [Fact]
    public void MatchError_WithSuccessResult_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Success: 42");
    }

    [Fact]
    public void MatchError_WithValidationError_CallsOnValidation()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Validation("Invalid email", "email"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onValidation: err => $"Validation: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Validation: Invalid email");
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public void MatchError_WithNotFoundError_CallsOnNotFound()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("User not found"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onNotFound: err => $"NotFound: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("NotFound: User not found");
        result.Should().BeFailureOfType<NotFoundError>()
            .Which.Should().HaveDetail("User not found");
    }

    [Fact]
    public void MatchError_WithConflictError_CallsOnConflict()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Conflict("Email already exists"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onConflict: err => $"Conflict: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Conflict: Email already exists");
        result.Should().BeFailureOfType<ConflictError>();
    }

    [Fact]
    public void MatchError_WithBadRequestError_CallsOnBadRequest()
    {
        // Arrange
        var result = Result.Fail<int>(Error.BadRequest("Invalid request"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onBadRequest: err => $"BadRequest: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("BadRequest: Invalid request");
    }

    [Fact]
    public void MatchError_WithUnauthorizedError_CallsOnUnauthorized()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unauthorized("Not authenticated"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onUnauthorized: err => $"Unauthorized: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Unauthorized: Not authenticated");
    }

    [Fact]
    public void MatchError_WithForbiddenError_CallsOnForbidden()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Forbidden("Access denied"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onForbidden: err => $"Forbidden: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Forbidden: Access denied");
    }

    [Fact]
    public void MatchError_WithDomainError_CallsOnDomain()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Domain("Business rule violated"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onDomain: err => $"Domain: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Domain: Business rule violated");
    }

    [Fact]
    public void MatchError_WithRateLimitError_CallsOnRateLimit()
    {
        // Arrange
        var result = Result.Fail<int>(Error.RateLimit("Too many requests"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onRateLimit: err => $"RateLimit: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("RateLimit: Too many requests");
    }

    [Fact]
    public void MatchError_WithServiceUnavailableError_CallsOnServiceUnavailable()
    {
        // Arrange
        var result = Result.Fail<int>(Error.ServiceUnavailable("Service down"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onServiceUnavailable: err => $"ServiceUnavailable: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("ServiceUnavailable: Service down");
    }

    [Fact]
    public void MatchError_WithUnexpectedError_CallsOnUnexpected()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unexpected("Something went wrong"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onUnexpected: err => $"Unexpected: {err.Detail}",
            onError: err => $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Unexpected: Something went wrong");
    }

    [Fact]
    public void MatchError_WithNoSpecificHandler_CallsOnError()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        var output = result.MatchError(
            onSuccess: value => $"Success: {value}",
            onError: err => $"Default: {err.Detail}"
        );

        // Assert
        output.Should().Be("Default: Not found");
    }

    [Fact]
    public void MatchError_WithNoHandlerForErrorType_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        var act = () => result.MatchError(
            onSuccess: value => $"Success: {value}"
            // No error handlers provided
        );

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No handler provided for error type NotFoundError*");
    }

    [Fact]
    public void SwitchError_WithSuccessResult_CallsOnSuccess()
    {
        // Arrange
        var result = Result.Ok(42);
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Success: 42");
    }

    [Fact]
    public void SwitchError_WithValidationError_CallsOnValidation()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Validation("Invalid email", "email"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onValidation: err => output = $"Validation: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Validation: Invalid email");
    }

    #region SwitchError Tests for All Error Types

    [Fact]
    public void SwitchError_WithNotFoundError_CallsOnNotFound()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("User not found"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onNotFound: err => output = $"NotFound: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("NotFound: User not found");
    }

    [Fact]
    public void SwitchError_WithConflictError_CallsOnConflict()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Conflict("Already exists"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onConflict: err => output = $"Conflict: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Conflict: Already exists");
    }

    [Fact]
    public void SwitchError_WithBadRequestError_CallsOnBadRequest()
    {
        // Arrange
        var result = Result.Fail<int>(Error.BadRequest("Malformed request"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onBadRequest: err => output = $"BadRequest: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("BadRequest: Malformed request");
    }

    [Fact]
    public void SwitchError_WithUnauthorizedError_CallsOnUnauthorized()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unauthorized("Not authenticated"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onUnauthorized: err => output = $"Unauthorized: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Unauthorized: Not authenticated");
    }

    [Fact]
    public void SwitchError_WithForbiddenError_CallsOnForbidden()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Forbidden("Access denied"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onForbidden: err => output = $"Forbidden: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Forbidden: Access denied");
    }

    [Fact]
    public void SwitchError_WithDomainError_CallsOnDomain()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Domain("Business rule violated"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onDomain: err => output = $"Domain: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Domain: Business rule violated");
    }

    [Fact]
    public void SwitchError_WithRateLimitError_CallsOnRateLimit()
    {
        // Arrange
        var result = Result.Fail<int>(Error.RateLimit("Too many requests"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onRateLimit: err => output = $"RateLimit: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("RateLimit: Too many requests");
    }

    [Fact]
    public void SwitchError_WithServiceUnavailableError_CallsOnServiceUnavailable()
    {
        // Arrange
        var result = Result.Fail<int>(Error.ServiceUnavailable("Service down"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onServiceUnavailable: err => output = $"ServiceUnavailable: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("ServiceUnavailable: Service down");
    }

    [Fact]
    public void SwitchError_WithUnexpectedError_CallsOnUnexpected()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unexpected("Something went wrong"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onUnexpected: err => output = $"Unexpected: {err.Detail}",
            onError: err => output = $"Error: {err.Detail}"
        );

        // Assert
        output.Should().Be("Unexpected: Something went wrong");
    }

    [Fact]
    public void SwitchError_WithNoSpecificHandler_CallsOnError()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));
        var output = "";

        // Act
        result.SwitchError(
            onSuccess: value => output = $"Success: {value}",
            onError: err => output = $"Default: {err.Detail}"
        );

        // Assert
        output.Should().Be("Default: Not found");
    }

    [Fact]
    public void SwitchError_WithNoHandlerForErrorType_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        var act = () => result.SwitchError(
            onSuccess: value => { }
            // No error handlers provided
        );

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No handler provided for error type NotFoundError*");
    }

    #endregion

    #region MatchErrorAsync with All Error Type Handlers

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_NotFoundError_CallsOnNotFound()
    {
        // Arrange
        var result = Result.Fail<int>(Error.NotFound("Not found"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onNotFound: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"NotFound: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("NotFound: Not found");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_ConflictError_CallsOnConflict()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Conflict("Already exists"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onConflict: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Conflict: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Conflict: Already exists");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_BadRequestError_CallsOnBadRequest()
    {
        // Arrange
        var result = Result.Fail<int>(Error.BadRequest("Bad request"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onBadRequest: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"BadRequest: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("BadRequest: Bad request");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_UnauthorizedError_CallsOnUnauthorized()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unauthorized("Not authenticated"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onUnauthorized: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Unauthorized: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Unauthorized: Not authenticated");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_ForbiddenError_CallsOnForbidden()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Forbidden("Access denied"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onForbidden: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Forbidden: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Forbidden: Access denied");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_DomainError_CallsOnDomain()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Domain("Business rule violated"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onDomain: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Domain: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Domain: Business rule violated");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_RateLimitError_CallsOnRateLimit()
    {
        // Arrange
        var result = Result.Fail<int>(Error.RateLimit("Too many requests"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onRateLimit: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"RateLimit: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("RateLimit: Too many requests");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_ServiceUnavailableError_CallsOnServiceUnavailable()
    {
        // Arrange
        var result = Result.Fail<int>(Error.ServiceUnavailable("Service down"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onServiceUnavailable: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"ServiceUnavailable: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("ServiceUnavailable: Service down");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_UnexpectedError_CallsOnUnexpected()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Unexpected("Something went wrong"));

        // Act
        var output = await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            },
            onUnexpected: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Unexpected: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Unexpected: Something went wrong");
    }

    [Fact]
    public async Task MatchErrorAsync_WithAsyncHandlers_NoHandler_Throws()
    {
        // Arrange
        var result = Result.Fail<int>(Error.Conflict("conflict"));

        // Act
        var act = async () => await result.MatchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                return $"Success: {value}";
            }
        );

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No handler provided for error type ConflictError*");
    }

    #endregion

    #region SwitchErrorAsync with All Error Type Handlers

    [Fact]
    public async Task SwitchErrorAsync_WithValidationError_CallsOnValidation()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.Validation("Invalid", "field")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onValidation: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Validation: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Validation: Invalid");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithConflictError_CallsOnConflict()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.Conflict("Already exists")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onConflict: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Conflict: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Conflict: Already exists");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithBadRequestError_CallsOnBadRequest()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.BadRequest("Bad request")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onBadRequest: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"BadRequest: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("BadRequest: Bad request");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithUnauthorizedError_CallsOnUnauthorized()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.Unauthorized("Not authenticated")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onUnauthorized: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Unauthorized: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Unauthorized: Not authenticated");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithForbiddenError_CallsOnForbidden()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.Forbidden("Access denied")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onForbidden: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Forbidden: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Forbidden: Access denied");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithDomainError_CallsOnDomain()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.Domain("Business rule violated")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onDomain: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Domain: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Domain: Business rule violated");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithRateLimitError_CallsOnRateLimit()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.RateLimit("Too many requests")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onRateLimit: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"RateLimit: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("RateLimit: Too many requests");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithServiceUnavailableError_CallsOnServiceUnavailable()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.ServiceUnavailable("Service down")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onServiceUnavailable: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"ServiceUnavailable: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("ServiceUnavailable: Service down");
    }

    [Fact]
    public async Task SwitchErrorAsync_WithUnexpectedError_CallsOnUnexpected()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(Error.Unexpected("Something went wrong")));
        var output = "";

        // Act
        await resultTask.SwitchErrorAsync(
            onSuccess: async (value, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Success: {value}";
            },
            onUnexpected: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Unexpected: {err.Detail}";
            },
            onError: async (err, ct) =>
            {
                await Task.Delay(1, ct);
                output = $"Error: {err.Detail}";
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        // Assert
        output.Should().Be("Unexpected: Something went wrong");
    }

    #endregion

    // ...existing async tests...
}