namespace Trellis.Asp.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;

/// <summary>
/// Unit tests for <see cref="PageHttpResultExtensions"/>: envelope shape, Link header
/// emission, error passthrough, and Task/ValueTask overloads.
/// </summary>
public class PageHttpResultExtensionsTests
{
    private static readonly int[] OneItem = [1];
    private static readonly int[] OneItemSeven = [7];
    private static readonly int[] OneItemNine = [9];
    private static readonly int[] OneTwoThree = [1, 2, 3];
    private static readonly int[] OneTwo = [1, 2];

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider(),
        };
        context.Response.Body = new System.IO.MemoryStream();
        return context;
    }

    private static string Build(Cursor cursor, int appliedLimit) =>
        $"https://api.example.com/widgets?limit={appliedLimit}&cursor={cursor.Token}";

    private static string MapToString(int x) => $"item-{x}";

    [Fact]
    public async Task Failure_result_returns_error_response_and_emits_no_link_header()
    {
        var httpContext = CreateHttpContext();
        var result = Result.Fail<Page<int>>(new Error.NotFound(new ResourceRef("Widget", "x")));

        var response = result.ToPagedHttpResult(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        httpContext.Response.Headers.ContainsKey("Link").Should().BeFalse();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Empty_page_with_no_cursors_returns_ok_envelope_and_no_link_header()
    {
        var httpContext = CreateHttpContext();
        var page = Page.Empty<int>(requestedLimit: 10, appliedLimit: 5);
        var result = Result.Ok(page);

        var response = result.ToPagedHttpResult(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        httpContext.Response.Headers.ContainsKey("Link").Should().BeFalse();
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Should().BeOfType<Ok<PagedResponse<string>>>();
    }

    [Fact]
    public async Task Page_with_next_cursor_emits_link_rel_next()
    {
        var httpContext = CreateHttpContext();
        var page = new Page<int>(
            Items: OneTwoThree,
            Next: new Cursor("nxt-token"),
            Previous: null,
            RequestedLimit: 10,
            AppliedLimit: 3);
        var result = Result.Ok(page);

        var response = result.ToPagedHttpResult(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        var link = httpContext.Response.Headers["Link"].ToString();
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain("cursor=nxt-token");
        link.Should().Contain("limit=3");
        link.Should().NotContain("rel=\"prev\"");
    }

    [Fact]
    public async Task Page_with_both_cursors_emits_comma_separated_link_header()
    {
        var httpContext = CreateHttpContext();
        var page = new Page<int>(
            Items: OneItem,
            Next: new Cursor("n"),
            Previous: new Cursor("p"),
            RequestedLimit: 5,
            AppliedLimit: 5);

        var response = Result.Ok(page).ToPagedHttpResult(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        var link = httpContext.Response.Headers["Link"].ToString();
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain("rel=\"prev\"");
        link.Should().Contain(", "); // RFC 8288 comma-joined values
    }

    [Fact]
    public async Task Url_builder_receives_applied_limit_not_requested()
    {
        var httpContext = CreateHttpContext();
        int? observedLimit = null;
        string Builder(Cursor c, int applied)
        {
            observedLimit = applied;
            return $"u?{applied}";
        }

        var page = new Page<int>(OneItem, new Cursor("c"), null, RequestedLimit: 100, AppliedLimit: 5);

        await Result.Ok(page).ToPagedHttpResult(Builder, MapToString).ExecuteAsync(httpContext);

        observedLimit.Should().Be(5);
    }

    [Fact]
    public async Task Items_are_projected_via_map_into_envelope()
    {
        var httpContext = CreateHttpContext();
        var page = new Page<int>(OneTwo, null, null, RequestedLimit: 10, AppliedLimit: 10);

        var response = Result.Ok(page).ToPagedHttpResult(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        var typed = response.Should().BeOfType<Ok<PagedResponse<string>>>().Subject;
        typed.Value!.Items.Should().Equal("item-1", "item-2");
        typed.Value.RequestedLimit.Should().Be(10);
        typed.Value.AppliedLimit.Should().Be(10);
        typed.Value.DeliveredCount.Should().Be(2);
        typed.Value.WasCapped.Should().BeFalse();
        typed.Value.Next.Should().BeNull();
        typed.Value.Previous.Should().BeNull();
    }

    [Fact]
    public async Task Task_overload_awaits_and_delegates_correctly()
    {
        var httpContext = CreateHttpContext();
        var page = new Page<int>(OneItemSeven, new Cursor("t"), null, 10, 1);
        Task<Result<Page<int>>> task = Task.FromResult(Result.Ok(page));

        var response = await task.ToPagedHttpResultAsync(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        httpContext.Response.Headers["Link"].ToString().Should().Contain("cursor=t");
    }

    [Fact]
    public async Task ValueTask_overload_awaits_and_delegates_correctly()
    {
        var httpContext = CreateHttpContext();
        var page = new Page<int>(OneItemNine, new Cursor("v"), null, 10, 1);
        ValueTask<Result<Page<int>>> task = new ValueTask<Result<Page<int>>>(Result.Ok(page));

        var response = await task.ToPagedHttpResultAsync(Build, MapToString);
        await response.ExecuteAsync(httpContext);

        httpContext.Response.Headers["Link"].ToString().Should().Contain("cursor=v");
    }

    [Fact]
    public void Null_url_builder_throws_argument_null_exception()
    {
        var page = new Page<int>(OneItem, null, null, 10, 10);
        var act = () => Result.Ok(page).ToPagedHttpResult<int, string>(null!, MapToString);

        act.Should().Throw<System.ArgumentNullException>().WithParameterName("nextUrlBuilder");
    }

    [Fact]
    public void Null_map_throws_argument_null_exception()
    {
        var page = new Page<int>(OneItem, null, null, 10, 10);
        var act = () => Result.Ok(page).ToPagedHttpResult<int, string>(Build, null!);

        act.Should().Throw<System.ArgumentNullException>().WithParameterName("map");
    }
}
