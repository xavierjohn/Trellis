namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class RemoteOrderClientTests
{
    [Fact]
    public async Task FindOrderAsync_200_returns_present_order()
    {
        var expected = new RemoteOrderDto(Guid.NewGuid(), "ORD-123");
        using var httpClient = new HttpClient(new StubHandler(
            HttpStatusCode.OK,
            JsonContent.Create(expected, RemoteOrderJsonContext.Default.RemoteOrderDto)))
        {
            BaseAddress = new Uri("https://orders.example/"),
        };
        var client = new RemoteOrderClient(httpClient);

        var result = await client.FindOrderAsync(expected.Id, CancellationToken.None);

        result.Should().BeSuccess()
            .Which.Should().HaveValueEquivalentTo(expected);
    }

    [Fact]
    public async Task FindOrderAsync_404_returns_absent_order()
    {
        using var httpClient = new HttpClient(new StubHandler(HttpStatusCode.NotFound))
        {
            BaseAddress = new Uri("https://orders.example/"),
        };
        var client = new RemoteOrderClient(httpClient);

        var result = await client.FindOrderAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeSuccess()
            .Which.Should().BeNone();
    }

    [Fact]
    public async Task FindOrderAsync_500_returns_failure()
    {
        using var httpClient = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://orders.example/"),
        };
        var client = new RemoteOrderClient(httpClient);

        var result = await client.FindOrderAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>();
    }

    private sealed class RemoteOrderClient(HttpClient httpClient)
    {
        public Task<Result<Maybe<RemoteOrderDto>>> FindOrderAsync(Guid orderId, CancellationToken ct = default) =>
            httpClient.GetAsync($"orders/{orderId:N}", ct)
                .ReadJsonOrNoneOn404Async(RemoteOrderJsonContext.Default.RemoteOrderDto, ct);
    }

    private sealed class StubHandler(HttpStatusCode status, HttpContent? content = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = content ?? new StringContent(string.Empty) });
    }
}

internal sealed record RemoteOrderDto(Guid Id, string Number);

[JsonSerializable(typeof(RemoteOrderDto))]
internal partial class RemoteOrderJsonContext : JsonSerializerContext;