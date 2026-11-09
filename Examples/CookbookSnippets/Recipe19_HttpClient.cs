// Cookbook Recipe 19 — HTTP client result safety and optional reads.
namespace CookbookSnippets.Recipe19;

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Http;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]   // match the upstream's camelCase property names
[JsonSerializable(typeof(OrderDto))]
public sealed partial class OrderJsonContext : JsonSerializerContext;

public sealed record OrderDto(Guid Id, decimal Total);

public static class Recipe19CompiledSurface
{
    public static Task<Result<OrderDto>> GetRequiredOrderAsync(HttpClient client, Guid id, CancellationToken ct) =>
        client.GetAsync($"/orders/{id}", ct)
            .ToResultAsync()
            .ReadJsonAsync(OrderJsonContext.Default.OrderDto, ct);

    public static Task<Result<Maybe<OrderDto>>> FindOrderAsync(HttpClient client, Guid id, CancellationToken ct) =>
        client.GetAsync($"/orders/{id}", ct)
            .ReadJsonOrNoneOn404Async(OrderJsonContext.Default.OrderDto, ct);

    public static Task<Result<OrderDto>> GetRequiredOrderWithDomainNotFoundAsync(HttpClient client, Guid id, CancellationToken ct) =>
        client.GetAsync($"/orders/{id}", ct)
            .ToResultAsync()
            .RecoverOnFailureAsync(e => e is Error.NotFound
                ? Result.Fail<HttpResponseMessage>(new Error.NotFound(ResourceRef.For<OrderDto>(id)))
                : Result.Fail<HttpResponseMessage>(e))
            .ReadJsonAsync(OrderJsonContext.Default.OrderDto, ct);
}

internal static class Recipe19Demonstrator
{
    public static Task<Result<HttpResponseMessage>> StrictToResult(Task<HttpResponseMessage> response) =>
        response.ToResultAsync();

    public static Task<Result<HttpResponseMessage>> StatusMapping(Task<HttpResponseMessage> response) =>
        response.ToResultAsync(status => status == HttpStatusCode.Forbidden
            ? new Error.Forbidden("orders.read")
            : null);

    public static Task<Result<Maybe<OrderDto>>> OptionalRead(Task<HttpResponseMessage> response, CancellationToken ct) =>
        response.ReadJsonOrNoneOn404Async(OrderJsonContext.Default.OrderDto, ct);
}