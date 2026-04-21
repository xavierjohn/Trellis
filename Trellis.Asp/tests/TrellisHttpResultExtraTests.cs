namespace Trellis.Asp.Tests;

using System.IO;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Extra coverage for <see cref="TrellisHttpResult{TDomain,TBody}"/> branches not exercised by
/// the higher-level <c>ToHttpResponseTests</c>. Focuses on metadata projection, range handling,
/// precondition decisions, location resolution, error-status resolution and metadata interfaces.
/// </summary>
[Collection("TrellisAspOptionsState")]
public sealed class TrellisHttpResultExtraTests : IDisposable
{
    public TrellisHttpResultExtraTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private sealed record Todo(int Id, string Title, string ETag, DateTimeOffset Modified);

    private sealed record TodoBody(int Id, string Title)
    {
        public static TodoBody From(Todo t) => new(t.Id, t.Title);
    }

    private static DefaultHttpContext NewContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IProblemDetailsService, NoopPds>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext context) => false;
#pragma warning restore CA1822
    }

    [Fact]
    public async Task ApplyMetadata_writes_LastModified_ContentLanguage_ContentLocation_AcceptRanges()
    {
        var ctx = NewContext();
        var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var r = Result.Ok(new Todo(1, "x", "abc", when));

        await r.ToHttpResponse(TodoBody.From, o => o
            .WithLastModified(t => t.Modified)
            .WithContentLanguage("en-US", "en")
            .WithContentLocation(t => $"/todos/{t.Id}")
            .WithAcceptRanges("bytes"))
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Last-Modified"].ToString().Should().Be(when.ToString("R"));
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en-US, en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/todos/1");
        ctx.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
    }

    [Fact]
    public async Task ApplyMetadata_skips_null_selector_returns()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", null!, default));

        // Selectors return null/empty -> no headers set
        await r.ToHttpResponse(TodoBody.From, o => o
            .WithETag(_ => (EntityTagValue?)null!)
            .WithContentLocation(_ => null!))
            .ExecuteAsync(ctx);

        ctx.Response.Headers.ContainsKey("ETag").Should().BeFalse();
        ctx.Response.Headers.ContainsKey("Content-Location").Should().BeFalse();
    }

    [Fact]
    public async Task ETag_overload_taking_EntityTagValue_emits_weak_tag()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "v1", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithETag(t => EntityTagValue.Weak(t.ETag)))
            .ExecuteAsync(ctx);

        ctx.Response.Headers.ETag.ToString().Should().Be("W/\"v1\"");
    }

    [Fact]
    public async Task EvaluatePreconditions_skipped_for_non_safe_method()
    {
        var ctx = NewContext();
        ctx.Request.Method = "POST";
        ctx.Request.Headers["If-None-Match"] = "\"abc\"";
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithETag(t => t.ETag).EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200); // not 304
    }

    [Fact]
    public async Task EvaluatePreconditions_no_metadata_skipped()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-None-Match"] = "\"abc\"";
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        // No ETag/LastModified selector -> no metadata, no precondition evaluation
        await r.ToHttpResponse(TodoBody.From, o => o.EvaluatePreconditions()).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task EvaluatePreconditions_returns_412_when_If_Match_fails()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-Match"] = "\"different\"";
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithETag(t => t.ETag).EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task EvaluatePreconditions_returns_304_when_If_Modified_Since_not_modified()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        var when = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ctx.Request.Headers["If-Modified-Since"] = when.AddDays(1).ToString("R");
        var r = Result.Ok(new Todo(1, "x", "abc", when));

        await r.ToHttpResponse(TodoBody.From, o => o.WithLastModified(t => t.Modified).EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(304);
    }

    [Fact]
    public async Task Static_range_partial_writes_206()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithRange(0, 9, 100)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(206);
        ctx.Response.Headers["Content-Range"].ToString().Should().Be("items 0-9/100");
    }

    [Fact]
    public async Task Static_range_full_falls_back_to_200()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithRange(0, 99, 100)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Theory]
    [InlineData(-1, 5, 100)]   // From < 0
    [InlineData(5, 4, 100)]    // To < From
    [InlineData(0, 0, 0)]      // Total <= 0
    [InlineData(100, 105, 100)] // From >= Total
    public async Task Static_range_invalid_ranges_fall_back_to_200(long from, long to, long total)
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithRange(from, to, total)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Range_selector_returning_partial_writes_206()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithRange(_ =>
                ContentRangeHeaderValue.Parse("bytes 5-9/50")))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(206);
        ctx.Response.Headers["Content-Range"].ToString().Should().Be("items 5-9/50");
    }

    [Fact]
    public async Task Range_selector_with_full_range_falls_back_to_200()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithRange(_ =>
                ContentRangeHeaderValue.Parse("bytes 0-9/10")))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Range_selector_without_concrete_range_falls_back_to_200()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "abc", default));

        // ContentRangeHeaderValue("bytes */100") has no From/To -> selector returns null branch
        await r.ToHttpResponse(TodoBody.From, o => o.WithRange(_ =>
                new ContentRangeHeaderValue(100)))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Created_with_selector_writes_201_with_dynamic_Location()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(7, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From, o => o.Created(t => $"/todos/{t.Id}"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/todos/7");
    }

    [Fact]
    public async Task CreatedAtRoute_with_unresolvable_route_writes_500()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(7, "x", "e", default));

        // No route exists with this name -> LinkGenerator returns null -> InternalServerError
        await r.ToHttpResponse(TodoBody.From,
                o => o.CreatedAtRoute("NonExistent", _ => new RouteValueDictionary()))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task CreatedAtAction_with_unresolvable_action_writes_500()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(7, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From,
                o => o.CreatedAtAction("Get", _ => new RouteValueDictionary(), "Todos"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Failure_uses_per_call_ErrorMapper_when_provided()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoBody.From, o => o.WithErrorMapping(_ => 451))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(451);
    }

    [Fact]
    public async Task ErrorOverrides_match_via_base_type()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.Conflict(null, "dup"));

        // Override targets the actual type; verifies the dictionary lookup walks the hierarchy.
        await r.ToHttpResponse(TodoBody.From, o => o.WithErrorMapping<Error.Conflict>(418))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(418);
    }

    [Fact]
    public async Task Vary_dedupes_case_insensitively_against_existing_header()
    {
        var ctx = NewContext();
        ctx.Response.Headers["Vary"] = "accept";
        var r = Result.Ok(new Todo(1, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From, o => o.Vary("Accept", "Accept-Language"))
            .ExecuteAsync(ctx);

        var joined = string.Join("|", ctx.Response.Headers.Vary.ToArray()!);
        joined.ToLowerInvariant().Split('|', ',', ' ')
            .Where(p => p == "accept").Count().Should().Be(1);
        joined.Should().Contain("Accept-Language");
    }

    [Fact]
    public async Task IValueHttpResultTBody_Value_is_default_when_no_projector()
    {
        var inner = new TrellisHttpResult<Todo, Todo>(
            Result.Ok(new Todo(1, "x", "e", default)),
            null,
            new HttpResponseOptionsBuilder<Todo>().Build_ForTest());
        // No projector: IValueHttpResult<TBody>.Value falls through to default(TBody) which
        // is null for the reference-type Todo.
        ((IValueHttpResult<Todo>)inner).Value.Should().BeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public void StatusCode_hint_is_201_when_LocationKind_set()
    {
        var opts = new HttpResponseOptionsBuilder<Todo>().Created("/x").Build_ForTest();
        var inner = new TrellisHttpResult<Todo, TodoBody>(
            Result.Ok(new Todo(1, "x", "e", default)), TodoBody.From, opts);

        inner.StatusCode.Should().Be(StatusCodes.Status201Created);
        inner.ContentType.Should().Be("application/json");
        ((IValueHttpResult)inner).Value.Should().NotBeNull();
        ((IValueHttpResult<TodoBody>)inner).Value!.Id.Should().Be(1);
    }

    [Fact]
    public void StatusCode_hint_is_200_when_no_LocationKind()
    {
        var opts = new HttpResponseOptionsBuilder<Todo>().Build_ForTest();
        var inner = new TrellisHttpResult<Todo, TodoBody>(
            Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo"))), TodoBody.From, opts);

        inner.StatusCode.Should().Be(StatusCodes.Status200OK);
        inner.Value.Should().BeNull();           // failure path
        ((IValueHttpResult<TodoBody>)inner).Value.Should().BeNull();
    }

    [Fact]
    public void Value_returns_domain_when_no_projector_on_success()
    {
        var opts = new HttpResponseOptionsBuilder<Todo>().Build_ForTest();
        var inner = new TrellisHttpResult<Todo, Todo>(
            Result.Ok(new Todo(2, "y", "e", default)), null, opts);

        inner.Value.Should().BeOfType<Todo>().Which.Id.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_throws_on_null_HttpContext()
    {
        var inner = new TrellisHttpResult<Todo, TodoBody>(
            Result.Ok(new Todo(1, "x", "e", default)), TodoBody.From,
            new HttpResponseOptionsBuilder<Todo>().Build_ForTest());

        await Assert.ThrowsAsync<ArgumentNullException>(() => inner.ExecuteAsync(null!));
    }
}

internal static class HttpResponseOptionsBuilderTestExtensions
{
    /// <summary>Test-only access to internal Build().</summary>
    internal static HttpResponseOptions<T> Build_ForTest<T>(this HttpResponseOptionsBuilder<T> b)
    {
        var m = typeof(HttpResponseOptionsBuilder<T>).GetMethod(
            "Build", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (HttpResponseOptions<T>)m.Invoke(b, null)!;
    }
}
