namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trellis;

/// <summary>
/// Tests for <see cref="WriteOutcomeExtensions"/> Prefer header support (RFC 7240).
/// Covers the overloads that accept <see cref="HttpRequest"/> and honor the <c>Prefer</c> header
/// when mapping <see cref="WriteOutcome{T}"/> to ActionResult/IResult.
/// </summary>
public class WriteOutcomePreferTests
{
    private static (ControllerBase Controller, DefaultHttpContext HttpContext) CreateControllerWithPrefer(string? preferValue = null)
    {
        var httpContext = new DefaultHttpContext();
        if (preferValue is not null)
            httpContext.Request.Headers["Prefer"] = preferValue;
        var mock = new Mock<ControllerBase> { CallBase = true };
        mock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (mock.Object, httpContext);
    }

    #region Updated + return=minimal → 204

    [Fact]
    public void Updated_ReturnMinimal_Returns204NoContent()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        var metadata = RepresentationMetadata.WithStrongETag("etag1");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated", metadata);

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<NoContentResult>();
        controller.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
        controller.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    [Fact]
    public void Updated_ReturnMinimal_StillAppliesMetadataHeaders()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        var metadata = RepresentationMetadata.WithStrongETag("etag2");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated", metadata);

        outcome.ToActionResult<string, string>(controller);

        controller.Response.Headers.ETag.ToString().Should().Be("\"etag2\"");
    }

    #endregion

    #region Updated + return=representation → 200

    [Fact]
    public void Updated_ReturnRepresentation_Returns200WithBody()
    {
        var (controller, _) = CreateControllerWithPrefer("return=representation");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<OkObjectResult>();
        actionResult.As<OkObjectResult>().Value.Should().Be("updated");
        controller.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
        controller.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    #endregion

    #region Updated + return=representation + metadata Vary — no overwrite

    [Fact]
    public void Updated_ReturnRepresentation_WithMetadataVary_PreservesPreferInVary()
    {
        // Regression: ApplyMetadataHeaders was overwriting Vary, losing "Prefer"
        var (controller, _) = CreateControllerWithPrefer("return=representation");
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("etag4")
            .AddVary("Accept")
            .Build();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated", metadata);

        outcome.ToActionResult<string, string>(controller);

        var vary = controller.Response.Headers.Vary.ToString();
        vary.Should().Contain("Accept");
        vary.Should().Contain("Prefer");
        controller.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
    }

    #endregion

    #region Updated + no Prefer → 200 (default)

    [Fact]
    public void Updated_NoPrefer_Returns200WithBody_Default()
    {
        var (controller, _) = CreateControllerWithPrefer();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<OkObjectResult>();
        actionResult.As<OkObjectResult>().Value.Should().Be("updated");
        controller.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
        // RFC 7240 §2: Vary: Prefer MUST be included even when Prefer was not sent
        controller.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    #endregion

    #region Updated + return=minimal + map → 204 (map not invoked)

    [Fact]
    public void Updated_ReturnMinimal_WithMap_DoesNotInvokeMap()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("hello");
        var mapInvoked = false;

        outcome.ToActionResult(controller, (Func<string, string>)(s =>
        {
            mapInvoked = true;
            return s.ToUpperInvariant();
        }));

        mapInvoked.Should().BeFalse("map should not be invoked when returning 204");
    }

    #endregion

    #region Created — always 201 regardless of Prefer

    [Fact]
    public void Created_ReturnMinimal_StillReturns201WithBody()
    {
        // RFC 7240 §4.2: "return=minimal" typically uses 204, but 201 Created with Location is
        // mandatory per RFC 9110 §9.3.3 — the client needs the Location header.
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("item", "/api/items/1");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<CreatedResult>();
        actionResult.As<CreatedResult>().StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    #endregion

    #region UpdatedNoContent — always 204 regardless of Prefer

    [Fact]
    public void UpdatedNoContent_ReturnRepresentation_StillReturns204()
    {
        // If the server explicitly chose UpdatedNoContent, the client's return=representation
        // preference cannot be honored (there's no body to return).
        var (controller, _) = CreateControllerWithPrefer("return=representation");
        WriteOutcome<string> outcome = new WriteOutcome<string>.UpdatedNoContent();

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<NoContentResult>();
        controller.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
    }

    #endregion

    #region Accepted — unaffected by return preference

    [Fact]
    public void Accepted_WithBody_ReturnMinimal_StillReturns202WithBody()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Accepted(StatusBody: "processing");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<ObjectResult>();
        actionResult.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status202Accepted);
        actionResult.As<ObjectResult>().Value.Should().Be("processing");
    }

    #endregion

    #region Preference-Applied header

    [Fact]
    public void PreferenceApplied_NotEmitted_WhenNoPreferHeader()
    {
        var (controller, _) = CreateControllerWithPrefer();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated");

        outcome.ToActionResult<string, string>(controller);

        controller.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
    }

    [Fact]
    public void PreferenceApplied_NotEmitted_WhenPreferenceNotApplicable()
    {
        // respond-async without being honored doesn't emit Preference-Applied
        var (controller, _) = CreateControllerWithPrefer("respond-async");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated");

        outcome.ToActionResult<string, string>(controller);

        controller.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
    }

    #endregion

    #region Result<T>.ToUpdatedActionResult convenience extension

    [Fact]
    public void ToUpdatedActionResult_Success_NoPrefer_Returns200WithBody()
    {
        var (controller, _) = CreateControllerWithPrefer();
        var result = Result.Success("updated-value");
        var metadata = RepresentationMetadata.WithStrongETag("etag1");

        var actionResult = result.ToUpdatedActionResult(controller, metadata, (string s) => s.ToUpperInvariant());

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        actionResult.Result.As<OkObjectResult>().Value.Should().Be("UPDATED-VALUE");
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag1\"");
    }

    [Fact]
    public void ToUpdatedActionResult_Success_ReturnMinimal_Returns204()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        var result = Result.Success("updated-value");
        var metadata = RepresentationMetadata.WithStrongETag("etag2");

        var actionResult = result.ToUpdatedActionResult(controller, metadata, (string s) => s.ToUpperInvariant());

        actionResult.Result.Should().BeOfType<NoContentResult>();
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag2\"");
        controller.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
        controller.Response.Headers.Vary.ToString().Should().Contain("Prefer");
    }

    [Fact]
    public void ToUpdatedActionResult_Success_ReturnRepresentation_Returns200()
    {
        var (controller, _) = CreateControllerWithPrefer("return=representation");
        var result = Result.Success("updated-value");
        var metadata = RepresentationMetadata.WithStrongETag("etag3");

        var actionResult = result.ToUpdatedActionResult(controller, metadata, (string s) => s.ToUpperInvariant());

        actionResult.Result.Should().BeOfType<OkObjectResult>();
        actionResult.Result.As<OkObjectResult>().Value.Should().Be("UPDATED-VALUE");
        controller.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
    }

    [Fact]
    public void ToUpdatedActionResult_Failure_ReturnsError()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        var result = Result.Failure<string>(Error.NotFound("not found"));

        var actionResult = result.ToUpdatedActionResult(controller, (RepresentationMetadata?)null, (string s) => s);

        actionResult.Result.Should().BeOfType<ObjectResult>();
        actionResult.Result.As<ObjectResult>().StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToUpdatedActionResult_MetadataSelector_Success_ReturnMinimal_Returns204()
    {
        var (controller, _) = CreateControllerWithPrefer("return=minimal");
        var result = Result.Success("updated-value");

        var actionResult = result.ToUpdatedActionResult(
            controller,
            _ => RepresentationMetadata.WithStrongETag("dynamic-etag"),
            (string s) => s.ToUpperInvariant());

        actionResult.Result.Should().BeOfType<NoContentResult>();
        controller.Response.Headers.ETag.ToString().Should().Be("\"dynamic-etag\"");
        controller.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
    }

    #endregion
}
