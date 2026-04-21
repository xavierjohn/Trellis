namespace Trellis.Asp.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;

/// <summary>
/// Unit tests for <see cref="PageActionResultExtensions"/>: envelope shape, Link header
/// emission, error passthrough, and Task/ValueTask overloads.
/// </summary>
public class PageActionResultExtensionsTests
{
    private static readonly int[] OneItem = [1];
    private static readonly int[] OneItemSeven = [7];
    private static readonly int[] OneItemNine = [9];
    private static readonly int[] OneTwoThree = [1, 2, 3];
    private static readonly int[] OneTwo = [1, 2];

    private sealed class TestController : ControllerBase { }

    private static TestController CreateController()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory,
            Microsoft.AspNetCore.Mvc.Infrastructure.DefaultProblemDetailsFactory>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Http.Json.JsonOptions()));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ApiBehaviorOptions()));
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        return new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static string Build(Cursor cursor, int appliedLimit) =>
        $"https://api.example.com/widgets?limit={appliedLimit}&cursor={cursor.Token}";

    private static string MapToString(int x) => $"item-{x}";

    [Fact]
    public void Failure_result_returns_error_action_result_and_emits_no_link_header()
    {
        var controller = CreateController();
        var result = Result.Fail<Page<int>>(new Error.NotFound(new ResourceRef("Widget", "x")));

        var response = result.ToPagedActionResult(controller, Build, MapToString);

        controller.Response.Headers.ContainsKey("Link").Should().BeFalse();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public void Empty_page_with_no_cursors_returns_ok_envelope_and_no_link_header()
    {
        var controller = CreateController();
        var page = Page.Empty<int>(requestedLimit: 10, appliedLimit: 5);

        var response = Result.Ok(page).ToPagedActionResult(controller, Build, MapToString);

        controller.Response.Headers.ContainsKey("Link").Should().BeFalse();
        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<PagedResponse<string>>();
    }

    [Fact]
    public void Page_with_next_cursor_emits_link_rel_next()
    {
        var controller = CreateController();
        var page = new Page<int>(OneTwoThree, new Cursor("nxt-token"), null, RequestedLimit: 10, AppliedLimit: 3);

        Result.Ok(page).ToPagedActionResult(controller, Build, MapToString);

        var link = controller.Response.Headers["Link"].ToString();
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain("cursor=nxt-token");
        link.Should().Contain("limit=3");
        link.Should().NotContain("rel=\"prev\"");
    }

    [Fact]
    public void Page_with_both_cursors_emits_comma_separated_link_header()
    {
        var controller = CreateController();
        var page = new Page<int>(OneItem, new Cursor("n"), new Cursor("p"), RequestedLimit: 5, AppliedLimit: 5);

        Result.Ok(page).ToPagedActionResult(controller, Build, MapToString);

        var link = controller.Response.Headers["Link"].ToString();
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain("rel=\"prev\"");
        link.Should().Contain(", ");
    }

    [Fact]
    public void Url_builder_receives_applied_limit_not_requested()
    {
        var controller = CreateController();
        int? observedLimit = null;
        string Builder(Cursor c, int applied)
        {
            observedLimit = applied;
            return $"u?{applied}";
        }

        var page = new Page<int>(OneItem, new Cursor("c"), null, RequestedLimit: 100, AppliedLimit: 5);

        Result.Ok(page).ToPagedActionResult(controller, Builder, MapToString);

        observedLimit.Should().Be(5);
    }

    [Fact]
    public void Items_are_projected_via_map_into_envelope()
    {
        var controller = CreateController();
        var page = new Page<int>(OneTwo, null, null, RequestedLimit: 10, AppliedLimit: 10);

        var response = Result.Ok(page).ToPagedActionResult(controller, Build, MapToString);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<PagedResponse<string>>().Subject;
        envelope.Items.Should().Equal("item-1", "item-2");
        envelope.RequestedLimit.Should().Be(10);
        envelope.AppliedLimit.Should().Be(10);
        envelope.DeliveredCount.Should().Be(2);
        envelope.WasCapped.Should().BeFalse();
    }

    [Fact]
    public async Task Task_overload_awaits_and_delegates_correctly()
    {
        var controller = CreateController();
        var page = new Page<int>(OneItemSeven, new Cursor("t"), null, 10, 1);
        Task<Result<Page<int>>> task = Task.FromResult(Result.Ok(page));

        await task.ToPagedActionResultAsync(controller, Build, MapToString);

        controller.Response.Headers["Link"].ToString().Should().Contain("cursor=t");
    }

    [Fact]
    public async Task ValueTask_overload_awaits_and_delegates_correctly()
    {
        var controller = CreateController();
        var page = new Page<int>(OneItemNine, new Cursor("v"), null, 10, 1);
        ValueTask<Result<Page<int>>> task = new ValueTask<Result<Page<int>>>(Result.Ok(page));

        await task.ToPagedActionResultAsync(controller, Build, MapToString);

        controller.Response.Headers["Link"].ToString().Should().Contain("cursor=v");
    }

    [Fact]
    public void Null_controller_throws_argument_null_exception()
    {
        var page = new Page<int>(OneItem, null, null, 10, 10);
        var act = () => Result.Ok(page).ToPagedActionResult<int, string>(null!, Build, MapToString);

        act.Should().Throw<System.ArgumentNullException>().WithParameterName("controller");
    }
}
