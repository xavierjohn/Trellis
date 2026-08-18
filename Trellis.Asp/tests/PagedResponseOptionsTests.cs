namespace Trellis.Asp.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;
using Xunit;

/// <summary>
/// Regression coverage for the paged-response-options fix: every
/// <see cref="HttpResponseOptionsBuilder{TDomain}"/>-driven header
/// (<c>ETag</c>, <c>Last-Modified</c>, <c>Vary</c>, <c>Content-Language</c>,
/// <c>Content-Location</c>, conditional-request preconditions) now flows through to the
/// paged <c>Result&lt;Page&lt;T&gt;&gt;</c> response — previously silently dropped.
/// </summary>
public sealed class PagedResponseOptionsTests
{
    private sealed record Todo(int Id, string Title, string ETag);

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

    private static Page<Todo> SamplePage() => new(
        Items: [new Todo(1, "a", "e1"), new Todo(2, "b", "e2")],
        Next: null,
        Previous: null,
        RequestedLimit: 50,
        AppliedLimit: 50);

    private sealed class NoopPds : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    // ---------- ETag header ----------

    [Fact]
    public async Task WithETag_emits_strong_etag_on_paged_success()
    {
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithETag(p => $"page-{p.Items.Count}"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers.ETag.ToString().Should().Be("\"page-2\"");
    }

    // ---------- Last-Modified header ----------

    [Fact]
    public async Task WithLastModified_emits_RFC1123_date_on_paged_success()
    {
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());
        var stamp = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithLastModified(_ => stamp))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers.LastModified.ToString().Should().Be("Thu, 15 Jan 2026 10:30:00 GMT");
    }

    // ---------- Vary list + dedupe vs VaryForActor ----------

    [Fact]
    public async Task Vary_appends_to_paged_response()
    {
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.Vary("Accept-Language", "Accept-Encoding"))
            .ExecuteAsync(ctx);

        var vary = ctx.Response.Headers.Vary.ToString();
        vary.Should().Contain("Accept-Language");
        vary.Should().Contain("Accept-Encoding");
    }

    // ---------- Content-Language header ----------

    [Fact]
    public async Task WithContentLanguage_emits_on_paged_success()
    {
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithContentLanguage("en", "fr"))
            .ExecuteAsync(ctx);

        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en, fr");
    }

    // ---------- Content-Location header ----------

    [Fact]
    public async Task WithContentLocation_emits_on_paged_success()
    {
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithContentLocation(_ => "/todos?cursor=current"))
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/todos?cursor=current");
    }

    // ---------- Conditional GET: 304 Not Modified ----------

    [Fact]
    public async Task EvaluatePreconditions_returns_304_when_If_None_Match_matches_emitted_etag()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-None-Match"] = "\"page-2\"";
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithETag(p => $"page-{p.Items.Count}").EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        ctx.Response.Headers.ETag.ToString().Should().Be("\"page-2\"");
    }

    [Fact]
    public async Task EvaluatePreconditions_returns_304_with_selector_CacheControl_on_revalidation()
    {
        // 304 is a success disposition — the revalidation response must carry the same
        // Cache-Control policy a fresh 200 would.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-None-Match"] = "\"page-2\"";
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o
                    .WithETag(p => $"page-{p.Items.Count}")
                    .WithCacheControl(_ => CacheControl.Public(TimeSpan.FromMinutes(5)))
                    .EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        ctx.Response.Headers.CacheControl.ToString().Should().Be("public, max-age=300");
    }

    // ---------- Conditional GET: 412 Precondition Failed ----------

    [Fact]
    public async Task EvaluatePreconditions_returns_412_when_If_Match_fails()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-Match"] = "\"stale-etag\"";
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithETag(p => $"page-{p.Items.Count}").EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public async Task EvaluatePreconditions_412_does_not_inherit_selector_CacheControl()
    {
        // Mid-flow client error must not be cached as a negative response — the selector
        // Cache-Control must not leak onto the 412 even though the success 200 / 304 would
        // carry it. Mirrors TrellisHttpResult's "selector applies only at success-emit
        // points" contract.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-Match"] = "\"stale-etag\"";
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o
                    .WithETag(p => $"page-{p.Items.Count}")
                    .WithCacheControl(_ => CacheControl.Public(TimeSpan.FromMinutes(5)))
                    .EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
        ctx.Response.Headers.CacheControl.ToString().Should().NotContain("max-age=300");
    }

    [Fact]
    public async Task EvaluatePreconditions_412_still_carries_static_CacheControl()
    {
        // Static Cache-Control protects mid-flow failures (no-store, private) and applies
        // before the conditional branch — the consumer's endpoint-level policy.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-Match"] = "\"stale-etag\"";
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o
                    .WithETag(p => $"page-{p.Items.Count}")
                    .WithCacheControl(CacheControl.NoStore())
                    .EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
        ctx.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    // ---------- Selector deferral ----------

    [Fact]
    public async Task Header_selectors_defer_to_ExecuteAsync_and_run_once_per_request()
    {
        // Mirrors the existing Cache-Control deferral test: per-domain selectors must
        // evaluate inside ExecuteAsync, not eagerly during ToHttpResponse. Also pins that
        // the resolved ETag/Last-Modified values are cached so a non-deterministic selector
        // cannot produce inconsistent header-vs-precondition-metadata values.
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());
        var etagCalls = 0;
        var lastModCalls = 0;

        var http = r.ToHttpResponse(
            (_, _) => "/next",
            t => t,
            o => o
                .WithETag(_ => { etagCalls++; return "page-1"; })
                .WithLastModified(_ => { lastModCalls++; return DateTimeOffset.UtcNow; })
                .EvaluatePreconditions());

        etagCalls.Should().Be(0);
        lastModCalls.Should().Be(0);

        ctx.Request.Method = "GET";
        await http.ExecuteAsync(ctx);

        etagCalls.Should().Be(1, "ETag selector must be invoked exactly once per request even when precondition evaluation also consults it");
        lastModCalls.Should().Be(1, "Last-Modified selector must be invoked exactly once per request even when precondition evaluation also consults it");
    }

    [Fact]
    public async Task EvaluatePreconditions_304_does_not_run_body_projector()
    {
        // The body projector projects every item into the response envelope. On a 304
        // revalidation the body is not emitted, so running the projector wastes work and
        // (worse) may invoke expensive per-item logic that the consumer expected to be
        // gated on a fresh 200. Regression for the eager-build bug where
        // PagedResponseBuilder.Build ran before the precondition decision.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-None-Match"] = "\"page-2\"";
        var r = Result.Ok(SamplePage());
        var projectorCalls = 0;

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => { projectorCalls++; return t; },
                o => o.WithETag(p => $"page-{p.Items.Count}").EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
        projectorCalls.Should().Be(0, "body projector must not run when the response short-circuits to 304");
    }

    [Fact]
    public async Task EvaluatePreconditions_412_does_not_run_body_projector()
    {
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        ctx.Request.Headers["If-Match"] = "\"stale-etag\"";
        var r = Result.Ok(SamplePage());
        var projectorCalls = 0;

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => { projectorCalls++; return t; },
                o => o.WithETag(p => $"page-{p.Items.Count}").EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status412PreconditionFailed);
        projectorCalls.Should().Be(0, "body projector must not run when the response fails into 412");
    }

    [Fact]
    public async Task EvaluatePreconditions_returns_304_on_If_Modified_Since_matching_emitted_LastModified()
    {
        // Regression: the emitted Last-Modified RFC1123 header is second-precision, so the
        // precondition metadata must also be second-precision — otherwise a client sending
        // back the exact emitted value would see selector-ticks > Parse(header).Ticks and
        // miss the 304 short-circuit. Selector returns a sub-second timestamp on purpose.
        var ctx = NewContext();
        ctx.Request.Method = "GET";
        var stamp = new DateTimeOffset(2026, 1, 15, 10, 30, 0, 500, TimeSpan.Zero);
        // Client revalidates with the truncated value that was emitted to it.
        ctx.Request.Headers["If-Modified-Since"] = "Thu, 15 Jan 2026 10:30:00 GMT";
        var r = Result.Ok(SamplePage());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithLastModified(_ => stamp).EvaluatePreconditions())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status304NotModified);
    }

    // ---------- Combined coverage: every header on one response ----------

    [Fact]
    public async Task All_supported_options_apply_together_on_paged_success()
    {
        var ctx = NewContext();
        var r = Result.Ok(SamplePage());
        var stamp = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o
                    .WithETag(p => $"page-{p.Items.Count}")
                    .WithLastModified(_ => stamp)
                    .Vary("Accept-Language")
                    .WithContentLanguage("en")
                    .WithContentLocation(_ => "/todos")
                    .WithCacheControl(CacheControl.Public(TimeSpan.FromMinutes(1))))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers.ETag.ToString().Should().Be("\"page-2\"");
        ctx.Response.Headers.LastModified.ToString().Should().Be("Thu, 15 Jan 2026 10:30:00 GMT");
        ctx.Response.Headers.Vary.ToString().Should().Contain("Accept-Language");
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/todos");
        ctx.Response.Headers.CacheControl.ToString().Should().Be("public, max-age=60");
    }

    [Theory]
    [InlineData(">")]
    [InlineData("<")]
    [InlineData("\"")]
    [InlineData(" ")]
    public async Task Link_header_percent_encodes_characters_that_would_break_rfc8288_parsing(string hostile)
    {
        // Cursor tokens are opaque server-defined strings and nextUrlBuilder is caller-supplied,
        // so an unencoded delimiter can escape the <URI-Reference> and forge extra link params.
        var ctx = NewContext();
        var page = new Page<Todo>(
            Items: [new Todo(1, "a", "e1")],
            Next: new Cursor($"tok{hostile}en"),
            Previous: null,
            RequestedLimit: 50,
            AppliedLimit: 50);

        await Result.Ok(page)
            .ToHttpResponse((c, limit) => $"https://api.test/todos?cursor={c.Token}&limit={limit}", t => t)
            .ExecuteAsync(ctx);

        var link = ctx.Response.Headers["Link"].ToString();

        link.Should().StartWith("<").And.EndWith("; rel=\"next\"");
        link.Should().NotContain($"tok{hostile}en", "the raw delimiter must not survive into the header");
        link.Count(c => c == '>').Should().Be(1, "a single next link must close exactly one URI-Reference");
        link.Count(c => c == '<').Should().Be(1, "a single next link must open exactly one URI-Reference");
    }

    [Fact]
    public async Task Link_header_leaves_ordinary_urls_untouched()
    {
        var ctx = NewContext();
        var page = new Page<Todo>(
            Items: [new Todo(1, "a", "e1")],
            Next: new Cursor("abc123"),
            Previous: new Cursor("xyz789"),
            RequestedLimit: 50,
            AppliedLimit: 50);

        await Result.Ok(page)
            .ToHttpResponse((c, limit) => $"https://api.test/todos?cursor={c.Token}&limit={limit}", t => t)
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Link"].ToString().Should().Be(
            "<https://api.test/todos?cursor=abc123&limit=50>; rel=\"next\", " +
            "<https://api.test/todos?cursor=xyz789&limit=50>; rel=\"prev\"");
    }
}