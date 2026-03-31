namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trellis;

/// <summary>
/// Tests for RepresentationMetadata-aware ToActionResult overloads.
/// </summary>
public class MetadataActionResultTests : IDisposable
{
    public MetadataActionResultTests() => TrellisAspOptions.ResetCurrent();

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

    #region Success with metadata headers

    [Fact]
    public void Success_WithMetadata_Returns200_WithAllHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("abc123")
            .SetLastModified(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero))
            .AddVary("Accept", "Accept-Language")
            .AddContentLanguage("en", "fr")
            .SetContentLocation("/api/items/1")
            .SetAcceptRanges("bytes")
            .Build();

        var response = result.ToActionResult(controller, metadata, s => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
        controller.Response.Headers.ETag.ToString().Should().Be("\"abc123\"");
        controller.Response.Headers["Last-Modified"].ToString().Should().Be("Sat, 15 Jun 2024 12:00:00 GMT");
        controller.Response.Headers.Vary.ToString().Should().Be("Accept, Accept-Language");
        controller.Response.Headers.ContentLanguage.ToString().Should().Be("en, fr");
        controller.Response.Headers["Content-Location"].ToString().Should().Be("/api/items/1");
        controller.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
    }

    [Fact]
    public void Success_WithWeakETag_SetsWeakETagHeader()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Success("hello");
        var metadata = RepresentationMetadata.Create()
            .SetWeakETag("abc123")
            .Build();

        result.ToActionResult(controller, metadata, s => s);

        controller.Response.Headers.ETag.ToString().Should().Be("W/\"abc123\"");
    }

    #endregion

    #region 304 Not Modified

    [Fact]
    public void Success_WithETag_MatchingIfNoneMatch_OnGET_Returns304()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"abc123\"");
        var result = Result.Success("hello");
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var response = result.ToActionResult(controller, metadata, s => s);

        response.Result.Should().BeOfType<StatusCodeResult>();
        response.Result.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void Success_WithETag_MatchingIfNoneMatch_OnHEAD_Returns304()
    {
        var controller = CreateControllerWithHttpContext("HEAD", "\"abc123\"");
        var result = Result.Success("hello");
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var response = result.ToActionResult(controller, metadata, s => s);

        response.Result.Should().BeOfType<StatusCodeResult>();
        response.Result.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    [Fact]
    public void Success_WithETag_NonMatchingIfNoneMatch_Returns200()
    {
        var controller = CreateControllerWithHttpContext("GET", "\"different\"");
        var result = Result.Success("hello");
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var response = result.ToActionResult(controller, metadata, s => s);

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public void Success_WithETag_MatchingIfNoneMatch_OnPOST_Returns200()
    {
        var controller = CreateControllerWithHttpContext("POST", "\"abc123\"");
        var result = Result.Success("hello");
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var response = result.ToActionResult(controller, metadata, s => s);

        response.Result.As<OkObjectResult>().StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    #endregion

    #region Failure

    [Fact]
    public void Failure_ReturnsErrorStatus_NoMetadataHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var result = Result.Failure<string>(Error.NotFound("gone"));
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var response = result.ToActionResult(controller, metadata, s => s);

        response.Result.Should().BeOfType<ObjectResult>();
        response.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
        controller.Response.Headers.ETag.ToString().Should().BeEmpty();
    }

    #endregion

    #region Async overloads

    [Fact]
    public async Task TaskOverload_Success_Returns200WithHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = Task.FromResult(Result.Success("hello"));
        var metadata = RepresentationMetadata.WithStrongETag("etag1");

        var response = await resultTask.ToActionResultAsync(controller, metadata, s => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag1\"");
    }

    [Fact]
    public async Task ValueTaskOverload_Success_Returns200WithHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var resultTask = new ValueTask<Result<string>>(Result.Success("hello"));
        var metadata = RepresentationMetadata.WithStrongETag("etag2");

        var response = await resultTask.ToActionResultAsync(controller, metadata, s => s.ToUpperInvariant());

        response.Result.As<OkObjectResult>().Value.Should().Be("HELLO");
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag2\"");
    }

    #endregion
}
