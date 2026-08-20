namespace Trellis.Asp.Tests;

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// Pins <c>Content-Language</c> on problem responses.
/// </summary>
/// <remarks>
/// Success responses have carried this header for some time; failure responses return early into
/// <see cref="ResponseFailureWriter"/>, which applied none of the representation metadata, so every
/// problem response shipped prose unlabelled.
/// </remarks>
public sealed class ProblemContentLanguageTests
{
    private static DefaultHttpContext NewContext(TrellisAspOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (options is not null)
            services.AddSingleton(options);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task No_header_is_emitted_by_default()
    {
        var ctx = NewContext();

        await ResponseFailureWriter.WriteAsync(ctx, new Error.NotFound(new ResourceRef("thing", "1")), 404);

        ctx.Response.Headers.ContentLanguage.Should().BeEmpty(
            "the framework cannot know the language of application-supplied detail prose, so asserting one would be a claim it has not checked");
    }

    [Fact]
    public async Task The_configured_tag_is_emitted_when_set()
    {
        var ctx = NewContext(new TrellisAspOptions { ProblemContentLanguage = "en" });

        await ResponseFailureWriter.WriteAsync(ctx, new Error.NotFound(new ResourceRef("thing", "1")), 404);

        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("en");
    }

    [Fact]
    public async Task No_vary_header_is_emitted()
    {
        var ctx = NewContext(new TrellisAspOptions { ProblemContentLanguage = "en" });

        await ResponseFailureWriter.WriteAsync(ctx, new Error.NotFound(new ResourceRef("thing", "1")), 404);

        ctx.Response.Headers.Vary.ToString().Should().NotContain("Accept-Language",
            "the value is static and nothing reads Accept-Language, so the response does not vary by it; telling caches otherwise costs partitioning for no benefit");
    }

    [Fact]
    public async Task An_existing_header_is_not_overwritten()
    {
        var ctx = NewContext(new TrellisAspOptions { ProblemContentLanguage = "en" });
        ctx.Response.Headers.ContentLanguage = "fr";

        await ResponseFailureWriter.WriteAsync(ctx, new Error.NotFound(new ResourceRef("thing", "1")), 404);

        ctx.Response.Headers.ContentLanguage.ToString().Should().Be("fr",
            "a caller that has already labelled the response knows more than the static option does");
    }
}