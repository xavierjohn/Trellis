// Cookbook Recipe 5 — MVC controller using AsActionResult.
namespace CookbookSnippets.Recipe05;

using System.Threading;
using System.Threading.Tasks;
using global::Mediator;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Stubs;

[ApiController]
[Route("orders")]
public sealed class OrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(System.Guid id, CancellationToken ct)
    {
        Result<Order> result = await mediator.Send(new GetOrderQuery(id), ct);

        return result
            .ToHttpResponse(
                body: o => new OrderDto(o.Id.Value, o.Total.Amount, o.Total.Currency.Value),
                configure: opts => opts.WithETag(o => o.ETag).EvaluatePreconditions())
            .AsActionResult<OrderDto>();
    }
}

public sealed record OrderDto(System.Guid Id, decimal Amount, string Currency);
