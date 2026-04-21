namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Coverage for <see cref="HttpResponseExtensions"/> overloads not exercised by
/// <c>ToHttpResponseTests</c>: Error-only overload, async (Task / ValueTask) overloads, the
/// Vary path on <c>TrellisEmptyResult</c>, the standalone Error path, and Page&lt;T&gt; success +
/// failure paths.
/// </summary>
[Collection("TrellisAspOptionsState")]
public sealed class HttpResponseExtensionsExtraTests : IDisposable
{
    public HttpResponseExtensionsExtraTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private sealed record Thing(int Id, string Name);

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    [Fact]
    public async Task Error_ToHttpResponse_writes_problem_details()
    {
        var ctx = NewContext();
        var err = new Error.NotFound(new ResourceRef("X", "1"));

        await err.ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public void Error_ToHttpResponse_throws_on_null_error()
        => FluentActions.Invoking(() => HttpResponseExtensions.ToHttpResponse((Error)null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public async Task Error_ToHttpResponse_honours_per_call_error_mapping()
    {
        var ctx = NewContext();
        var err = new Error.Conflict(null, "x");

        await err.ToHttpResponse(o => o.WithErrorMapping(_ => 451)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(451);
    }

    [Fact]
    public async Task Error_ToHttpResponse_honours_typed_override_chain()
    {
        var ctx = NewContext();
        var err = new Error.Conflict(null, "x");

        await err.ToHttpResponse(o => o.WithErrorMapping<Error.Conflict>(418)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(418);
    }

    [Fact]
    public async Task Result_NonGeneric_HonorPrefer_emits_Vary_Prefer_on_204_path()
    {
        var ctx = NewContext();

        await Result.Ok().ToHttpResponse(o => o.HonorPrefer().Vary("Accept")).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        var vary = string.Join(",", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Prefer").And.Contain("Accept");
    }

    [Fact]
    public async Task Async_Task_overload_unwraps_and_executes()
    {
        var ctx = NewContext();
        var task = Task.FromResult(Result.Ok(new Thing(1, "x")));

        var http = await task.ToHttpResponseAsync<Thing>();
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Async_ValueTask_overload_unwraps_and_executes()
    {
        var ctx = NewContext();
        var vt = ValueTask.FromResult(Result.Ok(new Thing(1, "x")));

        var http = await vt.ToHttpResponseAsync<Thing>();
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Async_Task_with_body_projection_overload()
    {
        var ctx = NewContext();
        var task = Task.FromResult(Result.Ok(new Thing(1, "x")));

        var http = await task.ToHttpResponseAsync(t => new { t.Name });
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Async_ValueTask_with_body_projection_overload()
    {
        var ctx = NewContext();
        var vt = ValueTask.FromResult(Result.Ok(new Thing(1, "x")));

        var http = await vt.ToHttpResponseAsync(t => new { t.Name });
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task NonGeneric_Result_async_Task_overload()
    {
        var ctx = NewContext();
        var http = await Task.FromResult(Result.Ok()).ToHttpResponseAsync();
        await http.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task NonGeneric_Result_async_ValueTask_overload()
    {
        var ctx = NewContext();
        var http = await ValueTask.FromResult(Result.Ok()).ToHttpResponseAsync();
        await http.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task WriteOutcome_async_Task_overload()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Thing>.UpdatedNoContent();
        var http = await Task.FromResult(Result.Ok<WriteOutcome<Thing>>(outcome)).ToHttpResponseAsync();
        await http.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task WriteOutcome_async_ValueTask_overload()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Thing>.UpdatedNoContent();
        var http = await ValueTask.FromResult(Result.Ok<WriteOutcome<Thing>>(outcome))
            .ToHttpResponseAsync();
        await http.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task WriteOutcome_async_Task_with_body_projection_overload()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Thing>.Created(new Thing(7, "n"), "/things/7");
        var http = await Task.FromResult(Result.Ok<WriteOutcome<Thing>>(outcome))
            .ToHttpResponseAsync(t => new { t.Id });
        await http.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task WriteOutcome_async_ValueTask_with_body_projection_overload()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Thing>.Created(new Thing(7, "n"), "/things/7");
        var http = await ValueTask.FromResult(Result.Ok<WriteOutcome<Thing>>(outcome))
            .ToHttpResponseAsync(t => new { t.Id });
        await http.ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(201);
    }

    [Fact]
    public void Page_overload_throws_on_null_args()
    {
        var page = new Page<Thing>(new[] { new Thing(1, "x") }, null, null, 10, 10);
        var r = Result.Ok(page);
        FluentActions.Invoking(() => r.ToHttpResponse<Thing, object>(null!, _ => new()))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => r.ToHttpResponse<Thing, object>((_, _) => "next", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Page_failure_writes_problem_details_via_per_call_mapping()
    {
        var ctx = NewContext();
        var r = Result.Fail<Page<Thing>>(new Error.Conflict(null, "x"));

        var http = r.ToHttpResponse<Thing, object>((_, _) => "next", t => t,
            o => o.WithErrorMapping<Error.Conflict>(418));
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(418);
    }

    [Fact]
    public async Task Page_success_emits_envelope()
    {
        var ctx = NewContext();
        var page = new Page<Thing>(new[] { new Thing(1, "x") }, null, null, 10, 10);
        var r = Result.Ok(page);

        var http = r.ToHttpResponse<Thing, object>((_, _) => "next", t => new { t.Id });
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Page_async_Task_overload()
    {
        var ctx = NewContext();
        var page = new Page<Thing>(new[] { new Thing(1, "x") }, null, null, 10, 10);
        var r = Task.FromResult(Result.Ok(page));

        var http = await r.ToHttpResponseAsync<Thing, object>((_, _) => "n", t => new { t.Id });
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Page_async_ValueTask_overload()
    {
        var ctx = NewContext();
        var page = new Page<Thing>(new[] { new Thing(1, "x") }, null, null, 10, 10);
        var r = ValueTask.FromResult(Result.Ok(page));

        var http = await r.ToHttpResponseAsync<Thing, object>((_, _) => "n", t => new { t.Id });
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }
}
