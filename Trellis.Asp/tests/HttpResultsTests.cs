namespace Trellis.Asp.Tests;

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Xunit;

[Collection("TrellisAspOptionsState")]
public class HttpResultsTests : IDisposable
{
    public HttpResultsTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Will_return_Ok_Result()
    {
        // Arrange
        var result = Result.Ok("Test");

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        var okResult = response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be("Test");
    }

    [Fact]
    public void Will_return_BadRequest_Result()
    {
        // Arrange
        var error = Error.BadRequest("Test");
        var result = Result.Fail<string>(error);
        var expected = new ProblemDetails
        {
            Title = "Bad Request",
            Detail = "Test",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Status = StatusCodes.Status400BadRequest
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_return_BadRequst_for_validation_failure()
    {
        // Arrange
        ValidationError.FieldError field1 = new("MyField1", ["Detail 1"]);
        ValidationError.FieldError field2 = new("MyField2", ["Detail 2", "More Detail 2"]);
        Error errors = Error.Validation([field1, field2], "Some validation failed.", "magicInstance");
        var result = Result.Fail(errors);
        var expected = new HttpValidationProblemDetails
        {
            Title = "One or more validation errors occurred.",
            Detail = "Some validation failed.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Status = StatusCodes.Status400BadRequest,
            Instance = "magicInstance",
            Errors = new Dictionary<string, string[]>
            {
                ["MyField1"] = ["Detail 1"],
                ["MyField2"] = ["Detail 2", "More Detail 2"]
            }
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>();
        HttpValidationProblemDetails actualProblemDetails = problemResult.ProblemDetails.As<HttpValidationProblemDetails>();
        actualProblemDetails.Should().BeEquivalentTo(expected);

    }

    [Fact]
    public void Will_retun_NotFound()
    {
        // Arrange
        var result = Result.Fail(Error.NotFound("User not found", "Chris"));
        var expected = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "User not found",
            Instance = "Chris",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Status = StatusCodes.Status404NotFound
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_retun_Conflict()
    {
        // Arrange
        var result = Result.Fail(Error.Conflict("Record has changed.", "Jon"));
        var expected = new ProblemDetails
        {
            Title = "Conflict",
            Detail = "Record has changed.",
            Instance = "Jon",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Status = StatusCodes.Status409Conflict
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_retun_Unauthorized()
    {
        // Arrange
        var result = Result.Fail(Error.Unauthorized("You do not have access.", "Donald"));
        var expected = new ProblemDetails
        {
            Title = "Unauthorized",
            Detail = "You do not have access.",
            Instance = "Donald",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            Status = StatusCodes.Status401Unauthorized
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_return_Forbidden()
    {
        // Arrange
        var result = Result.Fail(Error.Forbidden("Access is forbidden.", "Alice"));
        var expected = new ProblemDetails
        {
            Title = "Forbidden",
            Detail = "Access is forbidden.",
            Instance = "Alice",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            Status = StatusCodes.Status403Forbidden
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_return_InternalServerError()
    {
        // Arrange
        var result = Result.Fail(Error.Unexpected("An unexpected error occurred.", "Server"));
        var expected = new ProblemDetails
        {
            Title = "An error occurred while processing your request.",
            Detail = "An internal error occurred.",
            Instance = "Server",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Status = StatusCodes.Status500InternalServerError
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_return_NoContent_for_Unit_success()
    {
        // Arrange
        var result = Result.Ok();

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        var noContentResult = response.As<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void Will_return_NotFound_for_Unit_failure()
    {
        // Arrange
        var result = Result.Fail<Unit>(Error.NotFound("Resource not found", "UnitResource"));
        var expected = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "Resource not found",
            Instance = "UnitResource",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Status = StatusCodes.Status404NotFound
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Will_return_UnprocessableEntity_for_Domain_error()
    {
        // Arrange
        var result = Result.Fail(Error.Domain("Cannot withdraw more than account balance", "account-123"));

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Title.Should().Be("Unprocessable Entity");
        problemResult.ProblemDetails.Detail.Should().Be("Cannot withdraw more than account balance");
        problemResult.ProblemDetails.Instance.Should().Be("account-123");
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void Will_return_TooManyRequests_for_RateLimit_error()
    {
        // Arrange
        var result = Result.Fail(Error.RateLimit("API rate limit exceeded. Please try again in 60 seconds", "user-456"));

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Detail.Should().Be("API rate limit exceeded. Please try again in 60 seconds");
        problemResult.ProblemDetails.Instance.Should().Be("user-456");
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void Will_return_ServiceUnavailable_for_ServiceUnavailable_error()
    {
        // Arrange
        var result = Result.Fail(Error.ServiceUnavailable("Service is under maintenance. Please try again later", "payment-service"));

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Detail.Should().Be("An internal error occurred.");
        problemResult.ProblemDetails.Instance.Should().Be("payment-service");
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    #region Custom Options

    [Fact]
    public void ToHttpResult_with_custom_options_uses_overridden_mapping()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var result = Result.Fail<string>(Error.Domain("Business rule"));

        // Act
        var response = result.ToHttpResult(options);

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToHttpResult_without_options_uses_defaults()
    {
        // Arrange
        var result = Result.Fail<string>(Error.Domain("Business rule"));

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void ToHttpResult_Error_with_custom_options_uses_overridden_mapping()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<ConflictError>(StatusCodes.Status422UnprocessableEntity);
        var error = Error.Conflict("Already exists");

        // Act
        var response = error.ToHttpResult(options);

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void ToHttpResult_custom_options_do_not_affect_unmapped_errors()
    {
        // Arrange — override DomainError only
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var result = Result.Fail<string>(Error.NotFound("Missing"));

        // Act
        var response = result.ToHttpResult(options);

        // Assert — NotFound still uses default 404
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    #endregion

    #region AddTrellisAsp integration

    [Fact]
    public void ToHttpResult_without_explicit_options_uses_AddTrellisAsp_configuration()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<DomainError>(StatusCodes.Status400BadRequest));

        var result = Result.Fail<string>(Error.Domain("Business rule"));

        var response = result.ToHttpResult();

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest,
                "AddTrellisAsp configured DomainError → 400, but ToHttpResult ignores it without explicit options");
    }

    [Fact]
    public async Task ToHttpResult_without_explicit_options_preserves_AddTrellisAsp_configuration_across_execution_context_boundaries()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<DomainError>(StatusCodes.Status400BadRequest));

        var result = Result.Fail<string>(Error.Domain("Business rule"));

        Task<Microsoft.AspNetCore.Http.IResult> responseTask;
        using (ExecutionContext.SuppressFlow())
            responseTask = Task.Run(() => result.ToHttpResult());

        var response = await responseTask;

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest,
                "AddTrellisAsp configuration should apply even when ToHttpResult runs in a different execution context");
    }

    #endregion

    #region 5xx detail redaction

    [Fact]
    public void ToHttpResult_5xx_error_should_not_leak_internal_detail()
    {
        var result = Result.Fail<string>(
            Error.Unexpected("NullReferenceException at MyService.GetUser line 45"));

        var response = result.ToHttpResult();

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Detail.Should().NotContain("NullReferenceException",
            "5xx responses must not leak internal error details to clients");
    }

    #endregion
}