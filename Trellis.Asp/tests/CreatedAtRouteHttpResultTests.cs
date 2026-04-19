namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Trellis;
using Xunit;

/// <summary>
/// Tests for <see cref="HttpResultExtensions.ToCreatedAtRouteHttpResult{TValue}"/> and
/// <see cref="HttpResultExtensions.ToCreatedAtRouteHttpResult{TValue, TOut}"/>.
/// </summary>
public class CreatedAtRouteHttpResultTests
{
    private record UserDto(string Id, string Name);

    #region ToCreatedAtRouteHttpResult (identity — no map)

    [Fact]
    public void ToCreatedAtRouteHttpResult_Success_Returns201()
    {
        // Arrange
        var dto = new UserDto("42", "Alice");
        var result = Result.Ok(dto);

        // Act
        var response = result.ToCreatedAtRouteHttpResult(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Id }));

        // Assert
        response.Should().BeOfType<CreatedAtRoute<UserDto>>();
        var created = response.As<CreatedAtRoute<UserDto>>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Value.Should().Be(dto);
        created.RouteName.Should().Be("GetUser");
    }

    [Fact]
    public void ToCreatedAtRouteHttpResult_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var error = Error.BadRequest("Invalid input");
        var result = Result.Fail<UserDto>(error);

        // Act
        var response = result.ToCreatedAtRouteHttpResult(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Id }));

        // Assert
        response.Should().BeOfType<ProblemHttpResult>();
        var problemResult = response.As<ProblemHttpResult>();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToCreatedAtRouteHttpResult_Failure_DoesNotInvokeRouteValues()
    {
        // Arrange
        var result = Result.Fail<UserDto>(Error.NotFound("Missing"));

        // Act
        var response = result.ToCreatedAtRouteHttpResult(
            routeName: "GetUser",
            routeValues: _ => throw new InvalidOperationException("Should not be called"));

        // Assert
        response.Should().BeOfType<ProblemHttpResult>();
    }

    #endregion

    #region ToCreatedAtRouteHttpResult with map

    [Fact]
    public void ToCreatedAtRouteHttpResult_WithMap_Success_Returns201WithMappedValue()
    {
        // Arrange
        var result = Result.Ok(("42", "Alice"));

        // Act
        var response = result.ToCreatedAtRouteHttpResult(
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
    public void ToCreatedAtRouteHttpResult_WithMap_Failure_ReturnsProblemDetails()
    {
        // Arrange
        var result = Result.Fail<(string, string)>(Error.Conflict("Already exists"));

        // Act
        var response = result.ToCreatedAtRouteHttpResult(
            routeName: "GetUser",
            routeValues: v => new RouteValueDictionary(new { id = v.Item1 }),
            map: v => new UserDto(v.Item1, v.Item2));

        // Assert
        response.Should().BeOfType<ProblemHttpResult>();
        var problemResult = response.As<ProblemHttpResult>();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void ToCreatedAtRouteHttpResult_WithMap_Failure_DoesNotInvokeCallbacks()
    {
        // Arrange
        var result = Result.Fail<string>(Error.NotFound("Missing"));

        // Act
        var response = result.ToCreatedAtRouteHttpResult<string, UserDto>(
            routeName: "GetUser",
            routeValues: _ => throw new InvalidOperationException("routeValues should not be called"),
            map: _ => throw new InvalidOperationException("map should not be called"));

        // Assert
        response.Should().BeOfType<ProblemHttpResult>();
    }

    #endregion
}