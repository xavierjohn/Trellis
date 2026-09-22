namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Outcome-level coverage for <c>ToHttpResponse</c> branches not exercised by the higher-level
/// <c>ToHttpResponseTests</c>: metadata headers, conditional-request decisions,
/// location resolution, and per-call error-mapping overrides. Every test drives the public
/// extension and asserts on what a caller observes (status code, headers, body bytes).
/// </summary>
public sealed class TrellisHttpResultExtraTests
{
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
    public async Task Response_includes_LastModified_ContentLanguage_and_ContentLocation_headers()
    {
        var ctx = NewContext();
        var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var r = Result.Ok(new Todo(1, "x", "abc", when));

        await r.ToHttpResponse(TodoBody.From, o => o
            .WithLastModified(t => t.Modified)
            .WithContentLanguage("en-US", "en")
            .WithContentLocation(t => $"/todos/{t.Id}"))
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Last-Modified"].ToString().Should().Be(when.ToString("R"));
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en-US, en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/todos/1");
    }

    [Fact]
    public async Task ETag_and_ContentLocation_headers_are_omitted_when_selectors_return_null()
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
    public async Task Weak_ETag_is_serialized_with_W_prefix()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "v1", default));

        await r.ToHttpResponse(TodoBody.From, o => o.WithETag(t => EntityTagValue.Weak(t.ETag)))
            .ExecuteAsync(ctx);

        ctx.Response.Headers.ETag.ToString().Should().Be("W/\"v1\"");
    }

    [Fact]
    public async Task POST_request_with_If_None_Match_does_not_short_circuit_to_304()
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
    public async Task GET_request_with_If_None_Match_returns_200_when_no_validator_metadata()
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
    public async Task Response_is_412_when_If_Match_does_not_match_current_ETag()
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
    public async Task Response_is_412_with_IfUnmodifiedSince_kind_when_resource_modified_after_header_value()
    {
        // Regression: previously the precondition kind was hard-coded to IfMatch even
        // when the failure came from If-Unmodified-Since. ConditionalRequestEvaluatorTests
        // covers the kind reporting; here we assert the end-to-end 412 status survives.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        var when = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        ctx.Request.Headers["If-Unmodified-Since"] = when.AddDays(-1).ToString("R");
        var r = Result.Ok(new Todo(1, "x", "abc", when));

        await r.ToHttpResponse(TodoBody.From, o => o.WithLastModified(t => t.Modified).EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task Response_is_304_when_If_Modified_Since_is_after_Last_Modified()
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
    public async Task Response_is_304_when_If_Modified_Since_matches_emitted_LastModified_at_sub_second_precision()
    {
        // Regression: the emitted Last-Modified RFC1123 header is second-precision, so the
        // precondition metadata must also be truncated to seconds — otherwise a client
        // sending back the exact emitted value would see selector-ticks > Parse(header).Ticks
        // and miss the 304 short-circuit. Selector returns a sub-second timestamp on purpose.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        var stamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, 500, TimeSpan.Zero);
        ctx.Request.Headers["If-Modified-Since"] = "Mon, 01 Jan 2024 00:00:00 GMT";
        var r = Result.Ok(new Todo(1, "x", "abc", stamp));

        await r.ToHttpResponse(TodoBody.From, o => o.WithLastModified(t => t.Modified).EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(304);
    }

    [Fact]
    public async Task Created_with_location_selector_returns_201_with_dynamic_Location_header()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(7, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From, o => o.Created(t => $"/todos/{t.Id}"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/todos/7");
    }

    [Fact]
    public async Task CreatedAtRoute_with_unknown_route_name_returns_500()
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
    public async Task CreatedAtAction_with_unknown_action_returns_500()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(7, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From,
                o => o.CreatedAtAction("Get", _ => new RouteValueDictionary(), "Todos"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task Failure_response_uses_per_call_error_mapper_status_code()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(TodoBody.From, o => o.WithErrorMapping(_ => 451))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(451);
    }

    [Fact]
    public async Task Failure_response_uses_typed_error_override_status_code()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.Conflict(Resource: null, Code: "dup"));

        // Override targets the actual type; verifies the dictionary lookup walks the hierarchy.
        await r.ToHttpResponse(TodoBody.From, o => o.WithErrorMapping<Error.Conflict>(418))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(418);
    }

    [Fact]
    public async Task Vary_header_does_not_duplicate_case_insensitive_token_already_present()
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
    public async Task Success_response_advertises_application_json_Content_Type()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.ContentType.Should().StartWith("application/json");
        ctx.Response.Body.Position = 0;
        new StreamReader(ctx.Response.Body).ReadToEnd().Should().Contain("\"id\":1");
    }

    [Fact]
    public async Task Created_response_writes_201_and_Location_header_for_literal_path()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(3, "x", "e", default));

        await r.ToHttpResponse(TodoBody.From, o => o.Created("/todos/3")).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/todos/3");
        ctx.Response.ContentType.Should().StartWith("application/json");
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_HttpContext_is_null()
    {
        var http = Result.Ok(new Todo(1, "x", "e", default)).ToHttpResponse(TodoBody.From);

        await Assert.ThrowsAsync<ArgumentNullException>(() => http.ExecuteAsync(null!));
    }
}