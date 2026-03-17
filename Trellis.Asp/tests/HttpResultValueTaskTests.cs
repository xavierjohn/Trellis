namespace Trellis.Asp.Tests;

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Xunit;

[Collection("TrellisAspOptionsState")]
public class HttpResultValueTaskTests : IDisposable
{
    public HttpResultValueTaskTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Will_return_Ok_Result_Async()
    {
        // Arrange
        var result = ValueTask.FromResult(Result.Success("Test"));

        // Act
        var response = await result.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        var okResult = response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be("Test");
    }

    [Fact]
    public async Task Will_return_BadRequest_Result_Async()
    {
        // Arrange
        var error = Error.BadRequest("Test");
        var result = ValueTask.FromResult(Result.Failure<string>(error));
        var expected = new ProblemDetails
        {
            Title = "Bad Request",
            Detail = "Test",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Status = StatusCodes.Status400BadRequest
        };

        // Act
        var response = await result.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task Will_return_NoContent_for_Unit_success_async()
    {
        // Arrange
        var result = ValueTask.FromResult(Result.Success());

        // Act
        var response = await result.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        var noContentResult = response.As<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task Will_return_Conflict_for_Unit_failure_async()
    {
        // Arrange
        var error = Error.Conflict("Conflict occurred");
        var result = ValueTask.FromResult(Result.Failure<Unit>(error));
        var expected = new ProblemDetails
        {
            Title = "Conflict",
            Detail = "Conflict occurred",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Status = StatusCodes.Status409Conflict
        };

        // Act
        var response = await result.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected);
    }

    #region Custom Options

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_with_custom_options_uses_overridden_mapping()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var resultTask = ValueTask.FromResult(Result.Failure<string>(Error.Domain("Business rule")));

        // Act
        var response = await resultTask.ToHttpResultAsync(options);

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_without_options_uses_defaults()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Failure<string>(Error.Domain("Business rule")));

        // Act
        var response = await resultTask.ToHttpResultAsync();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    #endregion
}