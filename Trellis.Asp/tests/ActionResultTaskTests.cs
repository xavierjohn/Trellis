namespace Trellis.Asp.Tests;

using System.Collections.Immutable;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using static Trellis.Error.UnprocessableContent;

public class ActionResultTaskTests
{
    [Fact]
    public async Task Will_return_Ok_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Task.FromResult(Result.Ok("Test"));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<OkObjectResult>();
        var okObjResult = response.Result.As<OkObjectResult>();
        okObjResult.Value.Should().Be("Test");
        okObjResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Will_return_BadRequest_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.BadRequest("bad.request") { Detail = "Test" };
        var result = Task.FromResult(Result.Fail<string>(error));
        var expected = new ProblemDetails
        {
            Detail = "Test",
            Status = StatusCodes.Status400BadRequest,
            Instance = (string?)null
        };

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Will_return_Forbidden_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.Forbidden("xavier") { Detail = "You are forbidden." };
        var expected = new ProblemDetails
        {
            Detail = "You are forbidden.",
            Status = StatusCodes.Status403Forbidden,
            Instance = (string?)null
        };
        var result = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Will_return_Unauthorized_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.Unauthorized() { Detail = "You are not authorized." };
        var expected = new ProblemDetails
        {
            Detail = "You are not authorized.",
            Status = StatusCodes.Status401Unauthorized,
            Instance = (string?)null
        };
        var result = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Will_return_BadRequest_Validation1_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("firstName"), "validation.error") { Detail = "First name required." }))
            { Detail = "Customer validation failed." };
        var expected = new ValidationProblemDetails
        {
            Title = null,
            Detail = "Customer validation failed.",
            Instance = (string?)null,
            Status = StatusCodes.Status422UnprocessableEntity,
            Errors = new Dictionary<string, string[]> { { "firstName", ["First name required."] } }
        };
        var result = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeOfType<ValidationProblemDetails>();
        var validationProblem = objectResult.Value.As<ValidationProblemDetails>();
        validationProblem.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public async Task Will_return_Conflict_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.Conflict(null, "Micheal") { Detail = "There is a conflict." };
        var expected = new ProblemDetails
        {
            Detail = "There is a conflict.",
            Status = StatusCodes.Status409Conflict,
            Instance = (string?)null
        };
        var result = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Will_return_InternalServier_Result_Async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "What happened?" };
        var expected = new ProblemDetails
        {
            Detail = "An internal error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Instance = (string?)null
        };
        var result = Task.FromResult(Result.Fail<string>(error));

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ToPartialOrOkActionResultAsync_will_return_partial_status_code_when_results_are_partial_callback()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Task.FromResult(Result.Ok("Test"));

        // Act
        var response = await result.ToActionResultAsync(controller,
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
        var result = Task.FromResult(Result.Ok("Test"));

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

    [Fact]
    public async Task Will_return_NoContent_for_Unit_success_async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Task.FromResult(Result.Ok());

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Should().BeOfType<NoContentResult>();
        response.As<NoContentResult>().StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task Will_return_NotFound_for_Unit_failure_async()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" };
        var result = Task.FromResult(Result.Fail(error));
        var expected = new ProblemDetails
        {
            Detail = "Resource not found",
            Status = StatusCodes.Status404NotFound
        };

        // Act
        var response = await result.ToActionResultAsync(controller);

        // Assert
        response.Should().BeOfType<ObjectResult>();
        var objectResult = response.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}