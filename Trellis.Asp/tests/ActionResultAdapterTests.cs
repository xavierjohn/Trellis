namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Coverage for the async overloads of <see cref="ActionResultAdapterExtensions.AsActionResultAsync{T}"/>
/// (Task and ValueTask), plus the null-argument guards.
/// </summary>
public sealed class ActionResultAdapterTests
{
    private sealed record Thing(int Id);

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
    public void AsActionResult_throws_on_null_inner_result()
        => FluentActions.Invoking(() =>
                ActionResultAdapterExtensions.AsActionResult<Thing>(null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public Task AsActionResultAsync_Task_throws_on_null_task()
        => FluentActions.Invoking(async () =>
                await ActionResultAdapterExtensions.AsActionResultAsync<Thing>((Task<Microsoft.AspNetCore.Http.IResult>)null!))
            .Should().ThrowAsync<ArgumentNullException>();

    [Fact]
    public async Task AsActionResultAsync_Task_overload_unwraps_and_forwards_execution()
    {
        var ctx = NewContext();
        Task<Microsoft.AspNetCore.Http.IResult> source =
            Task.FromResult(Result.Ok(new Thing(1)).ToHttpResponse());

        var ar = await source.AsActionResultAsync<Thing>();

        ar.Should().NotBeNull();
        var actionContext = new ActionContext(ctx, new RouteData(), new ActionDescriptor());
        await ar.Result!.ExecuteResultAsync(actionContext);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task AsActionResultAsync_ValueTask_overload_unwraps_and_forwards_execution()
    {
        var ctx = NewContext();
        ValueTask<Microsoft.AspNetCore.Http.IResult> source =
            ValueTask.FromResult(Result.Ok(new Thing(7)).ToHttpResponse());

        var ar = await source.AsActionResultAsync<Thing>();

        ar.Should().NotBeNull();
        var actionContext = new ActionContext(ctx, new RouteData(), new ActionDescriptor());
        await ar.Result!.ExecuteResultAsync(actionContext);
        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task TrellisActionResult_ExecuteResultAsync_throws_on_null_context()
    {
        var ar = Result.Ok(new Thing(1)).ToHttpResponse().AsActionResult<Thing>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => ar.Result!.ExecuteResultAsync(null!));
    }
}
