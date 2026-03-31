namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using Trellis;

/// <summary>
/// Tests for ETag-aware ActionResult extensions: ToETagActionResult, ToCreatedAtETagActionResult,
/// SuccessWithETag (304 Not Modified), and SetETagHeader.
/// </summary>
public class ETagActionResultTests : IDisposable
{
    public ETagActionResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private static ControllerBase CreateControllerWithHttpContext(string method = "GET", string? ifNoneMatch = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        if (ifNoneMatch is not null)
            httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;

        var mock = new Mock<ControllerBase> { CallBase = true };
        mock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return mock.Object;
    }

    #region ToETagActionResult — Success

    [Fact]
    public void ToETagActionResult_Success_ReturnsOkWithMappedValue()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "etag1", s => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
    }

    [Fact]
    public void ToETagActionResult_Success_SetsETagHeader()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        result.ToETagActionResult(controller, _ => "abc123", s => s);

        controller.Response.Headers.ETag.ToString().Should().Be("\"abc123\"");
    }

    [Fact]
    public void ToETagActionResult_Success_EmptyETag_DoesNotSetHeader()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        result.ToETagActionResult(controller, _ => "", s => s);

        controller.Response.Headers.ETag.ToString().Should().BeEmpty();
    }

    #endregion

    #region ToETagActionResult — Failure

    [Fact]
    public void ToETagActionResult_Failure_ReturnsErrorStatusCode()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.NotFound("gone"));

        var response = result.ToETagActionResult(controller, _ => "etag", s => s);

        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToETagActionResult_PreconditionFailed_Returns412()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.PreconditionFailed("stale"));

        var response = result.ToETagActionResult(controller, _ => "etag", s => s);

        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    #endregion

    #region If-None-Match → 304 Not Modified

    [Fact]
    public void ToETagActionResult_GET_IfNoneMatchMatches_Returns304()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "abc123", s => s);

        var statusResult = response.Result as IStatusCodeActionResult;
        statusResult!.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToETagActionResult_GET_IfNoneMatchDoesNotMatch_Returns200()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"other\"");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "abc123", s => s);

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void ToETagActionResult_HEAD_IfNoneMatchMatches_Returns304()
    {
        var controller = CreateControllerWithHttpContext("HEAD", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "abc123", s => s);

        var statusResult = response.Result as IStatusCodeActionResult;
        statusResult!.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToETagActionResult_PUT_IfNoneMatchMatches_DoesNotReturn304()
    {
        // 304 is only for GET/HEAD, not unsafe methods
        var controller = CreateControllerWithHttpContext("PUT", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "abc123", s => s);

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void ToETagActionResult_GET_IfNoneMatchWildcard_Returns304()
    {
        var controller = CreateControllerWithHttpContext("GET", "*");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "any-etag", s => s);

        var statusResult = response.Result as IStatusCodeActionResult;
        statusResult!.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToETagActionResult_GET_WeakIfNoneMatchMatches_Returns304()
    {
        // RFC 9110 §13.1.2: If-None-Match uses weak comparison
        var controller = CreateControllerWithHttpContext("GET", "W/\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "abc123", s => s);

        var statusResult = response.Result as IStatusCodeActionResult;
        statusResult!.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void ToETagActionResult_GET_NoIfNoneMatch_Returns200()
    {
        var controller = CreateControllerWithHttpContext("GET");
        var result = Result.Success("hello");

        var response = result.ToETagActionResult(controller, _ => "abc123", s => s);

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    #endregion

    #region ToCreatedAtETagActionResult

    [Fact]
    public void ToCreatedAtETagActionResult_Success_SetsETagHeader()
    {
        var controller = CreateControllerWithHttpContext("POST");
        var result = Result.Success("hello");

        result.ToCreatedAtETagActionResult(controller, "GetById", _ => new { id = 1 }, _ => "etag1", s => s);

        controller.Response.Headers.ETag.ToString().Should().Be("\"etag1\"");
    }

    [Fact]
    public void ToCreatedAtETagActionResult_Failure_ReturnsError()
    {
        var controller = CreateControllerWithHttpContext("POST");
        var result = Result.Failure<string>(Error.Validation("bad", "field"));

        var response = result.ToCreatedAtETagActionResult(controller, "GetById", _ => new { id = 1 }, _ => "etag", s => s);

        response.Result.Should().NotBeNull();
    }

    #endregion

    #region Async Overloads

    [Fact]
    public async Task ToETagActionResultAsync_Task_Success_SetsETagAndReturns200()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToETagActionResultAsync(controller, _ => "etag-task", s => s);

        response.Result.As<OkObjectResult>().Value.Should().Be("hello");
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag-task\"");
    }

    [Fact]
    public async Task ToETagActionResultAsync_ValueTask_Success_SetsETagAndReturns200()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = new ValueTask<Result<string>>(Result.Success("hello"));

        var response = await resultTask.ToETagActionResultAsync(controller, _ => "etag-vt", s => s);

        response.Result.As<OkObjectResult>().Value.Should().Be("hello");
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag-vt\"");
    }

    [Fact]
    public async Task ToETagActionResultAsync_Task_GET_IfNoneMatchMatches_Returns304()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"match-me\"");
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToETagActionResultAsync(controller, _ => "match-me", s => s);

        var statusResult = response.Result as IStatusCodeActionResult;
        statusResult!.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public async Task ToCreatedAtETagActionResultAsync_Task_Success_SetsETag()
    {
        var controller = CreateControllerWithHttpContext("POST");
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToCreatedAtETagActionResultAsync(controller, "GetById", _ => new { id = 1 }, _ => "etag-created", s => s);

        controller.Response.Headers.ETag.ToString().Should().Be("\"etag-created\"");
    }

    #endregion
}
