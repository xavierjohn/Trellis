namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

[Collection("TrellisAspOptionsState")]
public class CompanionHeaderTests : IDisposable
{
    public CompanionHeaderTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MethodNotAllowedError_emits_Allow_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = Error.MethodNotAllowed("DELETE is not supported.", ["GET", "PUT"]);
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status405MethodNotAllowed);
        controller.Response.Headers["Allow"].ToString().Should().Be("GET, PUT");
    }

    [Fact]
    public void RateLimitError_with_RetryAfter_emits_RetryAfter_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = Error.RateLimit("Too many requests", RetryAfterValue.FromSeconds(60));
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        controller.Response.Headers["Retry-After"].ToString().Should().Be("60");
    }

    [Fact]
    public void RateLimitError_without_RetryAfter_does_not_emit_RetryAfter_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = Error.RateLimit("Too many requests");
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public void ServiceUnavailableError_with_RetryAfter_emits_RetryAfter_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = Error.ServiceUnavailable("Service is down", RetryAfterValue.FromSeconds(120));
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        controller.Response.Headers["Retry-After"].ToString().Should().Be("120");
    }

    [Fact]
    public void ContentTooLargeError_with_RetryAfter_emits_RetryAfter_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = Error.ContentTooLarge("Request body too large", RetryAfterValue.FromSeconds(30));
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status413RequestEntityTooLarge);
        controller.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public void RangeNotSatisfiableError_emits_ContentRange_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = Error.RangeNotSatisfiable("Requested range not satisfiable", 1024);
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status416RangeNotSatisfiable);
        controller.Response.Headers["Content-Range"].ToString().Should().Be("bytes */1024");
    }

    private static ControllerBase CreateControllerWithHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        var controllerMock = new Mock<ControllerBase> { CallBase = true };
        controllerMock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controllerMock.Object;
    }
}