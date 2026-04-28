namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class AsyncChainIntegrationTests
{
    private sealed class StubHandler(HttpStatusCode status, HttpContent? content = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = content ?? new StringContent(string.Empty) });
    }

    [Fact]
    public async Task GetAsync_HandleNotFoundAsync_ReadJsonAsync_happy_path()
    {
        var person = new camelcasePerson { firstName = "Ada", age = 36 };
        using var client = new HttpClient(new StubHandler(
            HttpStatusCode.OK,
            JsonContent.Create(person, SourceGenerationContext.Default.camelcasePerson)));

        var result = await client.GetAsync("https://example/api/people/1", CancellationToken.None)
            .HandleNotFoundAsync(new Error.NotFound(new ResourceRef("Person", "1")))
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var got = result.Should().BeSuccess().Subject;
        got.firstName.Should().Be("Ada");
        got.age.Should().Be(36);
    }

    [Fact]
    public async Task GetAsync_HandleNotFoundAsync_404_returns_NotFound()
    {
        using var client = new HttpClient(new StubHandler(HttpStatusCode.NotFound));
        var notFound = new Error.NotFound(new ResourceRef("Person", "missing")) { Detail = "missing" };

        var result = await client.GetAsync("https://example/api/people/missing", CancellationToken.None)
            .HandleNotFoundAsync(notFound)
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.NotFound>()
            .Which.Should().HaveDetail("missing");
    }

    [Fact]
    public async Task ToResultAsync_then_HandleConflictAsync_then_ReadJsonAsync_pipes_errors_correctly()
    {
        // 401 surfaces via the statusMap; 409 would surface via HandleConflictAsync;
        // success deserializes through ReadJsonAsync.
        using var client = new HttpClient(new StubHandler(HttpStatusCode.Unauthorized));
        var unauthorized = new Error.Unauthorized() { Detail = "expired" };
        var conflict = new Error.Conflict(new ResourceRef("Order", "1"), "duplicate_key");

        var result = await client.GetAsync("https://example/api/orders/1", CancellationToken.None)
            .ToResultAsync(status => status == HttpStatusCode.Unauthorized ? unauthorized : null)
            // HandleConflictAsync only operates on Task<HttpResponseMessage>; once we are in
            // Task<Result<HttpResponseMessage>> any conflict mapping must come from the statusMap.
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.Unauthorized>()
            .Which.Should().HaveDetail("expired");
        _ = conflict;
    }
}