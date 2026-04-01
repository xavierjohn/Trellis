namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Tests for <see cref="RangeRequestEvaluator"/> — RFC 9110 §14 range request evaluation.
/// </summary>
public class RangeRequestEvaluatorTests
{
    #region No Range header

    [Fact]
    public void Evaluate_NoRangeHeader_ReturnsFull()
    {
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Get;

        var outcome = RangeRequestEvaluator.Evaluate(request, 200);

        outcome.Should().BeOfType<RangeOutcome.FullRepresentation>();
    }

    #endregion

    #region Non-GET requests

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void Evaluate_NonGetRequest_ReturnsFull(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Headers.Range = "bytes=0-99";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        outcome.Should().BeOfType<RangeOutcome.FullRepresentation>();
    }

    #endregion

    #region Valid single range

    [Fact]
    public void Evaluate_ValidSingleRange_ReturnsPartial()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "bytes=0-99";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        var partial = outcome.Should().BeOfType<RangeOutcome.PartialContent>().Subject;
        partial.From.Should().Be(0);
        partial.To.Should().Be(99);
        partial.CompleteLength.Should().Be(200);
    }

    #endregion

    #region Open-ended range

    [Fact]
    public void Evaluate_OpenEndedRange_ReturnsPartialToEnd()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "bytes=100-";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        var partial = outcome.Should().BeOfType<RangeOutcome.PartialContent>().Subject;
        partial.From.Should().Be(100);
        partial.To.Should().Be(199);
        partial.CompleteLength.Should().Be(200);
    }

    #endregion

    #region Suffix range

    [Fact]
    public void Evaluate_SuffixRange_ReturnsPartialLastNBytes()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "bytes=-50";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        var partial = outcome.Should().BeOfType<RangeOutcome.PartialContent>().Subject;
        partial.From.Should().Be(150);
        partial.To.Should().Be(199);
        partial.CompleteLength.Should().Be(200);
    }

    #endregion

    #region Unsatisfiable range

    [Fact]
    public void Evaluate_UnsatisfiableRange_ReturnsNotSatisfiable()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "bytes=300-400";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        var notSatisfiable = outcome.Should().BeOfType<RangeOutcome.NotSatisfiable>().Subject;
        notSatisfiable.CompleteLength.Should().Be(200);
    }

    #endregion

    #region Range past end (clamped)

    [Fact]
    public void Evaluate_RangePastEnd_ClampsToCompleteLength()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "bytes=0-999";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        var partial = outcome.Should().BeOfType<RangeOutcome.PartialContent>().Subject;
        partial.From.Should().Be(0);
        partial.To.Should().Be(199);
        partial.CompleteLength.Should().Be(200);
    }

    #endregion

    #region Non-bytes unit

    [Fact]
    public void Evaluate_NonBytesUnit_ReturnsFull()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "items=0-99";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 200);

        outcome.Should().BeOfType<RangeOutcome.FullRepresentation>();
    }

    #endregion

    #region Multi-range requests

    [Fact]
    public void Evaluate_MultipleRanges_ReturnsFull()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Range = "bytes=0-99, 200-299";

        var outcome = RangeRequestEvaluator.Evaluate(context.Request, 500);

        outcome.Should().BeOfType<RangeOutcome.FullRepresentation>();
    }

    #endregion
}
