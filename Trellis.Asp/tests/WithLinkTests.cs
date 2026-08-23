namespace Trellis.Asp.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Tests for <c>WithLink()</c> on <see cref="HttpResponseOptionsBuilder{TDomain}"/> and the
/// non-generic <see cref="HttpResponseOptionsBuilder"/>. Pins the RFC 8288 contract: the
/// configured relations are emitted as a <c>Link</c> field on every builder-driven response
/// path (plain success, paged success, and <c>WriteOutcome</c>), the link target is
/// percent-encoded exactly as the paged <c>next</c>/<c>prev</c> targets already are, and a
/// relation that would forge additional link-params is rejected at configuration time.
/// </summary>
/// <remarks>
/// <para>
/// <c>schema</c> is deliberately NOT offered as a first-class relation: it is not in the IANA
/// link-relation registry, and RFC 8288 section 3.3 admits only a registered name or an
/// extension URI — so a bare <c>rel="schema"</c> is non-conformant and generic clients ignore
/// it. The registered spellings are <c>describedby</c> (a schema describing this resource) and
/// <c>service-desc</c> (an API description document, RFC 8631).
/// </para>
/// </remarks>
public sealed class WithLinkTests
{
    private sealed record Todo(int Id, string Title);

    private sealed record Item(int Id, string Name);

    private const string SchemaHref = "https://api.example.com/schemas/todo.json";

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
        public ValueTask WriteAsync(ProblemDetailsContext c) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext c) => false;
#pragma warning restore CA1822
    }

    private static Page<Todo> PageWithCursors() => new(
        Items: [new Todo(1, "a"), new Todo(2, "b")],
        Next: new Cursor("abc123"),
        Previous: new Cursor("xyz789"),
        RequestedLimit: 50,
        AppliedLimit: 50);

    // ---------- Argument validation ----------

    [Fact]
    public void WithLink_throws_on_null_rel()
    {
        var b = new HttpResponseOptionsBuilder<Todo>();
        FluentActions.Invoking(() => b.WithLink(null!, SchemaHref))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithLink_throws_on_null_href()
    {
        var b = new HttpResponseOptionsBuilder<Todo>();
        FluentActions.Invoking(() => b.WithLink("describedby", null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithLink_throws_on_blank_rel(string rel)
    {
        var b = new HttpResponseOptionsBuilder<Todo>();
        FluentActions.Invoking(() => b.WithLink(rel, SchemaHref))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithLink_throws_on_blank_href(string href)
    {
        var b = new HttpResponseOptionsBuilder<Todo>();
        FluentActions.Invoking(() => b.WithLink("describedby", href))
            .Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The injection guard. An unvalidated relation token closes the quoted string and appends
    /// its own link-params, which is a different attack surface from the link *target*
    /// percent-encoding and is not covered by it.
    /// </summary>
    [Theory]
    [InlineData("describedby\"; rel=\"stylesheet")]
    [InlineData("desc ribedby")]
    [InlineData("describedby;x=1")]
    [InlineData("describedby,next")]
    [InlineData("1describedby")]
    [InlineData("-describedby")]
    [InlineData("described\nby")]
    public void WithLink_rejects_a_relation_that_could_forge_link_params(string rel)
    {
        var b = new HttpResponseOptionsBuilder<Todo>();
        FluentActions.Invoking(() => b.WithLink(rel, SchemaHref))
            .Should().Throw<ArgumentException>(
                "a relation that is neither a valid RFC 8288 token nor an absolute URI must be rejected at configuration time");
    }

    [Fact]
    public async Task WithLink_accepts_an_absolute_uri_extension_relation()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi"));

        await r.ToHttpResponse(t => t,
            o => o.WithLink("https://example.com/rels/schema", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Link"].ToString().Should().Be(
            $"<{SchemaHref}>; rel=\"https://example.com/rels/schema\"",
            "RFC 8288 section 3.3 admits an extension relation type expressed as an absolute URI");
    }

    [Fact]
    public async Task WithLink_accepts_a_token_that_is_shaped_correctly_but_unregistered()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi"));

        await r.ToHttpResponse(t => t, o => o.WithLink("schema", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Link"].ToString().Should().Be(
            $"<{SchemaHref}>; rel=\"schema\"",
            "only the RFC 8288 token shape is validated; IANA registration is deliberately not enforced, "
            + "because the registry changes independently of Trellis and an allow-list would reject a "
            + "newly registered relation until the framework shipped again");
    }

    [Fact]
    public async Task WithLink_normalizes_a_registered_token_to_lowercase()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi"));

        await r.ToHttpResponse(t => t, o => o.WithLink("DescribedBy", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.Headers["Link"].ToString().Should().Be(
            $"<{SchemaHref}>; rel=\"describedby\"",
            "RFC 8288 section 2.1 defines registered relation types as case-insensitive and lowercase on the wire");
    }

    // ---------- Emission ----------

    [Fact]
    public async Task WithLink_emits_describedby_on_200()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi"));

        await r.ToHttpResponse(t => t, o => o.WithLink("describedby", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers["Link"].ToString().Should().Be($"<{SchemaHref}>; rel=\"describedby\"");
    }

    [Fact]
    public async Task WithLink_emits_every_configured_relation()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi"));

        await r.ToHttpResponse(t => t, o => o
                .WithLink("describedby", SchemaHref)
                .WithLink("service-desc", "https://api.example.com/openapi.json"))
            .ExecuteAsync(ctx);

        var link = ctx.Response.Headers["Link"].ToString();
        link.Should().Contain($"<{SchemaHref}>; rel=\"describedby\"");
        link.Should().Contain("<https://api.example.com/openapi.json>; rel=\"service-desc\"");
    }

    [Fact]
    public async Task WithLink_percent_encodes_characters_forbidden_in_a_link_target()
    {
        var ctx = NewContext();
        var r = Result.Ok(new Todo(1, "hi"));

        await r.ToHttpResponse(t => t,
            o => o.WithLink("describedby", "https://api.example.com/s?q=a>b"))
            .ExecuteAsync(ctx);

        var link = ctx.Response.Headers["Link"].ToString();
        link.Should().Be("<https://api.example.com/s?q=a%3Eb>; rel=\"describedby\"",
            "a literal '>' would close the URI-Reference early and let the remainder forge link-params");
    }

    [Fact]
    public async Task WithLink_emits_on_paged_success()
    {
        var ctx = NewContext();
        var r = Result.Ok(PageWithCursors());

        await r.ToHttpResponse(
                (_, _) => "/todos?cursor=next",
                t => t,
                o => o.WithLink("describedby", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers["Link"].ToString().Should().Contain($"<{SchemaHref}>; rel=\"describedby\"",
            "the paged path applies builder-driven headers through PagedSuccessHeaderWrapper, not the plain result type");
    }

    /// <summary>
    /// The paged path already emits its own <c>Link</c> field for <c>next</c>/<c>prev</c>.
    /// A configured relation must be additive rather than replacing the pagination cursors.
    /// </summary>
    [Fact]
    public async Task WithLink_coexists_with_paged_next_and_prev()
    {
        var ctx = NewContext();
        var r = Result.Ok(PageWithCursors());

        await r.ToHttpResponse(
                (c, _) => $"/todos?cursor={c.Token}",
                t => t,
                o => o.WithLink("describedby", SchemaHref))
            .ExecuteAsync(ctx);

        var link = ctx.Response.Headers["Link"].ToString();
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain("rel=\"prev\"");
        link.Should().Contain("rel=\"describedby\"");
    }

    [Fact]
    public async Task WithLink_emits_on_write_outcome_created()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.Created(new Item(7, "n"), "/items/7");

        await Result.Ok<WriteOutcome<Item>>(outcome)
            .ToHttpResponse((HttpResponseOptionsBuilder<Item> o) => o.WithLink("describedby", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers["Link"].ToString().Should().Contain($"<{SchemaHref}>; rel=\"describedby\"",
            "WriteOutcome responses apply builder metadata through a separate site from the plain result type");
    }

    [Fact]
    public void WithLink_is_not_offered_on_the_nongeneric_builder() =>
        typeof(HttpResponseOptionsBuilder).GetMethod("WithLink").Should().BeNull(
            "the non-generic builder is consumed only by Error.ToHttpResponse, which is a pure failure "
            + "response, and configured links are success-path headers — so the overload could never emit");

    /// <summary>
    /// <c>Result.Ok()</c> is <c>Result&lt;Unit&gt;</c>, so this exercises the generic builder's
    /// no-payload branch: links must survive the 204 short-circuit that skips the body.
    /// </summary>
    [Fact]
    public async Task WithLink_emits_on_a_no_content_success()
    {
        var ctx = NewContext();
        var r = Result.Ok();

        await r.ToHttpResponse(o => o.WithLink("service-desc", "https://api.example.com/openapi.json"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Headers["Link"].ToString().Should().Be(
            "<https://api.example.com/openapi.json>; rel=\"service-desc\"");
    }

    /// <summary>
    /// Configured links follow the <c>Vary</c> / <c>Content-Language</c> contract, not the
    /// <c>Cache-Control</c> one: they are applied in <c>ApplyMetadata</c>, which runs on the
    /// success path only. Pinning this stops a later change from leaking a link onto a failure
    /// response without an explicit decision.
    /// </summary>
    [Fact]
    public async Task WithLink_does_not_emit_on_a_failure_response()
    {
        var ctx = NewContext();
        var r = Result.Fail<Todo>(new Error.NotFound(new ResourceRef("Todo", "1")));

        await r.ToHttpResponse(t => t, o => o.WithLink("describedby", SchemaHref))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
        ctx.Response.Headers.ContainsKey("Link").Should().BeFalse();
    }
}
