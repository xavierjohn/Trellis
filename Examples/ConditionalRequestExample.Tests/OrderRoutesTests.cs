namespace ConditionalRequestExample.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

public class OrderRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid OrderId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly WebApplicationFactory<Program> _factory;

    public OrderRoutesTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_OrderRepresentation_Returns200_WithETagMetadata()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/orders/{OrderId}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.IsWeak.Should().BeFalse();
        response.Headers.LastModified.Should().NotBeNull();

        var order = await response.Content.ReadFromJsonAsync<OrderWire>(Ct);
        order.Should().NotBeNull();
        order!.Id.Should().Be(OrderId);
        response.Headers.ETag.Tag.Should().Be($"\"{order.ETag}\"");
    }

    [Fact]
    public async Task Get_WithMatchingIfNoneMatch_Returns304_NotModified()
    {
        using var client = _factory.CreateClient();
        using var firstResponse = await client.GetAsync(new Uri($"/orders/{OrderId}", UriKind.Relative), Ct);
        firstResponse.EnsureSuccessStatusCode();
        var eTag = firstResponse.Headers.ETag;
        eTag.Should().NotBeNull();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/orders/{OrderId}", UriKind.Relative));
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(eTag!.Tag));

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    private sealed record OrderWire(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("number")] string Number,
        [property: JsonPropertyName("total")] decimal Total,
        [property: JsonPropertyName("eTag")] string ETag,
        [property: JsonPropertyName("lastModified")] DateTimeOffset LastModified);
}