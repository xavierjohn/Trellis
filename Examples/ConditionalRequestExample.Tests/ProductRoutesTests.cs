namespace ConditionalRequestExample.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ConditionalRequestExample.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

/// <summary>
/// Integration tests for <c>ConditionalRequestExample</c>. Exercises the OPTIONAL
/// and REQUIRED ETag route groups so we can prove the RFC 9110 status codes
/// (200, 201, 304, 412, 428) are wired up correctly through Trellis' helpers.
/// </summary>
public class ProductRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductRoutesTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_NonExistentProduct_Returns404()
    {
        using var client = _factory.CreateClient();

        var missingId = ProductId.NewUniqueV4();
        using var response = await client.GetAsync(new Uri($"/optional/products/{missingId}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_CreateProduct_Returns201_WithETagHeader_AndStrongETagFormat()
    {
        using var client = _factory.CreateClient();

        var (created, response) = await CreateProductAsync(client, "/optional/products", "Widget-201", 19.99m);

        try
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            created.ETag.Should().NotBeNullOrWhiteSpace();

            response.Headers.ETag.Should().NotBeNull();
            response.Headers.ETag!.IsWeak.Should().BeFalse("strong ETag is required for optimistic concurrency");
            response.Headers.ETag.Tag.Should().StartWith("\"").And.EndWith("\"");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Get_AfterCreate_Returns200_WithSameETagAsCreatedResponse()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/optional/products", "Widget-Get", 21.50m);
        postResponse.Dispose();

        using var getResponse = await client.GetAsync(new Uri($"/optional/products/{created.Id}", UriKind.Relative), Ct);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.ETag.Should().NotBeNull();
        var fetched = await getResponse.Content.ReadFromJsonAsync<ProductWire>(Ct);
        fetched.Should().NotBeNull();
        fetched!.ETag.Should().Be(created.ETag);
    }

    [Fact]
    public async Task Put_OptionalETag_WithoutIfMatch_Returns200_AndUpdatesPrice()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/optional/products", "Widget-Opt-NoMatch", 10m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(client, $"/optional/products/{created.Id}", 99.99m, ifMatch: null);

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResponse.Content.ReadFromJsonAsync<ProductWire>(Ct);
        updated.Should().NotBeNull();
        updated!.Price.Should().Be(99.99m);
        updated.ETag.Should().NotBe(created.ETag);
    }

    [Fact]
    public async Task Put_OptionalETag_WithPreferReturnMinimal_Returns204_AndEmitsPreferenceApplied()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/optional/products", "Widget-Opt-Minimal", 10m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(
            client, $"/optional/products/{created.Id}", 77m, ifMatch: null, prefer: "return=minimal");

        putResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        putResponse.Headers.GetValues("Preference-Applied").Should().ContainSingle().Which.Should().Be("return=minimal");
        putResponse.Headers.Vary.Should().Contain("Prefer");
        putResponse.Headers.ETag.Should().NotBeNull();
        putResponse.Headers.ETag!.Tag.Should().NotBe($"\"{created.ETag}\"");
    }

    [Fact]
    public async Task Put_OptionalETag_WithPreferReturnRepresentation_Returns200_AndEmitsPreferenceApplied()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/optional/products", "Widget-Opt-Rep", 10m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(
            client, $"/optional/products/{created.Id}", 88m, ifMatch: null, prefer: "return=representation");

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        putResponse.Headers.GetValues("Preference-Applied").Should().ContainSingle().Which.Should().Be("return=representation");
        putResponse.Headers.Vary.Should().Contain("Prefer");
        var body = await putResponse.Content.ReadFromJsonAsync<ProductWire>(Ct);
        body.Should().NotBeNull();
        body!.Price.Should().Be(88m);
    }

    [Fact]
    public async Task Put_OptionalETag_WithStaleIfMatch_Returns412_PreconditionFailed()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/optional/products", "Widget-Opt-Stale", 10m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(client, $"/optional/products/{created.Id}", 50m, ifMatch: "\"stale-etag\"");

        putResponse.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Put_RequiredETag_WithoutIfMatch_Returns428_PreconditionRequired()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/required/products", "Gadget-Req-Missing", 12m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(client, $"/required/products/{created.Id}", 33m, ifMatch: null);

        putResponse.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Put_RequiredETag_WithFreshIfMatch_Returns200_AndUpdatesPrice()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/required/products", "Gadget-Req-Fresh", 12m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(client, $"/required/products/{created.Id}", 44m, ifMatch: $"\"{created.ETag}\"");

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResponse.Content.ReadFromJsonAsync<ProductWire>(Ct);
        updated.Should().NotBeNull();
        updated!.Price.Should().Be(44m);
    }

    [Fact]
    public async Task Put_RequiredETag_WithFreshIfMatchAndPreferReturnMinimal_Returns204_AndEmitsPreferenceApplied()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/required/products", "Gadget-Req-Minimal", 12m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(
            client, $"/required/products/{created.Id}", 55m, ifMatch: $"\"{created.ETag}\"", prefer: "return=minimal");

        putResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        putResponse.Headers.GetValues("Preference-Applied").Should().ContainSingle().Which.Should().Be("return=minimal");
        putResponse.Headers.Vary.Should().Contain("Prefer");
        putResponse.Headers.ETag.Should().NotBeNull();
        putResponse.Headers.ETag!.Tag.Should().NotBe($"\"{created.ETag}\"");
    }

    [Fact]
    public async Task Put_RequiredETag_WithFreshIfMatchAndPreferReturnRepresentation_Returns200_AndEmitsPreferenceApplied()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/required/products", "Gadget-Req-Rep", 12m);
        postResponse.Dispose();

        using var putResponse = await PutPriceAsync(
            client, $"/required/products/{created.Id}", 66m, ifMatch: $"\"{created.ETag}\"", prefer: "return=representation");

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        putResponse.Headers.GetValues("Preference-Applied").Should().ContainSingle().Which.Should().Be("return=representation");
        putResponse.Headers.Vary.Should().Contain("Prefer");
        var body = await putResponse.Content.ReadFromJsonAsync<ProductWire>(Ct);
        body.Should().NotBeNull();
        body!.Price.Should().Be(66m);
    }

    [Fact]
    public async Task Get_WithIfNoneMatch_OnUnchangedResource_Returns304_NotModified()
    {
        using var client = _factory.CreateClient();
        var (created, postResponse) = await CreateProductAsync(client, "/optional/products", "Widget-IfNoneMatch", 7m);
        postResponse.Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/optional/products/{created.Id}", UriKind.Relative));
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{created.ETag}\""));

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    private static async Task<(ProductWire Product, HttpResponseMessage Response)> CreateProductAsync(
        HttpClient client, string groupPath, string name, decimal price)
    {
        var payload = new { name, price };
        var response = await client.PostAsJsonAsync(new Uri(groupPath, UriKind.Relative), payload, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ProductWire>(Ct);
        body.Should().NotBeNull();
        return (body!, response);
    }

    private static async Task<HttpResponseMessage> PutPriceAsync(HttpClient client, string path, decimal price, string? ifMatch, string? prefer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { price }),
        };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (prefer is not null)
            request.Headers.TryAddWithoutValidation("Prefer", prefer);

        return await client.SendAsync(request, Ct);
    }

    private sealed record ProductWire(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("price")] decimal Price,
        [property: JsonPropertyName("eTag")] string ETag);

}