namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Tests for Minimal API Prefer header / <c>ToUpdatedHttpResult</c> and <c>WriteOutcome.ToHttpResult</c>.
/// Covers RFC 7240 Prefer semantics, metadata headers, and all WriteOutcome variants for Minimal API.
/// </summary>
[Collection("TrellisAspOptionsState")]
public class WriteOutcomeHttpResultTests : IDisposable
{
    public WriteOutcomeHttpResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private static DefaultHttpContext CreateHttpContext(string? preferValue = null, string method = "PUT")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (preferValue is not null)
            context.Request.Headers["Prefer"] = preferValue;
        return context;
    }

    #region WriteOutcome.ToHttpResult — Updated

    [Fact]
    public void WriteOutcome_Updated_NoPrefer_Returns200()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        result.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>().Value.Should().Be("value");
        httpContext.Response.Headers.Vary.ToString().Should().Contain("Prefer");
        httpContext.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
    }

    [Fact]
    public void WriteOutcome_Updated_ReturnMinimal_Returns204()
    {
        var httpContext = CreateHttpContext("return=minimal");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        httpContext.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
        httpContext.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    [Fact]
    public void WriteOutcome_Updated_ReturnRepresentation_Returns200()
    {
        var httpContext = CreateHttpContext("return=representation");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        httpContext.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
        httpContext.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    [Fact]
    public void WriteOutcome_Updated_WithMetadata_AppliesHeaders()
    {
        var httpContext = CreateHttpContext();
        var metadata = RepresentationMetadata.WithStrongETag("abc123");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value", metadata);

        outcome.ToHttpResult(httpContext);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"abc123\"");
    }

    [Fact]
    public void WriteOutcome_Updated_ReturnMinimal_WithMap_DoesNotInvokeMap()
    {
        var httpContext = CreateHttpContext("return=minimal");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value");
        var mapInvoked = false;

        outcome.ToHttpResult(httpContext, (Func<string, string>)(s =>
        {
            mapInvoked = true;
            return s.ToUpperInvariant();
        }));

        mapInvoked.Should().BeFalse("map should not be invoked when returning 204");
    }

    [Fact]
    public void WriteOutcome_Updated_WithMap_AppliesMap()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("hello");

        var result = outcome.ToHttpResult(httpContext, (Func<string, string>)(s => s.ToUpperInvariant()));

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        result.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>().Value.Should().Be("HELLO");
    }

    [Fact]
    public void WriteOutcome_Updated_ReturnMinimal_StillAppliesMetadata()
    {
        var httpContext = CreateHttpContext("return=minimal");
        var metadata = RepresentationMetadata.WithStrongETag("etag2");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value", metadata);

        outcome.ToHttpResult(httpContext);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag2\"");
    }

    [Fact]
    public void WriteOutcome_Updated_ReturnRepresentation_WithMetadataVary_PreservesPrefer()
    {
        var httpContext = CreateHttpContext("return=representation");
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("etag4")
            .AddVary("Accept")
            .Build();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("value", metadata);

        outcome.ToHttpResult(httpContext);

        var vary = httpContext.Response.Headers.Vary.ToString();
        vary.Should().Contain("Accept");
        vary.Should().Contain("Prefer");
    }

    #endregion

    #region WriteOutcome.ToHttpResult — Created

    [Fact]
    public void WriteOutcome_Created_Returns201WithLocation()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("item", "/api/items/1");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<string>>();
        result.As<Microsoft.AspNetCore.Http.HttpResults.Created<string>>().Location.Should().Be("/api/items/1");
    }

    [Fact]
    public void WriteOutcome_Created_ReturnMinimal_StillReturns201()
    {
        var httpContext = CreateHttpContext("return=minimal");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("item", "/api/items/1");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<string>>();
    }

    [Fact]
    public void WriteOutcome_Created_WithMetadata_AppliesETag()
    {
        var httpContext = CreateHttpContext();
        var metadata = RepresentationMetadata.WithStrongETag("new-etag");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("item", "/api/items/1", metadata);

        outcome.ToHttpResult(httpContext);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"new-etag\"");
    }

    #endregion

    #region WriteOutcome.ToHttpResult — UpdatedNoContent

    [Fact]
    public void WriteOutcome_UpdatedNoContent_Returns204()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.UpdatedNoContent();

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
    }

    [Fact]
    public void WriteOutcome_UpdatedNoContent_WithMetadata_AppliesETag()
    {
        var httpContext = CreateHttpContext();
        var metadata = RepresentationMetadata.WithStrongETag("etag-nc");
        WriteOutcome<string> outcome = new WriteOutcome<string>.UpdatedNoContent(metadata);

        outcome.ToHttpResult(httpContext);

        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag-nc\"");
    }

    #endregion

    #region WriteOutcome.ToHttpResult — Accepted / AcceptedNoContent

    [Fact]
    public void WriteOutcome_AcceptedNoContent_Returns202()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.AcceptedNoContent("/api/status/1");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>();
        result.As<Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult>().StatusCode.Should().Be(StatusCodes.Status202Accepted);
        httpContext.Response.Headers.Location.ToString().Should().Be("/api/status/1");
    }

    [Fact]
    public void WriteOutcome_AcceptedNoContent_WithRetryAfter_SetsHeader()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.AcceptedNoContent(RetryAfter: RetryAfterValue.FromSeconds(30));

        outcome.ToHttpResult(httpContext);

        httpContext.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public void WriteOutcome_Accepted_WithBody_Returns202()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Accepted("processing", "/api/status/1");

        var result = outcome.ToHttpResult(httpContext);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Accepted<string>>();
        result.As<Microsoft.AspNetCore.Http.HttpResults.Accepted<string>>().StatusCode.Should().Be(StatusCodes.Status202Accepted);
        result.As<Microsoft.AspNetCore.Http.HttpResults.Accepted<string>>().Value.Should().Be("processing");
    }

    [Fact]
    public void WriteOutcome_Accepted_WithBody_AndMap_Returns202WithMappedValue()
    {
        var httpContext = CreateHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Accepted("processing", "/api/status/1");

        var result = outcome.ToHttpResult(httpContext, (Func<string, string>)(s => s.ToUpperInvariant()));

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Accepted<string>>();
        result.As<Microsoft.AspNetCore.Http.HttpResults.Accepted<string>>().Value.Should().Be("PROCESSING");
    }

    #endregion

    #region ToUpdatedHttpResult — static metadata

    [Fact]
    public void ToUpdatedHttpResult_Success_NoPrefer_Returns200()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Success("updated");
        var metadata = RepresentationMetadata.WithStrongETag("etag1");

        var response = result.ToUpdatedHttpResult(httpContext, metadata, (string s) => s.ToUpperInvariant());

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>().Value.Should().Be("UPDATED");
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag1\"");
    }

    [Fact]
    public void ToUpdatedHttpResult_Success_ReturnMinimal_Returns204()
    {
        var httpContext = CreateHttpContext("return=minimal");
        var result = Result.Success("updated");
        var metadata = RepresentationMetadata.WithStrongETag("etag2");

        var response = result.ToUpdatedHttpResult(httpContext, metadata, (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag2\"");
        httpContext.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
        httpContext.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    [Fact]
    public void ToUpdatedHttpResult_Success_ReturnRepresentation_Returns200()
    {
        var httpContext = CreateHttpContext("return=representation");
        var result = Result.Success("updated");
        var metadata = RepresentationMetadata.WithStrongETag("etag3");

        var response = result.ToUpdatedHttpResult(httpContext, metadata, (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        httpContext.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
    }

    [Fact]
    public void ToUpdatedHttpResult_Failure_ReturnsError()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Failure<string>(Error.NotFound("not found"));

        var response = result.ToUpdatedHttpResult(httpContext, (RepresentationMetadata?)null, (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }

    #endregion

    #region ToUpdatedHttpResult — metadata selector

    [Fact]
    public void ToUpdatedHttpResult_Selector_Success_ReturnMinimal_Returns204()
    {
        var httpContext = CreateHttpContext("return=minimal");
        var result = Result.Success("updated");

        var response = result.ToUpdatedHttpResult(
            httpContext,
            _ => RepresentationMetadata.WithStrongETag("dynamic-etag"),
            (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"dynamic-etag\"");
    }

    [Fact]
    public void ToUpdatedHttpResult_Selector_Failure_DoesNotInvokeSelector()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Failure<string>(Error.NotFound("gone"));
        var selectorInvoked = false;

        result.ToUpdatedHttpResult(
            httpContext,
            _ => { selectorInvoked = true; return RepresentationMetadata.WithStrongETag("x"); },
            (string s) => s);

        selectorInvoked.Should().BeFalse("selector should not be invoked for failed results");
    }

    #endregion

    #region Async variants — Task

    [Fact]
    public async Task ToUpdatedHttpResultAsync_Task_StaticMetadata_Success_Returns200()
    {
        var httpContext = CreateHttpContext();
        var resultTask = Task.FromResult(Result.Success("updated"));
        var metadata = RepresentationMetadata.WithStrongETag("etag-async");

        var response = await resultTask.ToUpdatedHttpResultAsync(httpContext, metadata, (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag-async\"");
    }

    [Fact]
    public async Task ToUpdatedHttpResultAsync_Task_Selector_ReturnMinimal_Returns204()
    {
        var httpContext = CreateHttpContext("return=minimal");
        var resultTask = Task.FromResult(Result.Success("updated"));

        var response = await resultTask.ToUpdatedHttpResultAsync(
            httpContext,
            _ => RepresentationMetadata.WithStrongETag("sel-etag"),
            (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"sel-etag\"");
    }

    #endregion

    #region Async variants — ValueTask

    [Fact]
    public async Task ToUpdatedHttpResultAsync_ValueTask_StaticMetadata_Success_Returns200()
    {
        var httpContext = CreateHttpContext();
        var resultTask = ValueTask.FromResult(Result.Success("updated"));
        var metadata = RepresentationMetadata.WithStrongETag("etag-vt");

        var response = await resultTask.ToUpdatedHttpResultAsync(httpContext, metadata, (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"etag-vt\"");
    }

    [Fact]
    public async Task ToUpdatedHttpResultAsync_ValueTask_Selector_ReturnMinimal_Returns204()
    {
        var httpContext = CreateHttpContext("return=minimal");
        var resultTask = ValueTask.FromResult(Result.Success("updated"));

        var response = await resultTask.ToUpdatedHttpResultAsync(
            httpContext,
            _ => RepresentationMetadata.WithStrongETag("vt-sel-etag"),
            (string s) => s);

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        httpContext.Response.Headers.ETag.ToString().Should().Be("\"vt-sel-etag\"");
    }

    #endregion
}