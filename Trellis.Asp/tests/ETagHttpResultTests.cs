namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Tests for Minimal API ETag-aware ToHttpResult overloads:
/// ETag header setting, If-None-Match → 304, and failure passthrough.
/// </summary>
[Collection("TrellisAspOptionsState")]
public class ETagHttpResultTests : IDisposable
{
    public ETagHttpResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private static DefaultHttpContext CreateHttpContext(string method = "GET", string? ifNoneMatch = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (ifNoneMatch is not null)
            context.Request.Headers.IfNoneMatch = ifNoneMatch;
        return context;
    }

    #region ToHttpResult — ETag header

    [Fact]
    public void ToHttpResult_Success_SetsQuotedETagHeader()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Success("hello");

        result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"abc123\"");
    }

    [Fact]
    public void ToHttpResult_Success_EmptyETag_DoesNotSetHeader()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Success("hello");

        result.ToHttpResult(httpContext, _ => RepresentationMetadata.Create().Build(), s => s);

        httpContext.Response.Headers.ETag.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ToHttpResult_Success_ReturnsOkWithMappedValue()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("etag"), s => s.ToUpperInvariant());

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    [Fact]
    public void ToHttpResult_Failure_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Failure<string>(Error.NotFound("gone"));

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("etag"), s => s);

        // Problem Details result for 404
        response.Should().NotBeNull();
    }

    #endregion

    #region ToHttpResult — If-None-Match → 304

    [Fact]
    public void ToHttpResult_GET_IfNoneMatchMatches_Returns304()
    {
        var httpContext = CreateHttpContext("GET", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"abc123\"");
    }

    [Fact]
    public void ToHttpResult_HEAD_IfNoneMatchMatches_Returns304()
    {
        var httpContext = CreateHttpContext("HEAD", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
    }

    [Fact]
    public void ToHttpResult_GET_IfNoneMatchDoesNotMatch_Returns200()
    {
        var httpContext = CreateHttpContext("GET", "\"other\"");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    [Fact]
    public void ToHttpResult_PUT_IfNoneMatchMatches_DoesNotReturn304()
    {
        // 304 is only for GET/HEAD
        var httpContext = CreateHttpContext("PUT", "\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    [Fact]
    public void ToHttpResult_GET_Wildcard_Returns304()
    {
        var httpContext = CreateHttpContext("GET", "*");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("any-etag"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
    }

    [Fact]
    public void ToHttpResult_GET_WeakIfNoneMatch_Returns304()
    {
        // RFC 9110 §13.1.2: If-None-Match uses weak comparison
        var httpContext = CreateHttpContext("GET", "W/\"abc123\"");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
    }

    [Fact]
    public void ToHttpResult_GET_NoIfNoneMatch_Returns200()
    {
        var httpContext = CreateHttpContext("GET");
        var result = Result.Success("hello");

        var response = result.ToHttpResult(httpContext, _ => RepresentationMetadata.WithStrongETag("abc123"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    #endregion

    #region ToCreatedHttpResult

    [Fact]
    public void ToCreatedHttpResult_Success_SetsETagHeader()
    {
        var httpContext = CreateHttpContext("POST");
        var result = Result.Success("hello");

        result.ToCreatedHttpResult(httpContext, _ => "/items/1", _ => RepresentationMetadata.WithStrongETag("etag-created"), s => s);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag-created\"");
    }

    [Fact]
    public void ToCreatedHttpResult_Success_ReturnsCreated()
    {
        var httpContext = CreateHttpContext("POST");
        var result = Result.Success("hello");

        var response = result.ToCreatedHttpResult(httpContext, _ => "/items/1", _ => RepresentationMetadata.WithStrongETag("etag"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<string>>();
    }

    [Fact]
    public void ToCreatedHttpResult_Failure_ReturnsError()
    {
        var httpContext = CreateHttpContext("POST");
        var result = Result.Failure<string>(Error.Validation("bad", "field"));

        var response = result.ToCreatedHttpResult(httpContext, _ => "/items/1", _ => RepresentationMetadata.WithStrongETag("etag"), s => s);

        response.Should().NotBeNull();
    }

    #endregion

    #region Async overloads

    [Fact]
    public async Task ToHttpResultAsync_Task_SetsETagAndReturns200()
    {
        var httpContext = CreateHttpContext();
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToHttpResultAsync(httpContext, _ => RepresentationMetadata.WithStrongETag("etag-async"), s => s);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag-async\"");
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_GET_IfNoneMatchMatches_Returns304()
    {
        var httpContext = CreateHttpContext("GET", "\"match\"");
        var resultTask = Task.FromResult(Result.Success("hello"));

        var response = await resultTask.ToHttpResultAsync(httpContext, _ => RepresentationMetadata.WithStrongETag("match"), s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
    }

    [Fact]
    public async Task ToCreatedHttpResultAsync_Task_SetsETag()
    {
        var httpContext = CreateHttpContext("POST");
        var resultTask = Task.FromResult(Result.Success("hello"));

        await resultTask.ToCreatedHttpResultAsync(httpContext, _ => "/items/1", _ => RepresentationMetadata.WithStrongETag("etag-cr"), s => s);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag-cr\"");
    }

    #endregion
}
