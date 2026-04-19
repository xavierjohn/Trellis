namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Trellis;
using Xunit;

/// <summary>
/// Tests for Task-based async overloads of ToCreatedAtRouteHttpResultAsync in <see cref="HttpResultExtensionsAsync"/>.
/// </summary>
public class CreatedAtRouteHttpResultTaskTests
{
    private record UserDto(string Id, string Name);

    [Fact]
    public async Task ToCreatedAtRouteHttpResultAsync_Task_Success_Returns201()
    {
        // Arrange
        var dto = new UserDto("42", "Alice");
        var resultTask = Task.FromResult(Result.Ok(dto));

        // Act
        var response = await resultTask.ToCreatedAtRouteHttpResultAsync(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Id }));

        // Assert
        response.Should().BeOfType<CreatedAtRoute<UserDto>>();
        var created = response.As<CreatedAtRoute<UserDto>>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().Be(dto);
    }

    [Fact]
    public async Task ToCreatedAtRouteHttpResultAsync_Task_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<UserDto>(Error.BadRequest("Bad")));

        // Act
        var response = await resultTask.ToCreatedAtRouteHttpResultAsync(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Id }));

        // Assert
        response.Should().BeOfType<ProblemHttpResult>();
        response.As<ProblemHttpResult>().ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ToCreatedAtRouteHttpResultAsync_Task_WithMap_Success_Returns201()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(("42", "Alice")));

        // Act
        var response = await resultTask.ToCreatedAtRouteHttpResultAsync(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Item1 }),
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        response.Should().BeOfType<CreatedAtRoute<UserDto>>();
        var created = response.As<CreatedAtRoute<UserDto>>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().BeEquivalentTo(new UserDto("42", "Alice"));
    }

    [Fact]
    public async Task ToCreatedAtRouteHttpResultAsync_Task_WithMap_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<(string, string)>(Error.Conflict("Exists")));

        // Act
        var response = await resultTask.ToCreatedAtRouteHttpResultAsync(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Item1 }),
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        response.Should().BeOfType<ProblemHttpResult>();
        response.As<ProblemHttpResult>().ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }
}