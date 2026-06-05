namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="AuthFailureExposurePolicy"/> translation logic in
/// <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/>.
/// Spec invariant: the synthesised <c>Error.NotFound.ResourceRef</c> references
/// <c>TLeaf</c> (the resource the command identifies), never <c>TOwner</c>.
/// </summary>
public sealed class ResourceAuthorizationViaBehaviorExposurePolicyTests
{
    [Fact]
    public async Task Handle_DefaultPropagate_OwnerMismatchSurfacesForbidden()
    {
        var leaf = new ViaLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new ViaOwner("owner-1", CreatedByActorId: "someone-else");
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id, owner);
        var behavior = CreateBehavior("actor-1", leaf, ownerRepo, new ResourceAuthorizationOptions());
        var command = new ViaSingleHopCommand("leaf-1");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
    }

    [Fact]
    public async Task Handle_HideExistenceOnLeaf_OwnerMismatchTranslatesToNotFoundReferencingLeaf()
    {
        // Spec invariant: translated NotFound references TLeaf, never TOwner.
        var leaf = new ViaLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new ViaOwner("owner-1", CreatedByActorId: "someone-else");
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id, owner);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaLeaf>();
        var behavior = CreateBehavior("actor-1", leaf, ownerRepo, options);
        var command = new ViaSingleHopCommand("leaf-1");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("ViaLeaf");
        notFound.Resource.Id.Should().Be("leaf-1");
    }

    [Fact]
    public async Task Handle_HideExistenceOnOwner_DoesNotTrigger_LookupKeyIsLeaf()
    {
        // Documenting that the via-path lookup key is TLeaf, not TOwner. A consumer who
        // mistakenly opts the owner into hide-existence sees the Forbidden propagated.
        var leaf = new ViaLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new ViaOwner("owner-1", CreatedByActorId: "someone-else");
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id, owner);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaOwner>();
        var behavior = CreateBehavior("actor-1", leaf, ownerRepo, options);
        var command = new ViaSingleHopCommand("leaf-1");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.UnwrapError().Should().BeOfType<Error.Forbidden>(
            "via-path policy lookup is keyed on TLeaf — opting the owner in is a no-op");
    }

    [Fact]
    public async Task Handle_HideExistenceOnLeaf_OwnerHopLoadFailureTranslatesToNotFoundReferencingLeaf()
    {
        // The synthetic Forbidden produced by an owner-hop load failure must also flow
        // through translation under HideAsNotFound — the leak is the same: existence of
        // the leaf is being inferred from the failure shape.
        var leaf = new ViaLeaf("leaf-1", OwnerId: "missing-owner");
        // Owner repo has no entry for "missing-owner" → repo returns NotFound → hop collapses to Forbidden.
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaLeaf>();
        var behavior = CreateBehavior("actor-1", leaf, ownerRepo, options);
        var command = new ViaSingleHopCommand("leaf-1");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("ViaLeaf");
        notFound.Resource.Id.Should().Be("leaf-1");
    }

    [Fact]
    public async Task Handle_HideExistenceOnLeaf_LeafLoadNotFoundPassesThroughUnchanged()
    {
        // Leaf-load NotFound is NOT in the translation set — pass through verbatim.
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaLeaf>();
        var behavior = CreateBehavior("actor-1", leaf: null, ownerRepo, options);
        var command = new ViaSingleHopCommand("missing-leaf");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        // Loader's NotFound carries TLeaf's typeof().Name shape ("ViaLeaf"), but importantly the
        // error reference equality contrast: a translated synthetic NotFound would have come from
        // our MaybeTranslateExposure. Loader-originated NotFound flows through.
        notFound.Resource.Type.Should().Be("ViaLeaf");
    }

    [Fact]
    public async Task Handle_HideExistenceOnLeaf_AuthenticationRequiredTranslatesToNotFoundReferencingLeaf()
    {
        var leaf = new ViaLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new ViaOwner("owner-1", CreatedByActorId: "actor-1");
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id, owner);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaLeaf>();
        var behavior = CreateBehavior(actorId: null, leaf, ownerRepo, options);
        var command = new ViaSingleHopCommand("leaf-1");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("ViaLeaf");
    }

    public sealed record ViaLeaf(string Id, string OwnerId)
        : IIdentifyRelatedResource<ViaOwner, string>
    {
        public string GetRelatedResourceId() => OwnerId;
    }

    public sealed record ViaOwner(string Id, string CreatedByActorId);

    public sealed record ViaSingleHopCommand(string LeafId)
        : global::Mediator.ICommand<Result<string>>,
          IAuthorizeResourceVia<ViaOwner>,
          IIdentifyResource<ViaLeaf, string>
    {
        public string GetResourceId() => LeafId;

        public IResult Authorize(Actor actor, IReadOnlyList<ViaOwner> owners) =>
            owners.Any(o => o.CreatedByActorId == actor.Id)
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("via.not-owner"));
    }

    private sealed class InMemoryRepo<T>(Func<T, string> idSelector, params T[] items)
        where T : class
    {
        private readonly Dictionary<string, T> _items = items.ToDictionary(idSelector);

        public Result<T> GetById(string id) =>
            _items.TryGetValue(id, out var v)
                ? Result.Ok(v)
                : Result.Fail<T>(new Error.NotFound(new ResourceRef(typeof(T).Name, id)));
    }

    private sealed class FakeLeafLoader(ViaLeaf? leaf) : IResourceLoader<ViaSingleHopCommand, ViaLeaf>
    {
        public Task<Result<ViaLeaf>> LoadAsync(ViaSingleHopCommand message, CancellationToken cancellationToken)
            => Task.FromResult(leaf is not null
                ? Result.Ok(leaf)
                : Result.Fail<ViaLeaf>(new Error.NotFound(new ResourceRef(typeof(ViaLeaf).Name, null))));
    }

    private static ResolvedAuthorizationPath BuildLeafToOwnerPath(InMemoryRepo<ViaOwner> ownerRepo)
    {
        var hop = new ResolvedAuthorizationHop(
            fromType: typeof(ViaLeaf),
            toType: typeof(ViaOwner),
            toIdType: typeof(string),
            extractIds: src => [((ViaLeaf)src).OwnerId],
            loadAsync: (_, id, _) =>
            {
                var r = ownerRepo.GetById((string)id);
                return Task.FromResult(r.TryGetValue(out var v, out var err)
                    ? HopLoadResult.Success(v)
                    : HopLoadResult.Failure(err));
            },
            isPlural: false);

        return new ResolvedAuthorizationPath(
            messageType: typeof(ViaSingleHopCommand),
            leafType: typeof(ViaLeaf),
            ownerType: typeof(ViaOwner),
            hops: [hop]);
    }

    private static ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>
        CreateBehavior(
            string? actorId,
            ViaLeaf? leaf,
            InMemoryRepo<ViaOwner> ownerRepo,
            ResourceAuthorizationOptions options)
    {
        IActorProvider actorProvider = actorId is null
            ? FakeActorProvider.Anonymous()
            : FakeActorProvider.NoPermissions(actorId);

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ViaSingleHopCommand, ViaLeaf>>(_ => new FakeLeafLoader(leaf));
        var sp = services.BuildServiceProvider();

        var path = BuildLeafToOwnerPath(ownerRepo);

        return new ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>(
            actorProvider,
            sp,
            path,
            Options.Create(options),
            logger: NullLogger<ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>>.Instance);
    }
}
