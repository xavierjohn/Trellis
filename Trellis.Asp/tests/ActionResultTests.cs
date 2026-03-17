namespace Trellis.Asp.Tests;

using System;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

[Collection("TrellisAspOptionsState")]
public class ActionResultTests : IDisposable
{
    public ActionResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Will_return_Ok_Result()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Success("Test");

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void Will_return_BadRequest_Result()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.BadRequest("Test");
        var result = Result.Failure<string>(error);
        var expected = new ProblemDetails
        {
            Detail = "Test",
            Status = StatusCodes.Status400BadRequest
        };

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected);
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToPartialOrOkActionResult_will_return_partial_status_code_when_results_are_partial_callback()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Success("Test");

        // Act
        var response = result.ToActionResult(controller,
            static r => new ContentRangeHeaderValue(4, 10, 15) { Unit = "stones" },
            static r => "Replaced");

        // Assert
        var partialResult = response.Result.As<PartialObjectResult>();
        partialResult.Value.Should().Be("Replaced");
        partialResult.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        partialResult.ContentRangeHeaderValue.From.Should().Be(4);
        partialResult.ContentRangeHeaderValue.To.Should().Be(10);
        partialResult.ContentRangeHeaderValue.Length.Should().Be(15);
        partialResult.ContentRangeHeaderValue.Unit.Should().Be("stones");
    }

    [Fact]
    public void ToPartialOrOkActionResult_will_not_call_callback_functions_if_result_in_failed_state()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Failure<string>(Error.NotFound("Can't find it"));

        // Act
        var response = result.ToActionResult<string, string>(controller,
            static r => throw new InvalidOperationException(),
            static r => throw new InvalidOperationException());

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToPartialOrOkActionResult_will_return_Okay_status_code_when_results_are_complete()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Success("Test");

        // Act
        var response = result.ToActionResult(controller, 0, 9, 10);

        // Assert
        var okObjResult = response.Result.As<OkObjectResult>();
        okObjResult.Value.Should().Be("Test");
        okObjResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void Will_return_NoContent_for_Unit_success()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Success();

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<NoContentResult>();
        response.Result.As<NoContentResult>().StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void Will_return_BadRequest_for_Unit_failure()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.BadRequest("Operation failed");
        var result = Result.Failure<Unit>(error);
        var expected = new ProblemDetails
        {
            Detail = "Operation failed",
            Status = StatusCodes.Status400BadRequest
        };

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.Value.Should().BeEquivalentTo(expected);
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    #region Error Type Tests

    [Fact]
    public void Will_return_NotFound_for_NotFoundError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.NotFound("Resource not found");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void Will_return_Unauthorized_for_UnauthorizedError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.Unauthorized("Not authenticated");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void Will_return_Forbidden_for_ForbiddenError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.Forbidden("Access denied");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void Will_return_Conflict_for_ConflictError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.Conflict("Resource already exists");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void Will_return_UnprocessableEntity_for_DomainError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.Domain("Business rule violation");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void Will_return_TooManyRequests_for_RateLimitError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.RateLimit("Too many requests");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void Will_return_InternalServerError_for_UnexpectedError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.Unexpected("Something went wrong");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void Will_return_ServiceUnavailable_for_ServiceUnavailableError()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.ServiceUnavailable("Service is down");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    #endregion

    #region Partial Content Tests

    [Fact]
    public void ToActionResult_with_range_returns_PartialContent_when_subset()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Success("Test");

        // Act
        var response = result.ToActionResult(controller, 0, 4, 10);

        // Assert
        var partialResult = response.Result.As<PartialObjectResult>();
        partialResult.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        partialResult.Value.Should().Be("Test");
    }

    [Fact]
    public void ToActionResult_with_range_returns_failure_for_error()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = Error.NotFound("Not found");
        var result = Result.Failure<string>(error);

        // Act
        var response = result.ToActionResult(controller, 0, 4, 10);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToActionResult_with_funcRange_returns_Ok_when_complete()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Success("Test");

        // Act
        var response = result.ToActionResult(controller,
            static r => new ContentRangeHeaderValue(0, 9, 10),
            static r => "Transformed");

        // Assert
        var okResult = response.Result.As<OkObjectResult>();
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be("Transformed");
    }

    #endregion

    #region Custom Options via DI

    [Fact]
    public void ToActionResult_uses_custom_options_from_DI()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var controller = CreateControllerWithOptions(options);
        var result = Result.Failure<string>(Error.Domain("Business rule"));

        // Act
        var response = result.ToActionResult(controller);

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToActionResult_falls_back_to_defaults_when_no_DI()
    {
        // Arrange — mock controller with no HttpContext (null-safe fallback)
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Failure<string>(Error.Domain("Business rule"));

        // Act
        var response = result.ToActionResult(controller);

        // Assert — DomainError defaults to 422
        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void ToActionResult_custom_options_do_not_affect_unmapped_errors()
    {
        // Arrange — override DomainError only
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var controller = CreateControllerWithOptions(options);
        var result = Result.Failure<string>(Error.NotFound("Missing"));

        // Act
        var response = result.ToActionResult(controller);

        // Assert — NotFound still uses default 404
        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    private static ControllerBase CreateControllerWithOptions(TrellisAspOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory,
            Microsoft.AspNetCore.Mvc.Infrastructure.DefaultProblemDetailsFactory>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ApiBehaviorOptions()));
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var controllerMock = new Mock<ControllerBase> { CallBase = true };
        controllerMock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controllerMock.Object;
    }

    #endregion
}