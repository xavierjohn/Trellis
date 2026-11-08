---
name: recipe-05-mvc-controller
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows an MVC controller action that returns a Trellis Result through AsActionResult. Use when adding or converting an MVC controller endpoint."
---
# Recipe 5 — MVC controller using `AsActionResult`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`.

**Problem.** Same payload as Recipe 4 but with a typed MVC `ActionResult<OrderDto>`.

```csharp
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

[ApiController]
[Route("orders")]
public sealed class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
    {
        if (!OrderId.TryCreate(id, nameof(id)).TryGetValue(out var orderId, out var idError))
            return idError.ToHttpResponse().AsActionResult<OrderDto>();

        Result<Order> result = await mediator.Send(new GetOrderQuery(orderId), ct);

        return result
            .ToHttpResponse(
                body: o => new OrderDto(o.Id.Value, o.Total.Amount, o.Total.Currency.Value),
                configure: opts => opts.WithETag(o => o.ETag).EvaluatePreconditions())
            .AsActionResult<OrderDto>();
    }
}

public sealed record OrderDto(Guid Id, decimal Amount, string Currency);
```

**What it shows.** `.AsActionResult<TBody>()` projects an `IResult` into a typed `ActionResult<TBody>`, so MVC clients still get OpenAPI/Swagger-friendly typed responses while the response itself executes through the same `IResult` pipeline as Minimal API.

The same adapter supports the Created/Updated outcomes in Recipe 4. For
`Result<WriteOutcome<Order>>`, project the body with `ToHttpResponse(...)` and chain
`.AsActionResult<OrderDto>()`; route/action location fallbacks run against the Order,
not the projected DTO. A nonblank outcome Location takes precedence.
