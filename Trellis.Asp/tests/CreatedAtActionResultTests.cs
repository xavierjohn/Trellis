namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>
/// Tests for <see cref="ActionResultExtensions.ToCreatedAtActionResult{TValue}"/> and
/// <see cref="ActionResultExtensions.ToCreatedAtActionResult{TValue, TOut}"/>.
/// </summary>
public class CreatedAtActionResultTests
{
    private record UserDto(string Id, string Name);

    #region ToCreatedAtActionResult (identity — no map)

    [Fact]
    public void ToCreatedAtActionResult_Success_Returns201WithValue()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var dto = new UserDto("42", "Alice");
        var result = Result.Ok(dto);

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id });

        // Assert
        response.Result.Should().BeOfType<CreatedAtActionResult>();
        var created = response.Result.As<CreatedAtActionResult>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be("GetUser");
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be("42");
        created.Value.Should().Be(dto);
    }

    [Fact]
    public void ToCreatedAtActionResult_Success_WithControllerName_SetsControllerName()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var dto = new UserDto("7", "Bob");
        var result = Result.Ok(dto);

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id },
            controllerName: "Users");

        // Assert
        var created = response.Result.As<CreatedAtActionResult>();
        created.ControllerName.Should().Be("Users");
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var error = new Error.BadRequest("bad.request") { Detail = "Invalid input" };
        var result = Result.Fail<UserDto>(error);

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id });

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        var objectResult = response.Result.As<ObjectResult>();
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_DoesNotInvokeRouteValues()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Fail<UserDto>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: _ => throw new InvalidOperationException("Should not be called"));

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_NotFound_Returns404()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Fail<UserDto>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id });

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_Conflict_Returns409()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Fail<UserDto>(new Error.Conflict(null, "conflict") { Detail = "Already exists" });

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id });

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region ToCreatedAtActionResult with map function

    [Fact]
    public void ToCreatedAtActionResult_WithMap_Success_Returns201WithMappedValue()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Ok(("42", "Alice"));

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Item1 },
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        response.Result.Should().BeOfType<CreatedAtActionResult>();
        var created = response.Result.As<CreatedAtActionResult>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be("GetUser");
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be("42");
        created.Value.Should().BeEquivalentTo(new UserDto("42", "Alice"));
    }

    [Fact]
    public void ToCreatedAtActionResult_WithMap_Success_WithControllerName()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Ok(("7", "Bob"));

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Item1 },
            map: v => new UserDto(v.Item1, v.Item2),
            controllerName: "Users");

        // Assert
        var created = response.Result.As<CreatedAtActionResult>();
        created.ControllerName.Should().Be("Users");
    }

    [Fact]
    public void ToCreatedAtActionResult_WithMap_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Fail<(string, string)>(new Error.BadRequest("bad.request") { Detail = "Bad input" });

        // Act
        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Item1 },
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToCreatedAtActionResult_WithMap_Failure_DoesNotInvokeCallbacks()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var result = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Missing" });

        // Act
        var response = result.ToCreatedAtActionResult<string, UserDto>(controller,
            actionName: "GetUser",
            routeValues: _ => throw new InvalidOperationException("routeValues should not be called"),
            map: _ => throw new InvalidOperationException("map should not be called"));

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    #endregion
}