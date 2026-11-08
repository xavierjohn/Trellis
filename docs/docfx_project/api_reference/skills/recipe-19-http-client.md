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
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

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

- Bare `ToResultAsync()` is strict in v3: 2xx responses stay on the success track; non-2xx responses become typed Trellis errors.
- Use `ReadJsonOrNoneOn404Async(...)` when `404` is expected domain absence, not failure.
- Use explicit status mapping only when the upstream status needs a domain-specific resource or policy:

```csharp
client.GetAsync($"/orders/{id}", ct)
    .ToResultAsync(status => status == HttpStatusCode.NotFound
        ? new Error.NotFound(ResourceRef.For<OrderDto>(id))
        : null)
    .ReadJsonAsync(OrderJsonContext.Default.OrderDto, ct);
```
