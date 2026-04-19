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
        var error = new Error.MethodNotAllowed(EquatableArray.Create("GET", "PUT")) { Detail = "DELETE is not supported." };
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
        var error = new Error.TooManyRequests(RetryAfterValue.FromSeconds(60)) { Detail = "Too many requests" };
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
        var error = new Error.TooManyRequests() { Detail = "Too many requests" };
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
        var error = new Error.ServiceUnavailable(RetryAfterValue.FromSeconds(120)) { Detail = "Service is down" };
        var result = Result.Fail<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        controller.Response.Headers["Retry-After"].ToString().Should().Be("120");
    }

    [Fact]
    public void ContentTooLarge_does_not_emit_RetryAfter_header_in_V6()
    {
        // V6 ADT note: Error.ContentTooLarge has no RetryAfter property.
        // If a Retry-After advisory becomes a requirement for 413, add a property to the
        // record and emit the header in ActionResultExtensions / HttpResultExtensions.
        var controller = CreateControllerWithHttpContext();
        var error = new Error.ContentTooLarge() { Detail = "Request body too large" };
        var result = Result.Fail<string>(error);

        var response = result.ToActionResult(controller);

        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status413RequestEntityTooLarge);
        controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public void RangeNotSatisfiableError_emits_ContentRange_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var error = new Error.RangeNotSatisfiable(1024, "bytes") { Detail = "Requested range not satisfiable" };
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