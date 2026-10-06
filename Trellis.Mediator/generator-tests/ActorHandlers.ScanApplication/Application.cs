namespace ActorHandlers.ScanApplication;

using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed class ScanProbe
{
    public Actor? Actor { get; set; }
    public Actor? ReplacementActor { get; set; }
    public int ProviderCalls { get; set; }
    public int LoaderConstructions { get; set; }
    public int LeafLoads { get; set; }
    public int BusinessCalls { get; set; }
    public int Commits { get; set; }
    public object? Loaded { get; set; }
    public List<string> Operations { get; } = [];
}

public sealed record ScanObservation(Actor Actor, object Resource);

public sealed record ScanResource(string Id, string OwnerId, ScanProbe Probe);
public sealed record ScanOwner(string Id, string OwnerId);
public sealed record ScanDocument(string Id, string FolderId, ScanProbe Probe) : IIdentifyRelatedResource<ScanOwner, string>
{
    public string GetRelatedResourceId() => FolderId;
}

public sealed record ScanCommand(string Id)
    : ICommand<Result<ScanObservation>>, IAuthorize, IAuthorizeResource<ScanResource>, IIdentifyResource<ScanResource, string>
{
    public IReadOnlyList<string> RequiredPermissions => ["write"];
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, ScanResource resource)
    {
        resource.Probe.Operations.Add("authorize");
        return Result.Ensure(actor.IsOwner(resource.OwnerId), () => new Error.Forbidden("scan.owner"));
    }
}

public sealed record ScanQuery(string Id)
    : IQuery<Result<ScanObservation>>, IAuthorizeResource<ScanResource>, IIdentifyResource<ScanResource, string>
{
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, ScanResource resource)
    {
        resource.Probe.Operations.Add("authorize");
        return Result.Ensure(actor.IsOwner(resource.OwnerId), () => new Error.Forbidden("scan.owner"));
    }
}

public sealed record ScanViaCommand(string Id)
    : ICommand<Result<ScanObservation>>, IAuthorize, IAuthorizeResourceVia<ScanOwner>, IIdentifyResource<ScanDocument, string>
{
    public IReadOnlyList<string> RequiredPermissions => ["write"];
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, IReadOnlyList<ScanOwner> owners)
        => Result.Ensure(owners.Any(owner => actor.IsOwner(owner.OwnerId)), () => new Error.Forbidden("scan.owner"));
}

public sealed record ScanViaQuery(string Id)
    : IQuery<Result<ScanObservation>>, IAuthorizeResourceVia<ScanOwner>, IIdentifyResource<ScanDocument, string>
{
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, IReadOnlyList<ScanOwner> owners)
        => Result.Ensure(owners.Any(owner => actor.IsOwner(owner.OwnerId)), () => new Error.Forbidden("scan.owner"));
}

public sealed class ScanActorProvider(ScanProbe probe) : IActorProvider
{
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        probe.ProviderCalls++;
        probe.Operations.Add("actor");
        var actor = probe.ProviderCalls == 1 ? probe.Actor : probe.ReplacementActor;
        return Task.FromResult(actor is null ? Maybe<Actor>.None : Maybe.From(actor));
    }
}

public sealed class ScanCommandHandler(ScanProbe business)
    : ActorResourceCommandHandler<ScanCommand, ScanResource, Result<ScanObservation>>
{
    protected override ValueTask<Result<ScanObservation>> HandleCore(ScanCommand command, Actor actor, ScanResource resource, CancellationToken cancellationToken)
    {
        business.BusinessCalls++;
        business.Operations.Add("business");
        return new(Result.Ok(new ScanObservation(actor, resource)));
    }
}

public sealed class ScanQueryHandler(ScanProbe business)
    : ActorResourceQueryHandler<ScanQuery, ScanResource, Result<ScanObservation>>
{
    protected override ValueTask<Result<ScanObservation>> HandleCore(ScanQuery query, Actor actor, ScanResource resource, CancellationToken cancellationToken)
    {
        business.BusinessCalls++;
        business.Operations.Add("business");
        return new(Result.Ok(new ScanObservation(actor, resource)));
    }
}

public sealed class ScanViaCommandHandler(ScanProbe business)
    : ActorResourceViaCommandHandler<ScanViaCommand, ScanDocument, ScanOwner, Result<ScanObservation>>
{
    protected override ValueTask<Result<ScanObservation>> HandleCore(ScanViaCommand command, Actor actor, ScanDocument leaf, CancellationToken cancellationToken)
    {
        business.BusinessCalls++;
        business.Operations.Add("business");
        return new(Result.Ok(new ScanObservation(actor, leaf)));
    }
}

public sealed class ScanViaQueryHandler(ScanProbe business)
    : ActorResourceViaQueryHandler<ScanViaQuery, ScanDocument, ScanOwner, Result<ScanObservation>>
{
    protected override ValueTask<Result<ScanObservation>> HandleCore(ScanViaQuery query, Actor actor, ScanDocument leaf, CancellationToken cancellationToken)
    {
        business.BusinessCalls++;
        business.Operations.Add("business");
        return new(Result.Ok(new ScanObservation(actor, leaf)));
    }
}

public static class ScanComposition
{
    public static IServiceCollection AddScanMediator(this IServiceCollection services)
        => services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
}
