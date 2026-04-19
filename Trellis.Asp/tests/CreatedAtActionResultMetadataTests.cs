namespace Trellis.Asp.Tests;

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trellis;

/// <summary>
/// Tests for <see cref="ActionResultExtensions.ToCreatedAtActionResult{TIn, TOut}(Result{TIn}, ControllerBase, string, Func{TIn, object?}, Func{TIn, RepresentationMetadata}, Func{TIn, TOut}, string?)"/>
/// — the Created+Metadata overload that emits ETag/Last-Modified on 201 responses.
/// </summary>
public class CreatedAtActionResultMetadataTests
{
    private record Order(string Id, int Version, string Name);
    private record OrderDto(string Id, string Name);

    private static ControllerBase CreateController()
    {
        var mock = new Mock<ControllerBase> { CallBase = true };
        mock.Object.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return mock.Object;
    }

    #region Sync

    [Fact]
    public void ToCreatedAtActionResult_Success_Returns201WithMetadataHeaders()
    {
        var controller = CreateController();
        var order = new Order("42", 1, "Widget");
        var result = Result.Ok(order);

        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: o => RepresentationMetadata.WithStrongETag(o.Version.ToString(CultureInfo.InvariantCulture)),
            map: o => new OrderDto(o.Id, o.Name));

        response.Result.Should().BeOfType<CreatedAtActionResult>();
        var created = response.Result.As<CreatedAtActionResult>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.ActionName.Should().Be("GetOrder");
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be("42");
        created.Value.Should().BeEquivalentTo(new OrderDto("42", "Widget"));
        controller.Response.Headers.ETag.ToString().Should().Be("\"1\"");
    }

    [Fact]
    public void ToCreatedAtActionResult_Success_WithLastModified_SetsHeader()
    {
        var controller = CreateController();
        var order = new Order("7", 2, "Gadget");
        var result = Result.Ok(order);
        var lastModified = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: _ => RepresentationMetadata.Create()
                .SetStrongETag("v2")
                .SetLastModified(lastModified)
                .Build(),
            map: o => new OrderDto(o.Id, o.Name));

        controller.Response.Headers.ETag.ToString().Should().Be("\"v2\"");
        controller.Response.Headers["Last-Modified"].ToString().Should().Be("Sat, 15 Jun 2024 12:00:00 GMT");
    }

    [Fact]
    public void ToCreatedAtActionResult_Success_WithControllerName_SetsControllerName()
    {
        var controller = CreateController();
        var order = new Order("1", 1, "Test");
        var result = Result.Ok(order);

        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: _ => RepresentationMetadata.WithStrongETag("etag"),
            map: o => new OrderDto(o.Id, o.Name),
            controllerName: "Orders");

        var created = response.Result.As<CreatedAtActionResult>();
        created.ControllerName.Should().Be("Orders");
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_ReturnsProblemDetails()
    {
        var controller = CreateController();
        var result = Result.Fail<Order>(new Error.BadRequest("bad.request") { Detail = "bad input" });

        var response = result.ToCreatedAtActionResult(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: o => RepresentationMetadata.WithStrongETag("etag"),
            map: o => new OrderDto(o.Id, o.Name));

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_DoesNotInvokeMetadataSelector()
    {
        var invoked = false;
        var controller = CreateController();
        var result = Result.Fail<Order>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "gone" });

        result.ToCreatedAtActionResult(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: _ => { invoked = true; return RepresentationMetadata.WithStrongETag("etag"); },
            map: o => new OrderDto(o.Id, o.Name));

        invoked.Should().BeFalse();
    }

    [Fact]
    public void ToCreatedAtActionResult_Failure_DoesNotSetMetadataHeaders()
    {
        var controller = CreateController();
        var result = Result.Fail<Order>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "gone" });

        result.ToCreatedAtActionResult(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: _ => RepresentationMetadata.WithStrongETag("etag"),
            map: o => new OrderDto(o.Id, o.Name));

        controller.Response.Headers.ETag.ToString().Should().BeEmpty();
    }

    #endregion

    #region Async Task

    [Fact]
    public async Task ToCreatedAtActionResultAsync_Task_Success_Returns201WithMetadata()
    {
        var controller = CreateController();
        var order = new Order("99", 3, "Async");
        var resultTask = Task.FromResult(Result.Ok(order));

        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: o => RepresentationMetadata.WithStrongETag(o.Version.ToString(CultureInfo.InvariantCulture)),
            map: o => new OrderDto(o.Id, o.Name));

        response.Result.Should().BeOfType<CreatedAtActionResult>();
        controller.Response.Headers.ETag.ToString().Should().Be("\"3\"");
    }

    [Fact]
    public async Task ToCreatedAtActionResultAsync_Task_Failure_ReturnsError()
    {
        var controller = CreateController();
        var resultTask = Task.FromResult(Result.Fail<Order>(new Error.Conflict(null, "conflict") { Detail = "exists" }));

        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: o => RepresentationMetadata.WithStrongETag("etag"),
            map: o => new OrderDto(o.Id, o.Name));

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    #endregion

    #region Async ValueTask

    [Fact]
    public async Task ToCreatedAtActionResultAsync_ValueTask_Success_Returns201WithMetadata()
    {
        var controller = CreateController();
        var order = new Order("55", 5, "VTask");
        var resultTask = new ValueTask<Result<Order>>(Result.Ok(order));

        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: o => RepresentationMetadata.WithStrongETag(o.Version.ToString(CultureInfo.InvariantCulture)),
            map: o => new OrderDto(o.Id, o.Name));

        response.Result.Should().BeOfType<CreatedAtActionResult>();
        controller.Response.Headers.ETag.ToString().Should().Be("\"5\"");
    }

    [Fact]
    public async Task ToCreatedAtActionResultAsync_ValueTask_Failure_ReturnsError()
    {
        var controller = CreateController();
        var resultTask = new ValueTask<Result<Order>>(Result.Fail<Order>(new Error.Forbidden("authorization.forbidden") { Detail = "denied" }));

        var response = await resultTask.ToCreatedAtActionResultAsync(controller,
            actionName: "GetOrder",
            routeValues: o => new { id = o.Id },
            metadataSelector: o => RepresentationMetadata.WithStrongETag("etag"),
            map: o => new OrderDto(o.Id, o.Name));

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    #endregion
}