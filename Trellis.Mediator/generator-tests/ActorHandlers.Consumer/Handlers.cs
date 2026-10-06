#if GENERATED_PIPELINE
namespace ActorHandlers.GeneratedPipeline;
#else
namespace ActorHandlers.Consumer;
#endif

using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

internal sealed record Observation(Actor Actor, object? Resource, CancellationToken Token);

internal sealed class DispatchProbe(Actor expectedActor)
{
    internal Actor ExpectedActor { get; } = expectedActor;
    internal Actor? CheckedActor { get; set; }
    internal object? Loaded { get; set; }
    internal int Loads { get; set; }
    internal int BusinessCalls { get; set; }
    internal Func<CancellationToken, Task>? BeforeLoad { get; set; }

    internal IResult Authorize(Actor actor, string ownerId)
    {
        CheckedActor = actor;
        return Result.Ensure(actor.IsOwner(ownerId), () => new Error.Forbidden("owner.required"));
    }
}

internal sealed record AuditCommand(DispatchProbe Probe,
    Func<Actor, CancellationToken, ValueTask<Result<Observation>>>? Body = null) : ICommand<Result<Observation>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["write"];
}

internal sealed record ActorQuery(DispatchProbe Probe) : IQuery<Result<Observation>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["read"];
}

internal interface IOrderMessage
{
    string Id { get; }
    DispatchProbe Probe { get; }
}

internal sealed record Order(string Id, string OwnerId);

internal sealed record UpdateOrderCommand(string Id, DispatchProbe Probe)
    : ICommand<Result<Observation>>, IAuthorize, IAuthorizeResource<Order>, IIdentifyResource<Order, string>, IOrderMessage
{
    public IReadOnlyList<string> RequiredPermissions => ["write"];
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, Order resource) => Probe.Authorize(actor, resource.OwnerId);
}

internal sealed record OrderQuery(string Id, DispatchProbe Probe)
    : IQuery<Result<Observation>>, IAuthorizeResource<Order>, IIdentifyResource<Order, string>, IOrderMessage
{
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, Order resource) => Probe.Authorize(actor, resource.OwnerId);
}

internal sealed record MultiResourceCommand(string Id, DispatchProbe Probe, DispatchProbe FolderProbe)
    : ICommand<Result<Observation>>, IAuthorizeResource<Order>, IAuthorizeResource<Folder>,
      IIdentifyResource<Order, string>, IOrderMessage
{
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, Order resource) => Probe.Authorize(actor, resource.OwnerId);
    public IResult Authorize(Actor actor, Folder resource) => FolderProbe.Authorize(actor, resource.OwnerId);
}

internal interface IDocumentMessage
{
    string Id { get; }
    DispatchProbe Probe { get; }
}

internal sealed record Document(string Id, string FolderId);
internal sealed record Folder(string Id, string OwnerId);

internal sealed record UpdateDocumentCommand(string Id, DispatchProbe Probe)
    : ICommand<Result<Observation>>, IAuthorize, IAuthorizeResourceVia<Folder>, IIdentifyResource<Document, string>, IDocumentMessage
{
    public IReadOnlyList<string> RequiredPermissions => ["write"];
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, IReadOnlyList<Folder> owners) => Probe.Authorize(actor, owners[0].OwnerId);
}

internal sealed record DocumentQuery(string Id, DispatchProbe Probe)
    : IQuery<Result<Observation>>, IAuthorizeResourceVia<Folder>, IIdentifyResource<Document, string>, IDocumentMessage
{
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, IReadOnlyList<Folder> owners) => Probe.Authorize(actor, owners[0].OwnerId);
}

internal sealed record AccessorQuery(string Id, DispatchProbe Probe)
    : IQuery<Result<Observation>>, IAuthorizeResource<Order>, IIdentifyResource<Order, string>, IOrderMessage
{
    public string GetResourceId() => Id;
    public IResult Authorize(Actor actor, Order resource) => Probe.Authorize(actor, resource.OwnerId);
}

internal sealed class AuditWriter
{
    private int _entries;
    internal int Entries => Volatile.Read(ref _entries);

    internal Result<Observation> Record(DispatchProbe probe, Actor actor, object? resource, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _entries);
        probe.BusinessCalls++;
        return Result.Ok(new Observation(actor, resource, token));
    }
}

internal sealed class AuditCommandHandler(AuditWriter audit) : ActorCommandHandler<AuditCommand, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(AuditCommand command, Actor actor, CancellationToken cancellationToken)
        => command.Body is { } body ? body(actor, cancellationToken) : new(audit.Record(command.Probe, actor, null, cancellationToken));
}

internal sealed class ActorQueryHandler(AuditWriter audit) : ActorQueryHandler<ActorQuery, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(ActorQuery query, Actor actor, CancellationToken cancellationToken)
        => new(audit.Record(query.Probe, actor, null, cancellationToken));
}

internal sealed class UpdateOrderHandler(AuditWriter audit) : ActorResourceCommandHandler<UpdateOrderCommand, Order, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(UpdateOrderCommand command, Actor actor, Order resource, CancellationToken cancellationToken)
        => new(audit.Record(command.Probe, actor, resource, cancellationToken));
}

internal sealed class OrderQueryHandler(AuditWriter audit) : ActorResourceQueryHandler<OrderQuery, Order, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(OrderQuery query, Actor actor, Order resource, CancellationToken cancellationToken)
        => new(audit.Record(query.Probe, actor, resource, cancellationToken));
}

internal sealed class MultiResourceCommandHandler(AuditWriter audit)
    : ActorResourceCommandHandler<MultiResourceCommand, Order, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(
        MultiResourceCommand command, Actor actor, Order resource, CancellationToken cancellationToken)
        => new(audit.Record(command.Probe, actor, resource, cancellationToken));
}

internal sealed class UpdateDocumentHandler(AuditWriter audit) : ActorResourceViaCommandHandler<UpdateDocumentCommand, Document, Folder, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(UpdateDocumentCommand command, Actor actor, Document leaf, CancellationToken cancellationToken)
        => new(audit.Record(command.Probe, actor, leaf, cancellationToken));
}

internal sealed class DocumentQueryHandler(AuditWriter audit) : ActorResourceViaQueryHandler<DocumentQuery, Document, Folder, Result<Observation>>
{
    protected override ValueTask<Result<Observation>> HandleCore(DocumentQuery query, Actor actor, Document leaf, CancellationToken cancellationToken)
        => new(audit.Record(query.Probe, actor, leaf, cancellationToken));
}

internal sealed class AccessorQueryHandler(IAuthorizedResource<AccessorQuery, Order> authorized)
    : IQueryHandler<AccessorQuery, Result<Observation>>
{
    public ValueTask<Result<Observation>> Handle(AccessorQuery query, CancellationToken cancellationToken)
    {
        query.Probe.BusinessCalls++;
        return new(Result.Ok(new Observation(query.Probe.CheckedActor
            ?? throw new InvalidOperationException("The accessor handler was not authorized."),
            authorized.GetRequiredResource(), cancellationToken)));
    }
}

internal sealed class OrderLoader<TMessage> : IResourceLoader<TMessage, Order> where TMessage : IOrderMessage
{
    public async Task<Result<Order>> LoadAsync(TMessage message, CancellationToken cancellationToken)
    {
        if (message.Probe.BeforeLoad is { } wait)
            await wait(cancellationToken).ConfigureAwait(false);
        message.Probe.Loads++;
        var order = new Order(message.Id, message.Probe.ExpectedActor.Id.Value);
        message.Probe.Loaded = order;
        return Result.Ok(order);
    }
}

internal sealed class DocumentLoader<TMessage> : IResourceLoader<TMessage, Document> where TMessage : IDocumentMessage
{
    public Task<Result<Document>> LoadAsync(TMessage message, CancellationToken cancellationToken)
    {
        message.Probe.Loads++;
        var document = new Document(message.Id, "folder");
        message.Probe.Loaded = document;
        return Task.FromResult(Result.Ok(document));
    }
}

internal sealed class FolderLoader : SharedResourceLoaderById<Folder, string>
{
    public override Task<Result<Folder>> GetByIdAsync(string id, CancellationToken cancellationToken)
        => Task.FromResult(Result.Ok(new Folder(id, "owner")));
}

internal sealed class CommandFolderLoader : IResourceLoader<MultiResourceCommand, Folder>
{
    public Task<Result<Folder>> LoadAsync(MultiResourceCommand message, CancellationToken cancellationToken)
    {
        message.FolderProbe.Loads++;
        var folder = new Folder("folder", message.FolderProbe.ExpectedActor.Id.Value);
        message.FolderProbe.Loaded = folder;
        return Task.FromResult(Result.Ok(folder));
    }
}
