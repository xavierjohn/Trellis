namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

/// <summary>
/// Tests for <see cref="ConditionalRequestEvaluator"/> — RFC 9110 §13.2.2 conditional request evaluation.
/// </summary>
public class ConditionalRequestEvaluatorTests
{
    #region No conditional headers

    [Fact]
    public void Evaluate_NoConditionalHeaders_ReturnsProcess()
    {
        var request = new DefaultHttpContext().Request;
        request.Method = HttpMethods.Get;
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    #endregion

    #region If-Match (Step 1)

    [Fact]
    public void Evaluate_IfMatch_StrongETagMatch_ReturnsProceed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.IfMatch = "\"abc123\"";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    [Fact]
    public void Evaluate_IfMatch_StrongETagMismatch_ReturnsPreconditionFailed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.IfMatch = "\"abc123\"";
        var metadata = RepresentationMetadata.WithStrongETag("different");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    [Fact]
    public void Evaluate_IfMatch_Wildcard_ReturnsProceed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.IfMatch = "*";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    [Fact]
    public void Evaluate_IfMatch_WeakETagInHeader_ReturnsPreconditionFailed()
    {
        // Strong comparison excludes weak tags per RFC 9110 §13.1.1
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.IfMatch = "W/\"abc123\"";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    [Fact]
    public void Evaluate_IfMatch_NoResourceETag_ReturnsPreconditionFailed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.IfMatch = "\"abc123\"";
        var metadata = RepresentationMetadata.Create().Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    [Fact]
    public void Evaluate_IfMatch_WeakResourceETag_ReturnsPreconditionFailed()
    {
        // Resource has a weak ETag — strong comparison requires strong ETag
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.IfMatch = "\"abc123\"";
        var metadata = RepresentationMetadata.Create()
            .SetWeakETag("abc123")
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    #endregion

    #region If-Unmodified-Since (Step 2)

    [Fact]
    public void Evaluate_IfUnmodifiedSince_LastModifiedBefore_ReturnsProceed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfUnmodifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetLastModified(threshold.AddHours(-1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    [Fact]
    public void Evaluate_IfUnmodifiedSince_LastModifiedAfter_ReturnsPreconditionFailed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfUnmodifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetLastModified(threshold.AddHours(1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    [Fact]
    public void Evaluate_IfUnmodifiedSince_IgnoredWhenIfMatchPresent()
    {
        // If-Match takes precedence; If-Unmodified-Since is skipped
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Headers.IfMatch = "\"abc123\"";
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfUnmodifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("abc123")
            .SetLastModified(threshold.AddHours(1)) // Would fail If-Unmodified-Since
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied, "If-Match matched, so If-Unmodified-Since is skipped");
    }

    #endregion

    #region If-None-Match (Step 3) — GET/HEAD

    [Fact]
    public void Evaluate_IfNoneMatch_GetMatch_ReturnsNotModified()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers["If-None-Match"] = "\"abc123\"";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.NotModified);
    }

    [Fact]
    public void Evaluate_IfNoneMatch_HeadMatch_ReturnsNotModified()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Head;
        context.Request.Headers["If-None-Match"] = "\"abc123\"";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.NotModified);
    }

    [Fact]
    public void Evaluate_IfNoneMatch_GetNoMatch_ReturnsProceed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers["If-None-Match"] = "\"other\"";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    [Fact]
    public void Evaluate_IfNoneMatch_GetWildcard_ReturnsNotModified()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers["If-None-Match"] = "*";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.NotModified);
    }

    #endregion

    #region If-None-Match (Step 3) — unsafe methods

    [Fact]
    public void Evaluate_IfNoneMatch_PutMatch_ReturnsPreconditionFailed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Headers["If-None-Match"] = "\"abc123\"";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    [Fact]
    public void Evaluate_IfNoneMatch_PutWildcard_ReturnsPreconditionFailed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Headers["If-None-Match"] = "*";
        var metadata = RepresentationMetadata.WithStrongETag("abc123");

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionFailed);
    }

    #endregion

    #region If-Modified-Since (Step 4)

    [Fact]
    public void Evaluate_IfModifiedSince_NotModified_ReturnsNotModified()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfModifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetLastModified(threshold.AddHours(-1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.NotModified);
    }

    [Fact]
    public void Evaluate_IfModifiedSince_Modified_ReturnsProceed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfModifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetLastModified(threshold.AddHours(1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    [Fact]
    public void Evaluate_IfModifiedSince_IgnoredWhenIfNoneMatchPresent()
    {
        // If-None-Match takes precedence; If-Modified-Since is skipped
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers["If-None-Match"] = "\"other\"";
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfModifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("abc123")
            .SetLastModified(threshold.AddHours(-1)) // Would match If-Modified-Since (→ 304)
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied, "If-None-Match didn't match, so result is Proceed regardless of If-Modified-Since");
    }

    [Fact]
    public void Evaluate_IfModifiedSince_IgnoredForPut()
    {
        // If-Modified-Since only applies to GET/HEAD
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfModifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetLastModified(threshold.AddHours(-1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied, "If-Modified-Since is ignored for unsafe methods");
    }

    #endregion

    #region Precedence

    [Fact]
    public void Evaluate_IfMatch_TakesPrecedence_OverIfUnmodifiedSince()
    {
        // If-Match succeeds but If-Unmodified-Since would fail — should Proceed
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Headers.IfMatch = "\"abc123\"";
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfUnmodifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("abc123")
            .SetLastModified(threshold.AddHours(1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied);
    }

    [Fact]
    public void Evaluate_IfNoneMatch_TakesPrecedence_OverIfModifiedSince()
    {
        // If-None-Match has no match but If-Modified-Since would return 304
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers["If-None-Match"] = "\"other\"";
        var threshold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfModifiedSince = threshold;
        var metadata = RepresentationMetadata.Create()
            .SetStrongETag("abc123")
            .SetLastModified(threshold.AddHours(-1))
            .Build();

        var decision = ConditionalRequestEvaluator.Evaluate(context.Request, metadata);

        decision.Should().Be(ConditionalDecision.PreconditionsSatisfied, "If-None-Match no-match overrides If-Modified-Since");
    }

    #endregion
}
