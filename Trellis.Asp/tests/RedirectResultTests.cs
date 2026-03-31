namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

[Collection("TrellisAspOptionsState")]
public class RedirectResultTests : IDisposable
{
    public RedirectResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ToSeeOther_success_returns_303_with_Location_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("resource-42");

        // Act
        var response = result.ToSeeOther(controller, v => $"/resources/{v}");

        // Assert
        response.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status303SeeOther);
        controller.Response.Headers.Location.ToString().Should().Be("/resources/resource-42");
    }

    [Fact]
    public void ToSeeOther_failure_returns_error_result()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.NotFound("Not found"));

        // Act
        var response = result.ToSeeOther(controller, v => $"/resources/{v}");

        // Assert
        response.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    private static ControllerBase CreateControllerWithHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        var controllerMock = new Mock<ControllerBase> { CallBase = true };
        controllerMock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controllerMock.Object;
    }
}
