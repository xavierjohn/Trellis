namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        var result = await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);

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

        var result = await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);

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

        var result = await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);

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

        var result = await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("ViaLeaf");
        notFound.Resource.Id.Should().Be("leaf-1");
    }

    [Fact]
    public async Task Handle_HideExistenceOnLeaf_LeafLoadNotFound_UsesCanonicalLeafError()
    {
        var original = new Error.NotFound(ResourceRef.For("InternalLeaf", "database-id"))
        {
            Code = "leaf.missing",
            Detail = "Internal leaf database-id was not found.",
            Cause = new Error.Forbidden("leaf.partition-hidden"),
        };
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaLeaf>();
        var behavior = CreateBehavior("actor-1", leaf: null, ownerRepo, options, original);
        var command = new ViaSingleHopCommand("missing-leaf");
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Should().NotBeSameAs(original);
        notFound.Resource.Should().Be(ResourceRef.For<ViaLeaf>("missing-leaf"));
        notFound.Code.Should().Be("error.unspecified");
        notFound.Detail.Should().BeNull();
        notFound.Cause.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Propagate_LeafLoadNotFound_PreservesOriginalError()
    {
        var original = Error.NotFound.For<ViaLeaf>("leaf.missing", id: "missing-leaf", detail: "Detailed leaf failure.");
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id);
        var behavior = CreateBehavior("actor-1", null, ownerRepo, new ResourceAuthorizationOptions(), original);
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.HandleWithContext(
            new ViaSingleHopCommand("missing-leaf"), next, TestContext.Current.CancellationToken);

        result.UnwrapError().Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_HideExistenceOnLeaf_LeafOperationalFailure_PreservesOriginalError(bool unavailable)
    {
        Error original = unavailable
            ? new Error.Unavailable { Code = "leaf.unavailable" }
            : new Error.Unexpected("leaf.failed", "fault-42");
        var ownerRepo = new InMemoryRepo<ViaOwner>(o => o.Id);
        var options = new ResourceAuthorizationOptions().HideExistence<ViaLeaf>();
        var behavior = CreateBehavior("actor-1", null, ownerRepo, options, original);
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.HandleWithContext(
            new ViaSingleHopCommand("leaf-1"), next, TestContext.Current.CancellationToken);

        result.UnwrapError().Should().BeSameAs(original);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("denied", false)]
    [InlineData("anonymous", false)]
    [InlineData("owner-missing", false)]
    [InlineData("missing", true)]
    [InlineData("denied", true)]
    [InlineData("anonymous", true)]
    [InlineData("owner-missing", true)]
    public async Task Handle_PublicMetadata_FailureSources_UseCanonicalLeafError(string source, bool projection)
    {
        var options = new ResourceAuthorizationOptions();
        if (projection)
            options.HideExistence<ViaLeaf, PublicLeaf>("leaf.not-found", "Leaf not found.");
        else
            options.HideExistence<ViaLeaf>("leaf.not-found", "Leaf not found.");
        var leaf = new ViaLeaf("leaf-1", "owner-1");
        var ownerRepo = source == "owner-missing"
            ? new InMemoryRepo<ViaOwner>(owner => owner.Id)
            : new InMemoryRepo<ViaOwner>(owner => owner.Id, new ViaOwner("owner-1", "someone-else"));
        var leafError = source == "missing"
            ? Error.NotFound.For<ViaOwner>("leaf.missing", id: "internal-id", detail: "Private leaf detail.")
            : null;
        var behavior = CreateBehavior(source == "anonymous" ? null : "actor-1", leaf, ownerRepo, options, leafError);
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.HandleWithContext(
            new ViaSingleHopCommand("leaf-1"), next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Should().Be(ResourceRef.For(projection ? nameof(PublicLeaf) : nameof(ViaLeaf), "leaf-1"));
        notFound.Code.Should().Be("leaf.not-found");
        notFound.Detail.Should().Be("Leaf not found.");
        notFound.Cause.Should().BeNull();
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("forbidden")]
    [InlineData("authentication-required")]
    public async Task Handle_HideAsNotFound_PrivateDiagnostics_RetainOriginalKindAndCode(string kind)
    {
        Error original = kind switch
        {
            "not-found" => Error.NotFound.For<ViaOwner>("private.missing", id: "internal-id"),
            "forbidden" => new Error.Forbidden("private.denied"),
            _ => new Error.AuthenticationRequired { Code = "private.unauthenticated" },
        };
        var logger = new CapturingLogger<ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>>();
        var options = new ResourceAuthorizationOptions()
            .HideExistence<ViaLeaf>("leaf.not-found", "Leaf not found.");
        var ownerRepo = new InMemoryRepo<ViaOwner>(owner => owner.Id);
        var behavior = CreateBehavior("actor-1", null, ownerRepo, options, original, logger);
        var (next, _) = NextDelegate.TrackingAsync<ViaSingleHopCommand, Result<string>>(Result.Ok("nope"));

        var result = await behavior.HandleWithContext(
            new ViaSingleHopCommand("leaf-1"), next, TestContext.Current.CancellationToken);

        result.UnwrapError().Code.Should().Be("leaf.not-found");
        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries.Single();
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Id.Should().Be(1);
        entry.EventId.Name.Should().Be("ExistenceHidden");
        var fields = entry.State.Should()
            .BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Which;
        fields.Should().Contain(new KeyValuePair<string, object?>("OriginalKind", original.Kind));
        fields.Should().Contain(new KeyValuePair<string, object?>("OriginalCode", original.Code));
        fields.Should().Contain(new KeyValuePair<string, object?>("PublicResourceType", nameof(ViaLeaf)));
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

        var result = await behavior.HandleWithContext(command, next, TestContext.Current.CancellationToken);

        var notFound = result.UnwrapError().Should().BeOfType<Error.NotFound>().Subject;
        notFound.Resource.Type.Should().Be("ViaLeaf");
    }

    public sealed record ViaLeaf(string Id, string OwnerId)
        : IIdentifyRelatedResource<ViaOwner, string>
    {
        public string GetRelatedResourceId() => OwnerId;
    }

    public sealed record ViaOwner(string Id, string CreatedByActorId);

    private sealed record PublicLeaf;

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

    private sealed class FakeLeafLoader(ViaLeaf? leaf, Error? error) : IResourceLoader<ViaSingleHopCommand, ViaLeaf>
    {
        public Task<Result<ViaLeaf>> LoadAsync(ViaSingleHopCommand message, CancellationToken cancellationToken)
            => Task.FromResult(error is not null
                ? Result.Fail<ViaLeaf>(error)
                : leaf is not null
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
            ResourceAuthorizationOptions options,
            Error? leafError = null,
            ILogger<ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>>? logger = null)
    {
        IActorProvider actorProvider = actorId is null
            ? FakeActorProvider.Anonymous()
            : FakeActorProvider.NoPermissions(actorId);

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ViaSingleHopCommand, ViaLeaf>>(_ => new FakeLeafLoader(leaf, leafError));
        var sp = services.BuildServiceProvider();

        var path = BuildLeafToOwnerPath(ownerRepo);

        return new ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>(
            actorProvider,
            sp,
            path,
            Options.Create(options),
            logger: logger ?? NullLogger<ResourceAuthorizationViaBehavior<ViaSingleHopCommand, ViaLeaf, ViaOwner, Result<string>>>.Instance);
    }
}