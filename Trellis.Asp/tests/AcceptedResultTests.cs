namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

[Collection("TrellisAspOptionsState")]
public class AcceptedResultTests : IDisposable
{
    public AcceptedResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ToAcceptedActionResult_success_with_map_returns_202_with_body()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        // Act
        var response = result.ToAcceptedActionResult<string, string>(controller, map: v => v.ToUpperInvariant());

        // Assert
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(202);
        objectResult.Value.Should().Be("HELLO");
    }

    [Fact]
    public void ToAcceptedActionResult_success_with_monitorUri_sets_Location_header()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("job-123");

        // Act
        var response = result.ToAcceptedActionResult<string, string>(
            controller,
            monitorUri: v => $"/status/{v}",
            map: v => v);

        // Assert
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(202);
        controller.Response.Headers.Location.ToString().Should().Be("/status/job-123");
    }

    [Fact]
    public void ToAcceptedActionResult_failure_returns_error_status_code()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.BadRequest("Invalid request"));

        // Act
        var response = result.ToAcceptedActionResult<string, string>(controller, map: v => v);

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToAcceptedActionResult_success_without_map_returns_202_status_code_only()
    {
        // Arrange
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        // Act
        var response = result.ToAcceptedActionResult<string, string>(controller);

        // Assert
        response.Result.As<StatusCodeResult>().StatusCode.Should().Be(202);
    }

    private static ControllerBase CreateControllerWithHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        var controllerMock = new Mock<ControllerBase> { CallBase = true };
        controllerMock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controllerMock.Object;
    }
}
