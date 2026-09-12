namespace Trellis.Asp.Tests;

using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

public sealed class DirectionalPaginationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task ToHttpResponse_Directional_LinksPresent_EmitsMatchingEnvelopeAndHeaders(int variant, bool configureHeaders)
    {
        var context = NewContext();
        var calls = new List<(string Token, PageDirection Direction, int Limit)>();
        var page = new Page<int>([1, 2], new Cursor("next-token"), new Cursor("previous-token"), 20, 10);
        string BuildUrl(Cursor cursor, PageDirection direction, int limit)
        {
            calls.Add((cursor.Token, direction, limit));
            var parameter = direction == PageDirection.Next ? "after" : "before";
            return $"https://example.test/items?{parameter}={cursor.Token}&limit={limit}";
        }

        var response = await ConvertAsync(variant, Result.Ok(page), BuildUrl, item => $"item-{item}",
            configureHeaders ? options => options.WithContentLanguage("en") : null);
        await response.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        calls.Should().Equal([("next-token", PageDirection.Next, 10), ("previous-token", PageDirection.Previous, 10)]);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var envelope = json.RootElement;
        var next = envelope.GetProperty("next");
        var previous = envelope.GetProperty("previous");
        next.GetProperty("cursor").GetString().Should().Be("next-token");
        previous.GetProperty("cursor").GetString().Should().Be("previous-token");
        next.GetProperty("href").GetString().Should().Be("https://example.test/items?after=next-token&limit=10");
        previous.GetProperty("href").GetString().Should().Be("https://example.test/items?before=previous-token&limit=10");
        context.Response.Headers.Link.ToString().Should().Be(
            $"<{next.GetProperty("href").GetString()}>; rel=\"next\", <{previous.GetProperty("href").GetString()}>; rel=\"prev\"");
        envelope.GetProperty("items").EnumerateArray().Select(item => item.GetString()).Should().Equal(["item-1", "item-2"]);
        envelope.GetProperty("requestedLimit").GetInt32().Should().Be(20);
        envelope.GetProperty("appliedLimit").GetInt32().Should().Be(10);
        envelope.GetProperty("deliveredCount").GetInt32().Should().Be(2);
        envelope.GetProperty("wasCapped").GetBoolean().Should().BeTrue();
        if (configureHeaders)
            context.Response.Headers.ContentLanguage.ToString().Should().Be("en");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ToHttpResponse_Directional_Failure_DoesNotInvokeCallbacks(int variant)
    {
        var context = NewContext();
        var failure = Result.Fail<Page<int>>(new Error.Conflict(null, "conflict"));

        var response = await ConvertAsync(variant, failure,
            (_, _, _) => throw new InvalidOperationException("URL builder must not run"),
            _ => throw new InvalidOperationException("Body mapper must not run"),
            options => options.WithErrorMapping<Error.Conflict>(418));
        await response.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(418);
        context.Response.Headers.Should().NotContainKey("Link");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ToHttpResponse_Directional_NoCursors_DoesNotInvokeUrlBuilder(int variant)
    {
        var context = NewContext();
        var response = await ConvertAsync(variant, Result.Ok(Page.Empty<int>(10, 10)),
            (_, _, _) => throw new InvalidOperationException("URL builder must not run"), item => $"item-{item}");
        await response.ExecuteAsync(context);

        context.Response.Headers.Should().NotContainKey("Link");
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        json.RootElement.GetProperty("next").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("previous").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ToHttpResponse_Directional_NotModified_DoesNotInvokeCallbacks(int variant)
    {
        var context = NewContext();
        context.Request.Method = "GET";
        context.Request.Headers.IfNoneMatch = "\"page\"";
        var page = new Page<int>([1], new Cursor("next"), new Cursor("previous"), 10, 10);

        var response = await ConvertAsync(variant, Result.Ok(page),
            (_, _, _) => throw new InvalidOperationException("URL builder must not run"),
            _ => throw new InvalidOperationException("Body mapper must not run"),
            options => options.WithETag(_ => "page").EvaluatePreconditions());
        await response.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        context.Response.Headers.Should().NotContainKey("Link");
        context.Response.Body.Length.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ToHttpResponse_Legacy_BothCursors_UsesSameCallback(int variant)
    {
        var context = NewContext();
        var calls = new List<(string Token, int Limit)>();
        var result = Result.Ok(new Page<int>([1], new Cursor("next"), new Cursor("previous"), 20, 10));
        string BuildUrl(Cursor cursor, int limit)
        {
            calls.Add((cursor.Token, limit));
            return $"https://example.test/items?cursor={cursor.Token}&limit={limit}";
        }

        var response = variant switch
        {
            0 => result.ToHttpResponse(BuildUrl, item => item),
            1 => await Task.FromResult(result).ToHttpResponseAsync(BuildUrl, item => item),
            _ => await ValueTask.FromResult(result).ToHttpResponseAsync(BuildUrl, item => item)
        };
        await response.ExecuteAsync(context);

        calls.Should().Equal([("next", 10), ("previous", 10)]);
        context.Response.Headers.Link.ToString().Should().Be(
            "<https://example.test/items?cursor=next&limit=10>; rel=\"next\", <https://example.test/items?cursor=previous&limit=10>; rel=\"prev\"");
    }

    [Fact]
    public void ToHttpResponse_Directional_NullCallbacks_ThrowsArgumentNullException()
    {
        var result = Result.Ok(Page.Empty<int>(10, 10));
        FluentActions.Invoking(() => result.ToHttpResponse<int, string>((Func<Cursor, PageDirection, int, string>)null!, item => $"item-{item}"))
            .Should().Throw<ArgumentNullException>().WithParameterName("urlBuilder");
        FluentActions.Invoking(() => result.ToHttpResponse<int, string>((_, _, _) => "/items", null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("body");
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> ConvertAsync(
        int variant,
        Result<Page<int>> result,
        Func<Cursor, PageDirection, int, string> urlBuilder,
        Func<int, string> body,
        Action<HttpResponseOptionsBuilder<Page<int>>>? configure = null) => variant switch
        {
            0 => result.ToHttpResponse(urlBuilder, body, configure),
            1 => await Task.FromResult(result).ToHttpResponseAsync(urlBuilder, body, configure),
            _ => await ValueTask.FromResult(result).ToHttpResponseAsync(urlBuilder, body, configure)
        };

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopProblemDetailsService>();
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
    }

    private sealed class NoopProblemDetailsService : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
    }
}
