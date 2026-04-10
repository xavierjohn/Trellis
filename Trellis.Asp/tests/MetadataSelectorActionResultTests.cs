namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trellis;

/// <summary>
/// Tests for <see cref="ActionResultExtensions.ToActionResult{TIn,TOut}(Result{TIn}, ControllerBase, Func{TIn, RepresentationMetadata}, Func{TIn, TOut})"/>
/// — the metadata selector overload that derives metadata from the domain value (e.g., ETag from aggregate).
/// </summary>
[Collection("TrellisAspOptionsState")]
public class MetadataSelectorActionResultTests : IDisposable
{
    public MetadataSelectorActionResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private static ControllerBase CreateControllerWithHttpContext(
        string method = "GET",
        string? ifNoneMatch = null,
        string? ifMatch = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        if (ifNoneMatch is not null)
            httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;
        if (ifMatch is not null)
            httpContext.Request.Headers.IfMatch = ifMatch;

        var mock = new Mock<ControllerBase> { CallBase = true };
        mock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return mock.Object;
    }

    #region Success with metadata selector

    [Fact]
    public void Success_WithSelector_Returns200_WithMetadataHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("dynamic-etag"),
            (string s) => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
        controller.Response.Headers.ETag.ToString().Should().Be("\"dynamic-etag\"");
    }

    [Fact]
    public void Success_WithSelector_AppliesFullMetadata()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.Create()
                .SetStrongETag("etag1")
                .SetLastModified(new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero))
                .AddVary("Accept")
                .Build(),
            (string s) => s);

        response.Result.Should().BeOfType<OkObjectResult>();
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag1\"");
        controller.Response.Headers["Last-Modified"].ToString().Should().Be("Wed, 15 Jan 2025 12:00:00 GMT");
        controller.Response.Headers.Vary.ToString().Should().Be("Accept");
    }

    [Fact]
    public void Success_SelectorReceivesDomainValue()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("my-etag-source");
        string? capturedValue = null;

        result.ToActionResult(
            controller,
            value => { capturedValue = value; return RepresentationMetadata.WithStrongETag(value); },
            (string s) => s);

        capturedValue.Should().Be("my-etag-source");
        controller.Response.Headers.ETag.ToString().Should().Be("\"my-etag-source\"");
    }

    #endregion

    #region Failure — selector not invoked

    [Fact]
    public void Failure_SelectorNotInvoked()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.NotFound("not found"));
        var selectorInvoked = false;

        result.ToActionResult(
            controller,
            _ => { selectorInvoked = true; return RepresentationMetadata.WithStrongETag("x"); },
            (string s) => s);

        selectorInvoked.Should().BeFalse("selector should not be invoked for failed results");
    }

    [Fact]
    public void Failure_ReturnsErrorResponse()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.NotFound("not found"));

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("x"),
            (string s) => s);

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void Failure_DoesNotEmitMetadataHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.NotFound("not found"));

        result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("should-not-appear"),
            (string s) => s);

        controller.Response.Headers.ETag.ToString().Should().BeEmpty();
    }

    #endregion

    #region Conditional GET — 304 Not Modified

    [Fact]
    public void Success_MatchingIfNoneMatch_OnGET_Returns304()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("abc123"),
            (string s) => s);

        response.Result.Should().BeOfType<StatusCodeResult>();
        response.Result.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void Success_NonMatchingIfNoneMatch_OnGET_Returns200()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"old-etag\"");
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("new-etag"),
            (string s) => s);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void Success_MatchingIfNoneMatch_OnHEAD_Returns304()
    {
        var controller = CreateControllerWithHttpContext("HEAD", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("abc123"),
            (string s) => s);

        response.Result.Should().BeOfType<StatusCodeResult>();
        response.Result.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    #endregion

    #region Conditional request — 412 Precondition Failed

    [Fact]
    public void Success_FailedIfMatch_OnGET_Returns412()
    {
        var controller = CreateControllerWithHttpContext("GET", ifMatch: "\"wrong-etag\"");
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("actual-etag"),
            (string s) => s);

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    #endregion

    #region Non-safe methods — conditional not evaluated

    [Fact]
    public void Success_MatchingIfNoneMatch_OnPUT_Returns200()
    {
        var controller = CreateControllerWithHttpContext("PUT", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("abc123"),
            (string s) => s);

        response.Result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region Async variants — Task

    [Fact]
    public async Task ToActionResultAsync_Task_Success_Returns200_WithMetadata()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToActionResultAsync(
            controller,
            _ => RepresentationMetadata.WithStrongETag("async-etag"),
            (string s) => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
        controller.Response.Headers.ETag.ToString().Should().Be("\"async-etag\"");
    }

    [Fact]
    public async Task ToActionResultAsync_Task_MatchingIfNoneMatch_Returns304()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"etag1\"");
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToActionResultAsync(
            controller,
            _ => RepresentationMetadata.WithStrongETag("etag1"),
            (string s) => s);

        response.Result.Should().BeOfType<StatusCodeResult>();
        response.Result.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    #endregion

    #region Async variants — ValueTask

    [Fact]
    public async Task ToActionResultAsync_ValueTask_Success_Returns200_WithMetadata()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = ValueTask.FromResult(Result.Success("hello"));

        var response = await resultTask.ToActionResultAsync(
            controller,
            _ => RepresentationMetadata.WithStrongETag("vt-etag"),
            (string s) => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
        controller.Response.Headers.ETag.ToString().Should().Be("\"vt-etag\"");
    }

    [Fact]
    public async Task ToActionResultAsync_ValueTask_Failure_ReturnsError()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = ValueTask.FromResult(Result.Failure<string>(Error.NotFound("gone")));

        var response = await resultTask.ToActionResultAsync(
            controller,
            _ => RepresentationMetadata.WithStrongETag("x"),
            (string s) => s);

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    #endregion
}