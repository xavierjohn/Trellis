namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

public sealed class ToHttpResponseTests
{
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
    public async Task Per_call_error_mapper_returning_default_falls_through_to_ambient_status()
    {
        // The documented per-call mapping idiom uses `default` as the "not my error" arm:
        //     .WithErrorMapping(err => err is OutOfStockError ? 410 : default)
        // `default` is 0, which is not a legal HTTP status. It must be treated as "no match"
        // and fall through the precedence chain rather than being written to the response.
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoResponse.From, o => o.WithErrorMapping(_ => default)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Per_call_error_mapper_returning_status_is_honored()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoResponse.From, o => o.WithErrorMapping(_ => StatusCodes.Status410Gone))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(410);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(600)]
    [InlineData(-1)]
    public async Task Per_call_error_mapper_returning_out_of_range_status_falls_through(int mapped)
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoResponse.From, o => o.WithErrorMapping(_ => mapped)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Per_call_error_mapper_returning_default_falls_through_to_typed_override()
    {
        // A non-matching mapper arm must not shadow a WithErrorMapping<TError>(status) override.
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(
                TodoResponse.From,
                o => o.WithErrorMapping(_ => default).WithErrorMapping<Error.NotFound>(StatusCodes.Status410Gone))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(410);
    }

    [Fact]
    public async Task Result_failure_422_UnprocessableContent_uses_problem_plus_json_and_populates_instance()
    {
        // RFC 9457 §3 mandates application/problem+json for problem responses. The 422 path
        // must align with the rest of the framework's error surface (404, 403, 409, etc.),
        // which all emit problem+json. The instance assertion here specifically pins the
        // ResponseFailureWriter ValidationProblem branch (Error.InvalidInput with
        // field violations), distinct from the Results.Problem(...) branch exercised by the
        // NotFound test below.
        var ctx = NewContext();
        ctx.Request.Path = "/api/customers";
        ctx.Request.QueryString = new QueryString("?api-version=2026-11-12");
        var r = Result.Fail<Todo>(
            Error.InvalidInput.ForField("title", "required", "Title is required."));

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(422);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"instance\":\"/api/customers?api-version=2026-11-12\"");
    }

    [Fact]
    public async Task Result_failure_synthesizes_instance_from_resource_ref_when_url_does_not_identify_resource()
    {
        // RFC 9457 §3.1: "instance" identifies the specific occurrence of the problem.
        // When the failing Result carries a ResourceRef whose id is NOT in the request URL,
        // Trellis synthesises "instance" from the ResourceRef ("/todos/1") and preserves
        // the original request URI under Extensions["request"].
        var ctx = NewContext();
        ctx.Request.Path = "/api/orders/123";
        ctx.Request.QueryString = new QueryString("?api-version=2026-11-12");

        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"instance\":\"/todos/1\"");
        body.Should().Contain("\"request\":\"/api/orders/123?api-version=2026-11-12\"");
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
        var r = Result.Fail<Todo>(new Error.TransportFault(new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "PUT"))));

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(405);
        ctx.Response.Headers["Allow"].ToString().Should().Be("GET, PUT");
    }

    [Fact]
    public async Task TooManyRequests_without_retry_after_does_not_emit_RetryAfter_header()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.RateLimited());

        await r.ToHttpResponse(TodoResponse.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(429);
        ctx.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
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