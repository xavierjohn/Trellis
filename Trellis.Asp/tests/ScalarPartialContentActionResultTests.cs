namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trellis;

/// <summary>
/// Tests for scalar partial content overloads:
/// <see cref="ActionResultExtensions.ToActionResult{TValue}(Result{TValue}, ControllerBase, long, long, long)"/>
/// and its async variants.
/// </summary>
public class ScalarPartialContentActionResultTests
{
    private static ControllerBase CreateController()
    {
        var mock = new Mock<ControllerBase> { CallBase = true };
        mock.Object.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return mock.Object;
    }

    #region Sync

    [Fact]
    public void ToActionResult_PartialRange_Returns206()
    {
        var controller = CreateController();
        var data = new[] { "a", "b", "c" };
        var result = Result.Ok(data);

        var response = result.ToActionResult(controller, 0, 2, 10);

        response.Result.Should().BeOfType<PartialContentResult>();
        var partial = response.Result.As<PartialContentResult>();
        partial.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        partial.ContentRangeHeaderValue.ToString().Should().Be("items 0-2/10");
    }

    [Fact]
    public void ToActionResult_CompleteRange_Returns200()
    {
        var controller = CreateController();
        var data = new[] { "a", "b", "c" };
        var result = Result.Ok(data);

        var response = result.ToActionResult(controller, 0, 2, 3);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void ToActionResult_SingleItemOfOne_Returns200()
    {
        var controller = CreateController();
        var result = Result.Ok("only");

        var response = result.ToActionResult(controller, 0, 0, 1);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void ToActionResult_Failure_ReturnsError()
    {
        var controller = CreateController();
        var result = Result.Fail<string[]>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var response = result.ToActionResult(controller, 0, 2, 10);

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToActionResult_NegativeFrom_Returns200()
    {
        var controller = CreateController();
        var result = Result.Ok("data");

        var response = result.ToActionResult(controller, -1, 0, 10);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void ToActionResult_ToLessThanFrom_Returns200()
    {
        var controller = CreateController();
        var result = Result.Ok("data");

        var response = result.ToActionResult(controller, 5, 4, 10);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void ToActionResult_ZeroTotalLength_Returns200()
    {
        var controller = CreateController();
        var result = Result.Ok("data");

        var response = result.ToActionResult(controller, 0, 0, 0);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void ToActionResult_FromBeyondTotal_Returns200()
    {
        var controller = CreateController();
        var result = Result.Ok("data");

        var response = result.ToActionResult(controller, 10, 15, 5);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void ToActionResult_ToClamped_ToTotalLengthMinusOne()
    {
        var controller = CreateController();
        var result = Result.Ok("data");

        var response = result.ToActionResult(controller, 0, 100, 5);

        response.Result.Should().BeOfType<OkObjectResult>();
        // from=0, clampedTo=4, totalLength=5 → complete range → 200
    }

    [Fact]
    public void ToActionResult_PartialRange_ClampsTo()
    {
        var controller = CreateController();
        var result = Result.Ok("data");

        var response = result.ToActionResult(controller, 2, 100, 5);

        response.Result.Should().BeOfType<PartialContentResult>();
        var partial = response.Result.As<PartialContentResult>();
        partial.ContentRangeHeaderValue.ToString().Should().Be("items 2-4/5");
    }

    #endregion

    #region Async Task

    private static readonly string[] TaskItems = ["a", "b"];

    [Fact]
    public async Task ToActionResultAsync_Task_PartialRange_Returns206()
    {
        var controller = CreateController();
        var resultTask = Task.FromResult(Result.Ok(TaskItems));

        var response = await resultTask.ToActionResultAsync(controller, 0, 1, 10);

        response.Result.Should().BeOfType<PartialContentResult>();
    }

    [Fact]
    public async Task ToActionResultAsync_Task_Failure_ReturnsError()
    {
        var controller = CreateController();
        var resultTask = Task.FromResult(Result.Fail<string>(new Error.BadRequest("bad.request") { Detail = "bad" }));

        var response = await resultTask.ToActionResultAsync(controller, 0, 0, 1);

        response.Result.Should().BeOfType<ObjectResult>();
    }

    #endregion

    #region Async ValueTask

    private static readonly string[] ValueTaskItems = ["a"];

    [Fact]
    public async Task ToActionResultAsync_ValueTask_PartialRange_Returns206()
    {
        var controller = CreateController();
        var resultTask = new ValueTask<Result<string[]>>(Result.Ok(ValueTaskItems));

        var response = await resultTask.ToActionResultAsync(controller, 0, 0, 5);

        response.Result.Should().BeOfType<PartialContentResult>();
    }

    [Fact]
    public async Task ToActionResultAsync_ValueTask_CompleteRange_Returns200()
    {
        var controller = CreateController();
        var resultTask = new ValueTask<Result<string>>(Result.Ok("data"));

        var response = await resultTask.ToActionResultAsync(controller, 0, 0, 1);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    #endregion
}