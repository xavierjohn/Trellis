namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>
/// Tests for Task-based async overloads of ToCreatedAtActionResultAsync in <see cref="ActionResultExtensionsAsync"/>.
/// </summary>
public class CreatedAtActionResultTaskTests
{
    private record UserDto(string Id, string Name);

    #region ToCreatedAtActionResultAsync (identity — no map)

    [Fact]
    public async Task ToCreatedAtActionResultAsync_Task_Success_Returns201()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var dto = new UserDto("42", "Alice");
        var resultTask = Task.FromResult(Result.Ok(dto));

        // Act
        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id });

        // Assert
        response.Result.Should().BeOfType<CreatedAtActionResult>();
        var created = response.Result.As<CreatedAtActionResult>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be("GetUser");
        created.Value.Should().Be(dto);
    }

    [Fact]
    public async Task ToCreatedAtActionResultAsync_Task_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var resultTask = Task.FromResult(Result.Fail<UserDto>(Error.BadRequest("Bad")));

        // Act
        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Id });

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    #endregion

    #region ToCreatedAtActionResultAsync with map

    [Fact]
    public async Task ToCreatedAtActionResultAsync_Task_WithMap_Success_Returns201WithMappedValue()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var resultTask = Task.FromResult(Result.Ok(("42", "Alice")));

        // Act
        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Item1 },
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        var created = response.Result.As<CreatedAtActionResult>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().BeEquivalentTo(new UserDto("42", "Alice"));
    }

    [Fact]
    public async Task ToCreatedAtActionResultAsync_Task_WithMap_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var controller = new Mock<ControllerBase> { CallBase = true }.Object;
        var resultTask = Task.FromResult(Result.Fail<(string, string)>(Error.Conflict("Exists")));

        // Act
        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetUser",
            routeValues: v => new { id = v.Item1 },
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion
}