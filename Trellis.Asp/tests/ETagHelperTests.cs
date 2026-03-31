namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Trellis;

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
    public void ParseIfMatch_StrongETag_ReturnsEntityTagValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "\"abc123\"";

        var result = ETagHelper.ParseIfMatch(context.Request);
        result.Should().ContainSingle()
            .Which.Should().Be(EntityTagValue.Strong("abc123"));
    }

    [Fact]
    public void ParseIfMatch_Wildcard_ReturnsWildcardEntityTagValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "*";

        var result = ETagHelper.ParseIfMatch(context.Request);
        result.Should().ContainSingle()
            .Which.Should().Be(EntityTagValue.Strong("*"));
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

        var result = ETagHelper.ParseIfMatch(context.Request);
        result.Should().Equal(EntityTagValue.Strong("strong1"), EntityTagValue.Strong("strong2"));
    }

    [Fact]
    public void ParseIfMatch_MultipleStrongETags_ReturnsAll()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "\"aaa\", \"bbb\", \"ccc\"";

        var result = ETagHelper.ParseIfMatch(context.Request);
        result.Should().Equal(EntityTagValue.Strong("aaa"), EntityTagValue.Strong("bbb"), EntityTagValue.Strong("ccc"));
    }

    [Fact]
    public void ParseIfMatch_MalformedHeader_ReturnsEmptyArray()
    {
        // Malformed If-Match (unquoted) must not be treated as "no header"
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "abc";

        ETagHelper.ParseIfMatch(context.Request).Should().BeEmpty(
            "malformed If-Match must return empty array (→ 412), not null (→ unconditional)");
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

    #region ParseIfNoneMatch

    [Fact]
    public void ParseIfNoneMatch_NoHeader_ReturnsNull()
    {
        var request = new DefaultHttpContext().Request;

        ETagHelper.ParseIfNoneMatch(request).Should().BeNull();
    }

    [Fact]
    public void ParseIfNoneMatch_StrongETag_ReturnsUnquotedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["If-None-Match"] = "\"abc123\"";

        ETagHelper.ParseIfNoneMatch(context.Request).Should().Equal("abc123");
    }

    [Fact]
    public void ParseIfNoneMatch_WeakETag_ReturnsUnquotedValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["If-None-Match"] = "W/\"abc123\"";

        ETagHelper.ParseIfNoneMatch(context.Request).Should().Equal("abc123");
    }

    [Fact]
    public void ParseIfNoneMatch_Wildcard_ReturnsAsterisk()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["If-None-Match"] = "*";

        ETagHelper.ParseIfNoneMatch(context.Request).Should().Equal("*");
    }

    [Fact]
    public void ParseIfNoneMatch_MultipleETags_ReturnsAll()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["If-None-Match"] = "W/\"weak\", \"strong1\", \"strong2\"";

        ETagHelper.ParseIfNoneMatch(context.Request).Should().Equal("weak", "strong1", "strong2");
    }

    [Fact]
    public void ParseIfNoneMatch_MalformedHeader_ReturnsEmptyArray()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["If-None-Match"] = "abc";

        ETagHelper.ParseIfNoneMatch(context.Request).Should().BeEmpty();
    }

    #endregion

    #region ParseIfModifiedSince

    [Fact]
    public void ParseIfModifiedSince_NoHeader_ReturnsNull()
    {
        var request = new DefaultHttpContext().Request;

        ETagHelper.ParseIfModifiedSince(request).Should().BeNull();
    }

    [Fact]
    public void ParseIfModifiedSince_ValidDate_ReturnsDateTimeOffset()
    {
        var context = new DefaultHttpContext();
        var date = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfModifiedSince = date;

        ETagHelper.ParseIfModifiedSince(context.Request).Should().Be(date);
    }

    #endregion

    #region ParseIfUnmodifiedSince

    [Fact]
    public void ParseIfUnmodifiedSince_NoHeader_ReturnsNull()
    {
        var request = new DefaultHttpContext().Request;

        ETagHelper.ParseIfUnmodifiedSince(request).Should().BeNull();
    }

    [Fact]
    public void ParseIfUnmodifiedSince_ValidDate_ReturnsDateTimeOffset()
    {
        var context = new DefaultHttpContext();
        var date = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        context.Request.GetTypedHeaders().IfUnmodifiedSince = date;

        ETagHelper.ParseIfUnmodifiedSince(context.Request).Should().Be(date);
    }

    #endregion
}
