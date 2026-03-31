namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

/// <summary>
/// Tests for <see cref="ETagHelper"/> — RFC 9110 entity tag parsing and comparison.
/// </summary>
public class ETagHelperTests
{
    #region ParseIfMatch

    [Fact]
    public void ParseIfMatch_NoHeader_ReturnsNull()
    {
        var request = new DefaultHttpContext().Request;

        ETagHelper.ParseIfMatch(request).Should().BeNull();
    }

    [Fact]
    public void ParseIfMatch_StrongETag_ReturnsUnquotedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "\"abc123\"";

        ETagHelper.ParseIfMatch(context.Request).Should().Equal("abc123");
    }

    [Fact]
    public void ParseIfMatch_Wildcard_ReturnsAsterisk()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "*";

        ETagHelper.ParseIfMatch(context.Request).Should().Equal("*");
    }

    [Fact]
    public void ParseIfMatch_WeakETag_ReturnsEmptyArray()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "W/\"abc123\"";

        ETagHelper.ParseIfMatch(context.Request).Should().BeEmpty("weak ETags are excluded per RFC 9110 §13.1.1");
    }

    [Fact]
    public void ParseIfMatch_MultipleETags_ReturnsAllStrong()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "W/\"weak\", \"strong1\", \"strong2\"";

        ETagHelper.ParseIfMatch(context.Request).Should().Equal("strong1", "strong2");
    }

    [Fact]
    public void ParseIfMatch_MultipleStrongETags_ReturnsAll()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "\"aaa\", \"bbb\", \"ccc\"";

        ETagHelper.ParseIfMatch(context.Request).Should().Equal("aaa", "bbb", "ccc");
    }

    #endregion

    #region IfNoneMatchMatches

    [Fact]
    public void IfNoneMatchMatches_EmptyHeader_ReturnsFalse()
    {
        var result = ETagHelper.IfNoneMatchMatches([], "abc");

        result.Should().BeFalse();
    }

    [Fact]
    public void IfNoneMatchMatches_MatchingStrongETag_ReturnsTrue()
    {
        var tags = new[] { new EntityTagHeaderValue("\"abc123\"") };

        var result = ETagHelper.IfNoneMatchMatches(tags, "abc123");

        result.Should().BeTrue();
    }

    [Fact]
    public void IfNoneMatchMatches_MatchingWeakETag_ReturnsTrue()
    {
        // RFC 9110 §13.1.2: If-None-Match uses weak comparison
        var tags = new[] { new EntityTagHeaderValue("\"abc123\"", isWeak: true) };

        var result = ETagHelper.IfNoneMatchMatches(tags, "abc123");

        result.Should().BeTrue("If-None-Match uses weak comparison per RFC 9110 §13.1.2");
    }

    [Fact]
    public void IfNoneMatchMatches_Wildcard_ReturnsTrue()
    {
        var tags = new[] { EntityTagHeaderValue.Any };

        var result = ETagHelper.IfNoneMatchMatches(tags, "any-etag");

        result.Should().BeTrue();
    }

    [Fact]
    public void IfNoneMatchMatches_NoMatch_ReturnsFalse()
    {
        var tags = new[] { new EntityTagHeaderValue("\"other\"") };

        var result = ETagHelper.IfNoneMatchMatches(tags, "abc123");

        result.Should().BeFalse();
    }

    #endregion

    #region IfMatchSatisfied

    [Fact]
    public void IfMatchSatisfied_EmptyHeader_ReturnsTrue()
    {
        var result = ETagHelper.IfMatchSatisfied([], "abc");

        result.Should().BeTrue("no If-Match header means unconditional request");
    }

    [Fact]
    public void IfMatchSatisfied_MatchingStrongETag_ReturnsTrue()
    {
        var tags = new[] { new EntityTagHeaderValue("\"abc123\"") };

        var result = ETagHelper.IfMatchSatisfied(tags, "abc123");

        result.Should().BeTrue();
    }

    [Fact]
    public void IfMatchSatisfied_WeakETag_ReturnsFalse()
    {
        // RFC 9110 §13.1.1: If-Match uses strong comparison — weak ETags never match
        var tags = new[] { new EntityTagHeaderValue("\"abc123\"", isWeak: true) };

        var result = ETagHelper.IfMatchSatisfied(tags, "abc123");

        result.Should().BeFalse("If-Match requires strong comparison per RFC 9110 §13.1.1");
    }

    [Fact]
    public void IfMatchSatisfied_Wildcard_ReturnsTrue()
    {
        var tags = new[] { EntityTagHeaderValue.Any };

        var result = ETagHelper.IfMatchSatisfied(tags, "any-etag");

        result.Should().BeTrue();
    }

    [Fact]
    public void IfMatchSatisfied_NoMatch_ReturnsFalse()
    {
        var tags = new[] { new EntityTagHeaderValue("\"other\"") };

        var result = ETagHelper.IfMatchSatisfied(tags, "abc123");

        result.Should().BeFalse();
    }

    [Fact]
    public void IfMatchSatisfied_EmptyCurrentETag_ReturnsFalse()
    {
        var tags = new[] { new EntityTagHeaderValue("\"abc\"") };

        var result = ETagHelper.IfMatchSatisfied(tags, "");

        result.Should().BeFalse("resource with no ETag cannot satisfy If-Match");
    }

    #endregion
}
