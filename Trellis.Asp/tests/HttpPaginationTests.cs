namespace Trellis.Asp.Tests;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// Tests for Minimal API pagination: <see cref="PartialContentHttpResult"/> and
/// <see cref="HttpResultExtensions"/> pagination overloads (206 Partial Content / 200 OK).
/// </summary>
[Collection("TrellisAspOptionsState")]
public class HttpPaginationTests : IDisposable
{
    public HttpPaginationTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    #region PartialContentHttpResult — constructor and execution

    [Fact]
    public async Task PartialContentHttpResult_ExecuteAsync_SetsStatusCode206()
    {
        var inner = Results.Ok("data");
        var sut = new PartialContentHttpResult(0, 1, 10, inner);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        await sut.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
    }

    [Fact]
    public async Task PartialContentHttpResult_ExecuteAsync_SetsContentRangeHeader()
    {
        var inner = Results.Ok("data");
        var sut = new PartialContentHttpResult(0, 1, 10, inner);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

        await sut.ExecuteAsync(httpContext);

        httpContext.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.ContentRange]
            .ToString().Should().Be("items 0-1/10");
    }

    [Fact]
    public void PartialContentHttpResult_ContentRange_CorrectFormat()
    {
        var inner = Results.Ok("data");
        var sut = new PartialContentHttpResult(0, 1, 10, inner);

        sut.ContentRangeHeaderValue.ToString().Should().Be("items 0-1/10");
    }

    [Fact]
    public void PartialContentHttpResult_WithContentRangeHeaderValue_PreservesRange()
    {
        var range = new ContentRangeHeaderValue(10, 19, 100) { Unit = "items" };
        var inner = Results.Ok("data");
        var sut = new PartialContentHttpResult(range, inner);

        sut.ContentRangeHeaderValue.Should().Be(range);
    }

    [Fact]
    public void PartialContentHttpResult_UnknownTotal_FormatsWithWildcard()
    {
        var inner = Results.Ok("data");
        var sut = new PartialContentHttpResult(0, 24, null, inner);

        sut.ContentRangeHeaderValue.ToString().Should().Be("items 0-24/*");
    }

    #endregion

    #region ToHttpResult — direct range parameters

    [Fact]
    public void ToHttpResult_PartialRange_Returns206()
    {
        var data = new[] { "a", "b", "c" };
        var result = Result.Ok(data);

        var response = result.ToHttpResult(0, 2, 10);

        response.Should().BeOfType<PartialContentHttpResult>();
        response.As<PartialContentHttpResult>().ContentRangeHeaderValue.ToString()
            .Should().Be("items 0-2/10");
    }

    [Fact]
    public void ToHttpResult_CompleteRange_Returns200()
    {
        var data = new[] { "a", "b", "c" };
        var result = Result.Ok(data);

        var response = result.ToHttpResult(0, 2, 3);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string[]>>();
    }

    [Fact]
    public void ToHttpResult_Failure_ReturnsError()
    {
        var result = Result.Fail<string[]>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var response = result.ToHttpResult(0, 2, 10);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }

    [Fact]
    public void ToHttpResult_EmptyRange_ToLessThanFrom_Returns200()
    {
        var result = Result.Ok("data");

        var response = result.ToHttpResult(5, 4, 10);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    [Fact]
    public void ToHttpResult_ZeroTotalLength_Returns200()
    {
        var result = Result.Ok("data");

        var response = result.ToHttpResult(0, 0, 0);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    #endregion

    #region ToHttpResult — lambda-based ContentRangeHeaderValue

    [Fact]
    public void ToHttpResult_Lambda_PartialRange_Returns206()
    {
        var items = new[] { "a", "b" };
        var result = Result.Ok(("items", items, 10L));

        var response = result.ToHttpResult(
            r => new ContentRangeHeaderValue(0, 1, r.Item3) { Unit = "items" },
            r => r.Item2);

        response.Should().BeOfType<PartialContentHttpResult>();
    }

    [Fact]
    public void ToHttpResult_Lambda_CompleteRange_Returns200()
    {
        var items = new[] { "a" };
        var result = Result.Ok(("items", items, 1L));

        var response = result.ToHttpResult(
            r => new ContentRangeHeaderValue(0, 0, r.Item3) { Unit = "items" },
            r => r.Item2);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string[]>>();
    }

    [Fact]
    public void ToHttpResult_Lambda_NullRangeFields_Returns200()
    {
        var result = Result.Ok("data");

        var response = result.ToHttpResult(
            _ => new ContentRangeHeaderValue(0) { Unit = "items" },
            s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
    }

    [Fact]
    public void ToHttpResult_Lambda_Failure_ReturnsError()
    {
        var result = Result.Fail<string>(new Error.BadRequest("bad.request") { Detail = "bad" });

        var response = result.ToHttpResult(
            _ => new ContentRangeHeaderValue(0, 0, 1) { Unit = "items" },
            s => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }

    #endregion

    #region Async variants — Task

    [Fact]
    public async Task ToHttpResultAsync_Task_PartialRange_Returns206()
    {
        var data = new[] { "a", "b" };
        var resultTask = Task.FromResult(Result.Ok(data));

        var response = await resultTask.ToHttpResultAsync(0, 2, 10);

        response.Should().BeOfType<PartialContentHttpResult>();
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_CompleteRange_Returns200()
    {
        var data = new[] { "a", "b" };
        var resultTask = Task.FromResult(Result.Ok(data));

        var response = await resultTask.ToHttpResultAsync(0, 1, 2);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string[]>>();
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_Lambda_PartialRange_Returns206()
    {
        var items = new[] { "a" };
        var resultTask = Task.FromResult(Result.Ok((items, 5L)));

        var response = await resultTask.ToHttpResultAsync(
            r => new ContentRangeHeaderValue(0, 0, r.Item2) { Unit = "items" },
            r => r.Item1);

        response.Should().BeOfType<PartialContentHttpResult>();
    }

    #endregion

    #region Async variants — ValueTask

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_PartialRange_Returns206()
    {
        var data = new[] { "a", "b" };
        var resultTask = ValueTask.FromResult(Result.Ok(data));

        var response = await resultTask.ToHttpResultAsync(0, 2, 10);

        response.Should().BeOfType<PartialContentHttpResult>();
    }

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_CompleteRange_Returns200()
    {
        var data = new[] { "a", "b" };
        var resultTask = ValueTask.FromResult(Result.Ok(data));

        var response = await resultTask.ToHttpResultAsync(0, 1, 2);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string[]>>();
    }

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_Lambda_PartialRange_Returns206()
    {
        var items = new[] { "a" };
        var resultTask = ValueTask.FromResult(Result.Ok((items, 5L)));

        var response = await resultTask.ToHttpResultAsync(
            r => new ContentRangeHeaderValue(0, 0, r.Item2) { Unit = "items" },
            r => r.Item1);

        response.Should().BeOfType<PartialContentHttpResult>();
    }

    #endregion
}