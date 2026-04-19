namespace Trellis.Asp.Tests;

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Xunit;

[Collection("TrellisAspOptionsState")]
public class HttpResultTaskTests : IDisposable
{
    public HttpResultTaskTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_WithSuccess_ReturnsOk()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok("Test"));

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        var okResult = response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be("Test");
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_WithFailure_ReturnsProblem()
    {
        // Arrange
        var error = Error.BadRequest("Test error");
        var resultTask = Task.FromResult(Result.Fail<string>(error));
        var expected = new ProblemDetails
        {
            Title = "Bad Request",
            Detail = "Test error",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Status = StatusCodes.Status400BadRequest
        };

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_WithUnitSuccess_ReturnsNoContent()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok());

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        var noContentResult = response.As<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_WithNotFoundError_ReturnsNotFound()
    {
        // Arrange
        var error = Error.NotFound("Resource not found");
        var resultTask = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_WithConflictError_ReturnsConflict()
    {
        // Arrange
        var error = Error.Conflict("Resource conflict");
        var resultTask = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    #region Custom Options

    [Fact]
    public async Task ToHttpResultAsync_Task_with_custom_options_uses_overridden_mapping()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var resultTask = Task.FromResult(Result.Fail<string>(Error.Domain("Business rule")));

        // Act
        var response = await resultTask.ToHttpResultAsync(options);

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_without_options_uses_defaults()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<string>(Error.Domain("Business rule")));

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    #endregion
}