// Cookbook Recipe 7 — Authorization: IActorProvider + IAuthorize + resource-based auth.
namespace CookbookSnippets.Recipe07;

using System.Collections.Generic;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using CookbookSnippets.Recipe01;
using Trellis.Mediator;

public sealed record DeleteOrderCommand(OrderId OrderId) : ICommand<Result>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:delete"];
}

public sealed record UpdateOrderCommand(OrderId OrderId, decimal NewAmount)
    : ICommand<Result>, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    // Typed VO carried straight through — no parse, no throw.
    public OrderId GetResourceId() => OrderId;

    public Trellis.IResult Authorize(Actor actor, Order resource) =>
        resource.OwnerId == actor.Id || actor.Permissions.Contains("orders:write")
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden(
                PolicyId: "orders.owner",
                Resource: new ResourceRef("Order", OrderId.Value.ToString())));
}

public static class AuthorizationDi
{
    public static IServiceCollection Wire(IServiceCollection services)
    {
        services.AddTrellisBehaviors();
        services.AddClaimsActorProvider();
        services.AddResourceAuthorization(typeof(UpdateOrderCommand).Assembly);
        return services;
    }
}
