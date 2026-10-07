namespace Trellis.Asp.Tests;

using System.IO;
using System.Text.Json;
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
public sealed class TrellisWriteOutcomeResultTests
{
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
        var r = Result.Fail<WriteOutcome<Item>>(new Error.Conflict(Resource: null, Code: "x"));

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task Created_WithoutLocation_OmitsHeader(string? location)
    {
        var ctx = NewContext();
        var outcome = WriteOutcome.Created(new Item(7, "n", "v", default), location);

        await Result.Ok(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.ContainsKey("Location").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Created_BlankOutcomeLocation_UsesBuilderLiteralOrSelector(bool selector)
    {
        var ctx = NewContext();
        var outcome = WriteOutcome.Created(new Item(7, "n", "v", default), string.Empty);

        await Result.Ok(outcome).ToHttpResponse(i => new ItemBody(i.Id), options =>
        {
            if (selector)
                options.Created(item => $"/items/{item.Id}");
            else
                options.Created("/items/7");
        }).ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/items/7");
    }

    [Fact]
    public async Task Created_ExplicitLocation_SkipsBuilderLocationAndResolvers()
    {
        var ctx = NewContext();
        var outcome = WriteOutcome.Created(new Item(7, "n", "v", default), "/items/7");

        await Result.Ok(outcome).ToHttpResponse((HttpResponseOptionsBuilder<Item> options) => options
            .CreatedAtRoute("Missing", _ => throw new InvalidOperationException("Selector must not run."))
            .WithRouteValueResolver("tenant", _ => throw new InvalidOperationException("Resolver must not run."))
            .WithLocationRouteResolver(_ => throw new InvalidOperationException("Destination callback must not run.")))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/items/7");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task Created_UnresolvedBuilderLocation_ReturnsCodedFailureWithoutSuccessHeaders(string? location)
    {
        var ctx = NewContext();
        var successSelectorRan = false;
        var outcome = WriteOutcome.Created(new Item(7, "n", "v", default), string.Empty);

        await Result.Ok(outcome).ToHttpResponse((HttpResponseOptionsBuilder<Item> options) => options
            .Created(_ => location!)
            .WithCacheControl(CacheControl.NoStore())
            .WithCacheControl(_ =>
            {
                successSelectorRan = true;
                return CacheControl.Public(TimeSpan.FromMinutes(1));
            })
            .WithLink("describedby", "/items/schema"))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(500);
        ctx.Response.Headers.ContainsKey("Location").Should().BeFalse();
        ctx.Response.Headers.ContainsKey("Link").Should().BeFalse();
        ctx.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        successSelectorRan.Should().BeFalse();
        ctx.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(
            ctx.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.RootElement.GetProperty("code").GetString().Should().Be(FaultCodes.ResponseLocationUnresolved);
    }

    [Fact]
    public async Task Created_UnresolvedBuilderLocation_UsesPerCallErrorMapping()
    {
        var ctx = NewContext();
        var outcome = WriteOutcome.Created(new Item(7, "n", "v", default), string.Empty);

        await Result.Ok(outcome).ToHttpResponse((HttpResponseOptionsBuilder<Item> options) => options
            .Created(_ => null!)
            .WithErrorMapping<Error.Unexpected>(502))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task Created_BuilderLocationWithHonorPrefer_Preserves201AndRepresentation()
    {
        var ctx = NewContext();
        ctx.Request.Headers["Prefer"] = "return=minimal";
        var outcome = WriteOutcome.Created(new Item(7, "n", "v", default), string.Empty);

        await Result.Ok(outcome).ToHttpResponse((HttpResponseOptionsBuilder<Item> options) =>
                options.Created("/items/7").HonorPrefer())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(201);
        ctx.Response.Headers.Location.ToString().Should().Be("/items/7");
        ctx.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
        ctx.Response.Body.Length.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("updated", 200, null)]
    [InlineData("updated-no-content", 204, null)]
    [InlineData("accepted", 202, "/jobs/7")]
    [InlineData("accepted-no-content", 202, "/jobs/7")]
    public async Task NonCreated_BuilderLocation_DoesNotChangeOutcome(
        string variant, int status, string? location)
    {
        var ctx = NewContext();
        var item = new Item(7, "n", "v", default);
        var outcome = variant switch
        {
            "updated" => WriteOutcome.Updated(item),
            "updated-no-content" => WriteOutcome.UpdatedNoContent<Item>(),
            "accepted" => WriteOutcome.Accepted(item, location),
            "accepted-no-content" => WriteOutcome.AcceptedNoContent<Item>(location),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        await Result.Ok(outcome).ToHttpResponse((HttpResponseOptionsBuilder<Item> options) => options
            .Created(_ => throw new InvalidOperationException("Location selector must not run.")))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(status);
        ctx.Response.Headers.Location.ToString().Should().Be(location ?? string.Empty);
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

        await Result.Ok<WriteOutcome<Item>>(outcome)
            .ToHttpResponse(i => new ItemBody(i.Id), o => o.HonorPrefer())
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers["Preference-Applied"].ToString().Should().Be("return=representation");
    }

    [Fact]
    public async Task Updated_with_HonorPrefer_and_return_minimal_returns_204_with_PreferenceApplied()
    {
        var ctx = NewContext();
        ctx.Request.Headers["Prefer"] = "return=minimal";
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "v", default));

        await Result.Ok<WriteOutcome<Item>>(outcome)
            .ToHttpResponse((Action<HttpResponseOptionsBuilder<Item>>)(o => o.HonorPrefer()))
            .ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Headers["Preference-Applied"].ToString().Should().Be("return=minimal");
    }

    [Fact]
    public async Task Updated_without_HonorPrefer_ignores_return_minimal_request_header()
    {
        // Regression for M-3: Prefer is opt-in. When HonorPrefer is not enabled the response
        // body must remain the full representation (200) and no Preference-Applied/Vary: Prefer
        // headers should be emitted, even if the client sent Prefer: return=minimal.
        var ctx = NewContext();
        ctx.Request.Headers["Prefer"] = "return=minimal";
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "v", default));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
        ctx.Response.Headers.Vary.ToString().Should().NotContain("Prefer");
    }

    [Fact]
    public async Task Updated_without_HonorPrefer_ignores_return_representation_request_header()
    {
        var ctx = NewContext();
        ctx.Request.Headers["Prefer"] = "return=representation";
        var outcome = new WriteOutcome<Item>.Updated(new Item(1, "n", "v", default));

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(200);
        ctx.Response.Headers.ContainsKey("Preference-Applied").Should().BeFalse();
        ctx.Response.Headers.Vary.ToString().Should().NotContain("Prefer");
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
            .Build();
        var outcome = new WriteOutcome<Item>.UpdatedNoContent(meta);

        await Result.Ok<WriteOutcome<Item>>(outcome).ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(204);
        ctx.Response.Headers.ETag.ToString().Should().Be("\"etag\"");
        ctx.Response.Headers.Vary.ToString().Should().Contain("Accept");
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/items/1");
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
            .WithContentLocation(i => $"/items/{i.Id}"))
            .ExecuteAsync(ctx);

        ctx.Response.Headers.ETag.ToString().Should().Be("\"abc\"");
        ctx.Response.Headers["Last-Modified"].ToString().Should().Be(when.ToString("R"));
        ctx.Response.Headers.Vary.ToString().Should().Contain("Accept");
        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en");
        ctx.Response.Headers["Content-Location"].ToString().Should().Be("/items/1");
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