namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Branch coverage for <see cref="TrellisWriteOutcomeResult{TDomain,TBody}"/> across every
/// <see cref="WriteOutcome{T}"/> variant, with and without builder metadata, body projection,
/// and Prefer header semantics.
/// </summary>
[Collection("TrellisAspOptionsState")]
public sealed class TrellisWriteOutcomeResultTests : IDisposable
{
    public TrellisWriteOutcomeResultTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private sealed record Item(int Id, string Name, string ETag, DateTimeOffset Modified);

    private sealed record ItemBody(int Id);

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
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
#pragma warning disable CA1822
        public bool TryWrite(ProblemDetailsContext context) => false;
#pragma warning restore CA1822
    }

    [Fact]
    public async Task Failure_writes_problem_details()
    {
        var ctx = NewContext();
        var r = Result.Fail<WriteOutcome<Item>>(new Error.NotFound(new ResourceRef("Item", "1")));

        await r.ToHttpResponse(i => new ItemBody(i.Id)).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Failure_uses_per_call_error_mapping()
    {
        var ctx = NewContext();
        var r = Result.Fail<WriteOutcome<Item>>(new Error.Conflict(null, "x"));

        await r.ToHttpResponse(i => new ItemBody(i.Id), o => o.WithErrorMapping<Error.Conflict>(418))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(418);
    }

    [Fact]
    public async Task Created_with_metadata_emits_ETag_and_LastModified()
    {
        var ctx = NewContext();
        var when = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var meta = RepresentationMetadata.Create()
            .SetStrongETag("v1").SetLastModified(when).Build();
        var outcome = new WriteOutcome<Item>.Created(new Item(1, "n", "v1", when), "/items/1", meta);

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/items/1");
        ctx.Response.Headers.ETag.ToString().Should().Be("\"v1\"");
        ctx.Response.Headers["Last-Modified"].ToString().Should().Be(when.ToString("R"));
    }

    [Fact]
    public async Task Created_with_body_projector_returns_201()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.Created(new Item(7, "n", "v", default), "/items/7");

        await Result.Ok<WriteOutcome<Item>>(outcome)
            .ToHttpResponse(i => new ItemBody(i.Id))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/items/7");
    }

    [Fact]
    public async Task Updated_default_returns_200_without_PreferenceApplied()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "v", default));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
    }

    [Fact]
    public async Task Updated_with_Prefer_representation_emits_PreferenceApplied()
    {
        var ctx = NewContext();
        ctx.Request.Headers["Prefer"] = "return=representation";
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "v", default));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse(i => new ItemBody(i.Id))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
    }

    [Fact]
    public async Task Updated_with_metadata_overrides_propagate()
    {
        var ctx = NewContext();
        var meta = RepresentationMetadata.Create().SetStrongETag("u1").Build();
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "u1", default), meta);

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.Headers.ETag.ToString().Should().Be("\"u1\"");
    }

    [Fact]
    public async Task UpdatedNoContent_writes_204_with_metadata_headers()
    {
        var ctx = NewContext();
        var meta = RepresentationMetadata.Create()
            .SetStrongETag("etag")
            .AddVary("Accept")
            .AddContentLanguage("en")
            .SetContentLocation("/items/1")
            .SetAcceptRanges("bytes")
            .Build();
        var outcome = new WriteOutcome<Item>.UpdatedNoContent(meta);

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Headers.ETag.ToString().Should().Be("\"etag\"");
        ctx.Response.Headers.Vary.ToString().Should().Contain("Accept");
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/items/1");
        ctx.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
    }

    [Fact]
    public async Task Accepted_with_status_body_returns_202_with_RetryAfter()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.Accepted(
            new Item(1, "n", "v", default), "/jobs/1", RetryAfterValue.FromSeconds(5));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse(i => new ItemBody(i.Id))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(202);
        ctx.Response.Headers.Location.ToString().Should().Be("/jobs/1");
        ctx.Response.Headers["Retry-After"].ToString().Should().Be("5");
    }

    [Fact]
    public async Task Accepted_without_body_projector_uses_status_body_directly()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.Accepted(
            new Item(1, "n", "v", default), "/jobs/2");

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(202);
        ctx.Response.Headers.Location.ToString().Should().Be("/jobs/2");
    }

    [Fact]
    public async Task AcceptedNoContent_returns_202_with_Location_and_RetryAfter()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.AcceptedNoContent("/jobs/3", RetryAfterValue.FromSeconds(15));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(202);
        ctx.Response.Headers.Location.ToString().Should().Be("/jobs/3");
        ctx.Response.Headers["Retry-After"].ToString().Should().Be("15");
    }

    [Fact]
    public async Task AcceptedNoContent_without_monitor_or_retry_returns_202()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.AcceptedNoContent();

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(202);
        ctx.Response.Headers.ContainsKey("Location").Should().BeFalse();
        ctx.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task Builder_metadata_applies_for_Updated_with_domain()
    {
        var ctx = NewContext();
        var when = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "abc", when));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse(o => o
            .WithETag(i => i.ETag)
            .WithLastModified(i => i.Modified)
            .Vary("Accept")
            .WithContentLanguage("en")
            .WithContentLocation(i => $"/items/{i.Id}")
            .WithAcceptRanges("bytes"))
            .ExecuteAsync(ctx);

        ctx.Response.Headers.ETag.ToString().Should().Be("\"abc\"");
        ctx.Response.Headers["Last-Modified"].ToString().Should().Be(when.ToString("R"));
        ctx.Response.Headers.Vary.ToString().Should().Contain("Accept");
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/items/1");
        ctx.Response.Headers["Accept-Ranges"].ToString().Should().Be("bytes");
    }

    [Fact]
    public async Task UpdatedNoContent_omits_ETag_header_even_when_builder_supplies_selector()
    {
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.UpdatedNoContent();

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse(o => o
            .WithETag(i => i.ETag)
            .Vary("Accept"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Headers.ContainsKey("ETag").Should().BeFalse();
        // Vary still added (does not depend on domain).
        ctx.Response.Headers.Vary.ToString().Should().Contain("Accept");
    }

    [Fact]
    public async Task HonorPrefer_emits_Vary_Prefer_header_on_response()
    {
        // The Vary: Prefer is emitted before the switch on outcome variants for the success path.
        var ctx = NewContext();
        var outcome = new WriteOutcome<Item>.UpdatedNoContent();

        await Result.Ok<WriteOutcome<Item>>(outcome)
            .ToHttpResponse((Action<HttpResponseOptionsBuilder<Item>>)(o => o.HonorPrefer()))
            .ExecuteAsync(ctx);

        string.Join(",", ctx.Response.Headers.Vary.ToArray()!).Should().Contain("Prefer");
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_HttpContext_is_null()
    {
        var outcome = new WriteOutcome<Item>.UpdatedNoContent();
        var http = Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse();

        await Assert.ThrowsAsync<ArgumentNullException>(() => http.ExecuteAsync(null!));
    }

    [Fact]
    public async Task Vary_header_unions_builder_HonorPrefer_and_metadata_tokens()
    {
        // Regression: ApplyMetadataHeaders previously overwrote the Vary header,
        // dropping builder-supplied entries and the automatic "Vary: Prefer" added
        // by HonorPrefer. All three sources must contribute to the final Vary.
        var ctx = NewContext();
        var meta = RepresentationMetadata.Create()
            .AddVary("Accept")
            .Build();
        var outcome = new WriteOutcome<Item>.UpdatedNoContent(meta);

        await Result.Ok<WriteOutcome<Item>>(outcome)
            .ToHttpResponse((Action<HttpResponseOptionsBuilder<Item>>)(o => o.Vary("Accept-Encoding").HonorPrefer()))
            .ExecuteAsync(ctx);

        var tokens = string.Join(",", ctx.Response.Headers.Vary.ToArray()!)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        tokens.Should().Contain("Accept");
        tokens.Should().Contain("Accept-Encoding");
        tokens.Should().Contain("Prefer");
    }
}
