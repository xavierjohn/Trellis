namespace Trellis.Asp.Tests;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

public class ActionResultValueTaskTests
{
    [Fact]
    public async Task Will_return_Ok_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = ValueTask.FromResult(Result.Success("Test"));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        var okObjResult = response.Result.As<OkObjectResult>();
        okObjResult.Value.Should().Be("Test");
        okObjResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Will_return_BadRequest_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.BadRequest("Test", "Jackson");
        var result = ValueTask.FromResult(Result.Failure<string>(error));
        var expected = new ProblemDetails
        {
            Detail = "Test",
            Status = StatusCodes.Status400BadRequest,
            Instance = "Jackson"
        };

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected);
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ToPartialOrOkActionResultAsync_will_return_partial_status_code_when_results_are_partial_callback()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = ValueTask.FromResult(Result.Success("Test"));

        // Act
        var response = await result.ToActionResultAsync(
            controller,
            r => new ContentRangeHeaderValue(4, 10, 15) { Unit = "items" },
            r => r);

        // Assert
        var partialResult = response.Result.As<PartialContentResult>();
        partialResult.Value.Should().Be("Test");
        partialResult.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        partialResult.ContentRangeHeaderValue.From.Should().Be(4);
        partialResult.ContentRangeHeaderValue.To.Should().Be(10);
        partialResult.ContentRangeHeaderValue.Length.Should().Be(15);
        partialResult.ContentRangeHeaderValue.Unit.Should().Be("items");
    }

    [Fact]
    public async Task ToPartialOrOkActionResultAsync_will_return_Okay_status_code_when_results_are_complete_callback()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = ValueTask.FromResult(Result.Success("Test"));

        // Act
        var response = await result.ToActionResultAsync(
            controller,
            r => new ContentRangeHeaderValue(0, 9, 10) { Unit = "items" },
            r => r);

        // Assert
        var okObjResult = response.Result.As<OkObjectResult>();
        okObjResult.Value.Should().Be("Test");
        okObjResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}