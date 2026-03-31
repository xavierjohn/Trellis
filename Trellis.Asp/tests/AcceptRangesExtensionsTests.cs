namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Tests for <see cref="AcceptRangesExtensions"/> — RFC 9110 §14.3 Accept-Ranges header helpers.
/// </summary>
public class AcceptRangesExtensionsTests
{
    [Fact]
    public void AddAcceptRangesBytes_SetsHeaderToBytes()
    {
        var context = new DefaultHttpContext();

        context.Response.AddAcceptRangesBytes();

        context.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
    }

    [Fact]
    public void AddAcceptRangesNone_SetsHeaderToNone()
    {
        var context = new DefaultHttpContext();

        context.Response.AddAcceptRangesNone();

        context.Response.Headers["Accept-Ranges"].ToString().Should().Be("none");
    }
}
