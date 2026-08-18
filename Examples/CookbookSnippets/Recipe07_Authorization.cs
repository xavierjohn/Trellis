// Cookbook Recipe 7 — Authorization: IActorProvider + IAuthorize + resource-based auth.
namespace CookbookSnippets.Recipe07;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CookbookSnippets.Recipe01;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record DeleteOrderCommand(OrderId OrderId) : ICommand<Result<Trellis.Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["orders:delete"];
}

public sealed record UpdateOrderCommand(OrderId OrderId, decimal NewAmount)
    : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<Order>, IIdentifyResource<Order, OrderId>
{
    // Typed VO carried straight through — no parse, no throw.
    public OrderId GetResourceId() => OrderId;

    public Trellis.IResult Authorize(Actor actor, Order resource) =>
        resource.OwnerId == actor.Id || actor.Permissions.Contains("orders:write")
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden(
                PolicyId: "orders.owner",
                Resource: ResourceRef.For<Order>(OrderId)));
}

public static class AuthorizationDi
{
    public static IServiceCollection Wire(IServiceCollection services)
    {
        services.AddTrellisBehaviors();
        services.AddClaimsActorProvider();
        // Pass every assembly that holds command/query types AND every assembly that holds
        // IResourceLoader<,> implementations (typically the ACL assembly). The scanner
        // de-duplicates the assemblies internally (first-seen order preserved), so passing the
        // same assembly twice is harmless — ResourceAuthorizationBehavior is registered once per
        // request. In a real layered app these are two distinct assemblies; the demonstrator
        // packs everything into CookbookSnippets, so the .Distinct() below is just demo tidiness.
        Assembly applicationAssembly = typeof(UpdateOrderCommand).Assembly;
        Assembly aclAssembly = typeof(AuthorizationDi).Assembly; // same assembly in this demo
        Assembly[] scanAssemblies = new[] { applicationAssembly, aclAssembly }
            .Distinct()
            .ToArray();
        services.AddResourceAuthorization(scanAssemblies);
        return services;
    }
}
internal static class Recipe7AuthorizationSurface
{
    public static void AuthorizationBehavior_RegistrationSurface(IServiceCollection services, DeleteOrderCommand command)
    {
        services.AddTrellisBehaviors();
        Type behaviorType = typeof(AuthorizationBehavior<,>);
        IReadOnlyList<string> permissions = command.RequiredPermissions;

        _ = (behaviorType, permissions);
    }

    public static async Task IResourceLoader_LoadAsyncSignature(UpdateOrderCommand command, CancellationToken cancellationToken)
    {
        IResourceLoader<UpdateOrderCommand, Order> loader = new PerCommandOrderLoader();
        Result<Order> loaded = await loader.LoadAsync(command, cancellationToken);

        _ = loaded;
    }

    public static async Task SharedResourceLoader_ByIdSurface(UpdateOrderCommand command, CancellationToken cancellationToken)
    {
        Type identifyResourceType = typeof(IIdentifyResource<Order, OrderId>);
        OrderId id = command.GetResourceId();

        SharedResourceLoaderById<Order, OrderId> loader = new SharedOrderLoader();
        Result<Order> loaded = await loader.GetByIdAsync(id, cancellationToken);

        _ = (identifyResourceType, loaded);
    }

    private sealed class PerCommandOrderLoader : IResourceLoader<UpdateOrderCommand, Order>
    {
        public Task<Result<Order>> LoadAsync(UpdateOrderCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Order>(message.OrderId))));
    }

    private sealed class SharedOrderLoader : SharedResourceLoaderById<Order, OrderId>
    {
        public override Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Order>(id))));
    }
}