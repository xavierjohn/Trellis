namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Trellis;

/// <summary>
/// Tests for <see cref="WriteOutcome{T}"/> and <see cref="WriteOutcomeExtensions"/>.
/// </summary>
public class WriteOutcomeTests
{
    private static ControllerBase CreateControllerWithHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        var mock = new Mock<ControllerBase> { CallBase = true };
        mock.Object.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return mock.Object;
    }

    #region Created

    [Fact]
    public void Created_Returns201_WithLocation()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("item", "/api/items/1");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<CreatedResult>();
        var created = actionResult.As<CreatedResult>();
        created.StatusCode.Should().Be(StatusCodes.Status201Created);
        created.Location.Should().Be("/api/items/1");
        created.Value.Should().Be("item");
    }

    [Fact]
    public void Created_WithMap_TransformsValue()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("hello", "/api/items/1");

        var actionResult = outcome.ToActionResult(controller, (Func<string, string>)(s => s.ToUpperInvariant()));

        actionResult.As<CreatedResult>().Value.Should().Be("HELLO");
    }

    [Fact]
    public void Created_WithMetadata_AppliesHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var metadata = RepresentationMetadata.WithStrongETag("etag1");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Created("item", "/api/items/1", metadata);

        outcome.ToActionResult<string, string>(controller);

        controller.Response.Headers.ETag.ToString().Should().Be("\"etag1\"");
    }

    #endregion

    #region Replaced

    [Fact]
    public void Replaced_Returns200()
    {
        var controller = CreateControllerWithHttpContext();
        var metadata = RepresentationMetadata.WithStrongETag("etag2");
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated", metadata);

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<OkObjectResult>();
        actionResult.As<OkObjectResult>().Value.Should().Be("updated");
        controller.Response.Headers.ETag.ToString().Should().Be("\"etag2\"");
    }

    [Fact]
    public void Replaced_WithMap_TransformsValue()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("hello");

        var actionResult = outcome.ToActionResult(controller, (Func<string, string>)(s => s.ToUpperInvariant()));

        actionResult.As<OkObjectResult>().Value.Should().Be("HELLO");
    }

    #endregion

    #region ReplacedNoContent

    [Fact]
    public void ReplacedNoContent_Returns204()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.UpdatedNoContent();

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<NoContentResult>();
        actionResult.As<NoContentResult>().StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void ReplacedNoContent_WithMetadata_AppliesHeaders()
    {
        var controller = CreateControllerWithHttpContext();
        var metadata = RepresentationMetadata.WithStrongETag("etag3");
        WriteOutcome<string> outcome = new WriteOutcome<string>.UpdatedNoContent(metadata);

        outcome.ToActionResult<string, string>(controller);

        controller.Response.Headers.ETag.ToString().Should().Be("\"etag3\"");
    }

    #endregion

    #region Accepted

    [Fact]
    public void Accepted_WithStatusMonitor_Returns202_WithLocation()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.AcceptedNoContent(MonitorUri: "/api/status/123");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<StatusCodeResult>();
        actionResult.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status202Accepted);
        controller.Response.Headers.Location.ToString().Should().Be("/api/status/123");
    }

    [Fact]
    public void Accepted_WithRetryAfter_Returns202_WithRetryAfterHeader()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.AcceptedNoContent(RetryAfter: RetryAfterValue.FromSeconds(60));

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<StatusCodeResult>();
        actionResult.As<StatusCodeResult>().StatusCode.Should().Be(StatusCodes.Status202Accepted);
        controller.Response.Headers["Retry-After"].ToString().Should().Be("60");
    }

    [Fact]
    public void Accepted_WithStatusBody_Returns202_WithBody()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Accepted(StatusBody: "processing");

        var actionResult = outcome.ToActionResult<string, string>(controller);

        actionResult.Should().BeOfType<ObjectResult>();
        var objResult = actionResult.As<ObjectResult>();
        objResult.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        objResult.Value.Should().Be("processing");
    }

    [Fact]
    public void Accepted_WithStatusBodyAndMap_TransformsBody()
    {
        var controller = CreateControllerWithHttpContext();
        WriteOutcome<string> outcome = new WriteOutcome<string>.Accepted(StatusBody: "pending");

        var actionResult = outcome.ToActionResult(controller, (Func<string, string>)(s => s.ToUpperInvariant()));

        actionResult.As<ObjectResult>().Value.Should().Be("PENDING");
    }

    #endregion
}
