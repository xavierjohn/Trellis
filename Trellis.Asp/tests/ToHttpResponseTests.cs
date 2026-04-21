namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

[Collection("TrellisAspOptionsState")]
public sealed class ToHttpResponseTests : IDisposable
{
    public ToHttpResponseTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private sealed record Todo(int Id, string Title, string ETag);

    private sealed record TodoResponse(int Id, string Title)
    {
        public static TodoResponse From(Todo t) => new(t.Id, t.Title);
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<Microsoft.AspNetCore.Http.IProblemDetailsService, DefaultProblemDetailsService>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class DefaultProblemDetailsService : Microsoft.AspNetCore.Http.IProblemDetailsService
    {
        public ValueTask WriteAsync(Microsoft.AspNetCore.Http.ProblemDetailsContext context) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(Microsoft.AspNetCore.Http.ProblemDetailsContext context) => false;
#pragma warning restore CA1822
    }

    [Fact]
    public async Task Result_OK_writes_200_with_body()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi", "abc"));

        var http = r.ToHttpResponse(TodoResponse.From);
        await http.ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"title\":\"hi\"").And.NotContain("ETag");
    }

    [Fact]
    public async Task Result_with_WithETag_emits_ETag_header()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi", "abc"));

        await r.ToHttpResponse(TodoResponse.From, o => o.WithETag(t => t.ETag)).ExecuteAsync(ctx);

        ctx.Response.Headers.ETag.ToString().Should().Be("\"abc\"");
    }

    [Fact]
    public async Task Result_failure_writes_problem_details()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task Result_NonGeneric_success_writes_204()
    {
        var ctx = NewContext();
        await Result.Ok().ToHttpResponse().ExecuteAsync(ctx);
        ctx.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task Result_Created_literal_writes_201_with_Location()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(7, "x", "etag"));

        await r.ToHttpResponse(TodoResponse.From, o => o.Created("/todos/7")).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/todos/7");
    }

    [Fact]
    public async Task MethodNotAllowed_emits_Allow_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.MethodNotAllowed(EquatableArray.Create("GET", "PUT")));

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(405);
        ctx.Response.Headers["Allow"].ToString().Should().Be("GET, PUT");
    }

    [Fact]
    public async Task TooManyRequests_with_RetryAfter_emits_RetryAfter_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.TooManyRequests(RetryAfterValue.FromSeconds(30)));

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(429);
        ctx.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public async Task Vary_appends_unique_values()
    {
        var ctx = NewContext();
        ctx.Response.Headers["Vary"] = "Accept";
        var r = Result.Ok(new Todo(1, "hi", "abc"));

        await r.ToHttpResponse(TodoResponse.From, o => o.Vary("Accept", "Accept-Language")).ExecuteAsync(ctx);

        var vary = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        vary.Should().Contain("Accept").And.Contain("Accept-Language");
    }

    [Fact]
    public async Task HonorPrefer_always_appends_Vary_Prefer()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi", "abc"));

        await r.ToHttpResponse(TodoResponse.From, o => o.HonorPrefer()).ExecuteAsync(ctx);

        string.Join(",", ctx.Response.Headers.Vary.ToArray()!).Should().Contain("Prefer");
    }

    [Fact]
    public async Task EvaluatePreconditions_returns_304_on_matching_INM_for_GET()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-None-Match"] = "\"abc\"";
        var r = Result.Ok(new Todo(1, "hi", "abc"));

        await r.ToHttpResponse(TodoResponse.From, o => o.WithETag(t => t.ETag).EvaluatePreconditions()).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(304);
    }

    [Fact]
    public async Task WriteOutcome_Created_writes_201_with_Location()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Todo>.Created(new Todo(1, "x", "e"), "/todos/1");
        var r = Result.Ok<WriteOutcome<Todo>>(outcome);

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/todos/1");
    }

    [Fact]
    public async Task WriteOutcome_Updated_with_Prefer_minimal_returns_204()
    {
        var ctx = NewContext();
        ctx.Request.Headers["Prefer"] = "return=minimal";
        var outcome = new WriteOutcome<Todo>.Updated(new Todo(1, "x", "e"));
        var r = Result.Ok<WriteOutcome<Todo>>(outcome);

        await r.ToHttpResponse(TodoResponse.From, o => o.HonorPrefer()).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
    }

    [Fact]
    public async Task WithErrorMapping_overrides_status_for_specific_error()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.Conflict(null, "dup"));

        await r.ToHttpResponse(TodoResponse.From, o => o.WithErrorMapping<Error.Conflict>(418)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(418);
    }

    [Fact]
    public async Task AsActionResult_forwards_inner_result_execution()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi", "abc"));
        var ar = r.ToHttpResponse(TodoResponse.From).AsActionResult<TodoResponse>();

        ar.Should().NotBeNull();
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            ctx,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        await ar.Result!.ExecuteResultAsync(actionContext);
        ctx.Response.StatusCode.Should().Be(200);
    }
}
