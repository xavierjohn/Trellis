---
name: recipe-19-http-client
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Covers HttpClient result safety and optional reads where a 404 means absent. Use when calling HTTP services from handlers."
---
# Recipe 19 — HTTP client result safety and optional reads

**Requires:** `Trellis.Core`, plus `Trellis.Http`.

**Problem.** Call an upstream HTTP resource safely, preserving non-success status codes as Trellis errors and treating a missing optional resource as `Maybe.None`.

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]   // match the upstream's camelCase property names
[JsonSerializable(typeof(OrderDto))]
public sealed partial class OrderJsonContext : JsonSerializerContext;

public sealed record OrderDto(Guid Id, decimal Total);

public Task<Result<OrderDto>> GetRequiredOrderAsync(HttpClient client, Guid id, CancellationToken ct) =>
    client.GetAsync($"/orders/{id}", ct)
        .ToResultAsync()
        .ReadJsonAsync(OrderJsonContext.Default.OrderDto, ct);

public Task<Result<Maybe<OrderDto>>> FindOrderAsync(HttpClient client, Guid id, CancellationToken ct) =>
    client.GetAsync($"/orders/{id}", ct)
        .ReadJsonOrNoneOn404Async(OrderJsonContext.Default.OrderDto, ct);
```

**What it shows.**

- Bare `ToResultAsync()` is strict: 2xx responses stay on the success track; non-2xx responses become typed Trellis errors.
- The JSON context must match the upstream's property naming. Without `JsonSerializerDefaults.Web`, a camelCase body deserializes to a record of default values and the call still succeeds.
- Use `ReadJsonOrNoneOn404Async(...)` when `404` is expected domain absence, not failure. It returns `None` for 404, 204, 205, an empty body and a JSON `null`; other non-2xx statuses fail with the same typed errors as bare `ToResultAsync()`.
- A supplied status map replaces the strict mapping: statuses for which it returns `null` stay on the success track, and a later `ReadJsonAsync` reports them as a generic `Error.Unexpected` (code `http.response-not-success`), losing the typed 401, 403, 429 and 503 errors. To keep the strict errors for every other status, use `.ToResultAsync()` and handle the one expected status separately (for example with `HandleNotFoundAsync(new Error.NotFound(...))`), or return an explicit error for every status the map should cover. A map is for when the upstream status needs a domain-specific resource or policy:

```csharp
client.GetAsync($"/orders/{id}", ct)
    .ToResultAsync(status => status == HttpStatusCode.NotFound
        ? new Error.NotFound(ResourceRef.For<OrderDto>(id))
        : null)
    .ReadJsonAsync(OrderJsonContext.Default.OrderDto, ct);
```
