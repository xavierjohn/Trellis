namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/>.
/// Drives the indirect (multi-hop) resource authorization design via TDD, starting from
/// a single-hop singular path and growing to plural-terminal (cricket fan-out).
/// </summary>
public class ResourceAuthorizationViaBehaviorTests
{
    #region Single-hop, singular terminal — happy path

    [Fact]
    public async Task Handle_SingleHopSingular_ActorOwnsOwner_CallsNextAndReturnsSuccess()
    {
        var leaf = new TestLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new TestOwner("owner-1", CreatedByActorId: "actor-1");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id, owner);

        var behavior = CreateBehavior<SingleHopCommand>(
            actorId: "actor-1",
            leaf: leaf,
            ownerRepo: ownerRepo);

        var command = new SingleHopCommand("leaf-1");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("Done");
        tracker.WasInvoked.Should().BeTrue();
    }

    #endregion

    #region Single-hop, singular terminal — failure modes

    [Fact]
    public async Task Handle_SingleHopSingular_ActorIsNotOwner_ReturnsForbiddenAndDoesNotCallHandler()
    {
        var leaf = new TestLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new TestOwner("owner-1", CreatedByActorId: "someone-else");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id, owner);

        var behavior = CreateBehavior<SingleHopCommand>("actor-1", leaf, ownerRepo);
        var command = new SingleHopCommand("leaf-1");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        result.UnwrapError().Code.Should().Be("not_owner");
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LeafNotFound_BubblesLoaderErrorUnchanged()
    {
        // Leaf miss = the resource the command identifies is missing.
        // Existing zero-hop semantics: surface the loader's error verbatim.
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id);

        var behavior = CreateBehavior<SingleHopCommand>(
            "actor-1", leaf: null, ownerRepo: ownerRepo);

        var command = new SingleHopCommand("missing-leaf");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.NotFound>();
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_IntermediateOwnerNotFound_CollapsesToForbidden()
    {
        // Leaf loaded successfully, but the related owner is missing.
        // Per design, surface as Forbidden (not NotFound) to avoid leaking existence
        // of related resources the actor may not be authorized to learn about.
        var leaf = new TestLeaf("leaf-1", OwnerId: "owner-missing");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id);  // empty

        var behavior = CreateBehavior<SingleHopCommand>("actor-1", leaf, ownerRepo);

        var command = new SingleHopCommand("leaf-1");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var error = result.UnwrapError();
        error.Should().BeOfType<Error.Forbidden>();
        error.Code.Should().Be("resource.authorization-via.load-failed");
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LeafLoaderReturnsSuccessWithNullPayload_CollapsesToForbidden()
    {
        // Defense-in-depth: a leaf loader that violates its Result<T> contract by returning
        // Result.Ok carrying a null value must NOT crash the pipeline (NullReferenceException
        // when ExtractIds casts the leaf to TLeaf or accesses its members) and bubble as 500.
        // The pipeline must fail closed with a Forbidden result.
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id);

        var actorProvider = FakeActorProvider.NoPermissions("actor-1");
        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<SingleHopCommand, TestLeaf>>(_ => new NullPayloadLeafLoader<SingleHopCommand, TestLeaf>());
        var sp = services.BuildServiceProvider();
        var path = BuildLeafToOwnerPath(ownerRepo);

        var behavior = new ResourceAuthorizationViaBehavior<SingleHopCommand, TestLeaf, TestOwner, Result<string>>(
            actorProvider, sp, path);

        var command = new SingleHopCommand("any-leaf");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        var error = result.UnwrapError();
        error.Should().BeOfType<Error.Forbidden>();
        error.Code.Should().Be("resource.authorization-via.null-payload");
        tracker.WasInvoked.Should().BeFalse();
    }

    private sealed class NullPayloadLeafLoader<TMsg, TLeaf> : IResourceLoader<TMsg, TLeaf>
        where TLeaf : class
    {
        public Task<Result<TLeaf>> LoadAsync(TMsg message, CancellationToken cancellationToken)
            => Task.FromResult(Result.Ok<TLeaf>(null!));
    }

    #endregion

    #region Plural terminal — cricket fan-out (Match -> {Team1, Team2}, OR-ownership)

    [Fact]
    public async Task Handle_PluralTerminal_ActorOwnsTeam1_CallsNextAndReturnsSuccess()
    {
        // Cricket: Match has Team1 + Team2; actor owns Team1; OR-ownership succeeds.
        var match = new CricketMatch("match-1", Team1Id: "team-1", Team2Id: "team-2");
        var team1 = new TestTeam("team-1", CreatedByActorId: "actor-1");
        var team2 = new TestTeam("team-2", CreatedByActorId: "rival-actor");
        var teamRepo = new InMemoryRepo<TestTeam>(t => t.Id, team1, team2);

        var behavior = CreateCricketBehavior(
            actorId: "actor-1", match: match, teamRepo: teamRepo);

        var command = new UploadScorecardCommand("match-1");
        var (next, tracker) = NextDelegate.TrackingAsync<UploadScorecardCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PluralTerminal_ActorOwnsTeam2_CallsNextAndReturnsSuccess()
    {
        // OR-ownership succeeds via the away team.
        var match = new CricketMatch("match-1", Team1Id: "team-1", Team2Id: "team-2");
        var team1 = new TestTeam("team-1", CreatedByActorId: "rival-actor");
        var team2 = new TestTeam("team-2", CreatedByActorId: "actor-1");
        var teamRepo = new InMemoryRepo<TestTeam>(t => t.Id, team1, team2);

        var behavior = CreateCricketBehavior("actor-1", match, teamRepo);
        var command = new UploadScorecardCommand("match-1");
        var (next, tracker) = NextDelegate.TrackingAsync<UploadScorecardCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PluralTerminal_ActorOwnsNeitherTeam_ReturnsForbidden()
    {
        var match = new CricketMatch("match-1", Team1Id: "team-1", Team2Id: "team-2");
        var team1 = new TestTeam("team-1", CreatedByActorId: "rival-1");
        var team2 = new TestTeam("team-2", CreatedByActorId: "rival-2");
        var teamRepo = new InMemoryRepo<TestTeam>(t => t.Id, team1, team2);

        var behavior = CreateCricketBehavior("actor-1", match, teamRepo);
        var command = new UploadScorecardCommand("match-1");
        var (next, tracker) = NextDelegate.TrackingAsync<UploadScorecardCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        result.UnwrapError().Code.Should().Be("not_team_owner");
        tracker.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_PluralTerminal_DuplicateIds_DeduplicatedBeforeLoading()
    {
        // Match with Team1Id == Team2Id (rare data anomaly or by-design "single team")
        // should load the team exactly once and surface a size-1 list to Authorize.
        var match = new CricketMatch("match-1", Team1Id: "team-x", Team2Id: "team-x");
        var team = new TestTeam("team-x", CreatedByActorId: "actor-1");
        var teamRepo = new InMemoryRepo<TestTeam>(t => t.Id, team);

        var captured = new List<IReadOnlyList<TestTeam>>();
        var behavior = CreateCricketBehavior("actor-1", match, teamRepo);
        var command = new UploadScorecardCommand("match-1", owners => captured.Add(owners));
        var (next, _) = NextDelegate.TrackingAsync<UploadScorecardCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().HaveCount(1);
        captured[0].Count.Should().Be(1, "duplicate IDs should be de-duplicated before loading");
    }

    [Fact]
    public async Task Handle_PluralTerminal_OneTeamMissing_CollapsesToForbidden()
    {
        // If one of the two fan-out targets fails to load, collapse to Forbidden
        // even if the other team would have authorized the actor — partial-load
        // failure exposes existence/consistency details and is treated as Forbidden.
        var match = new CricketMatch("match-1", Team1Id: "team-1", Team2Id: "team-missing");
        var team1 = new TestTeam("team-1", CreatedByActorId: "actor-1");
        var teamRepo = new InMemoryRepo<TestTeam>(t => t.Id, team1);  // team-missing not present

        var behavior = CreateCricketBehavior("actor-1", match, teamRepo);
        var command = new UploadScorecardCommand("match-1");
        var (next, tracker) = NextDelegate.TrackingAsync<UploadScorecardCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        result.UnwrapError().Code.Should().Be("resource.authorization-via.load-failed");
        tracker.WasInvoked.Should().BeFalse();
    }

    #endregion

    #region Accessor populated for handler during next() — v4 typed accessor (leaf)

    [Fact]
    public async Task Handle_AuthorizeSucceeds_LeafAccessorPopulatedDuringNextWithSameInstance()
    {
        // For IAuthorizeResourceVia<TOwner> commands the handler's mutation target is
        // almost always the LEAF (the resource the command identifies), not the via owner.
        // v4 accessor exposes the leaf; owner accessor is intentionally not in scope.
        var leaf = new TestLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new TestOwner("owner-1", CreatedByActorId: "actor-1");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id, owner);
        var behavior = CreateBehavior<SingleHopCommand>("actor-1", leaf, ownerRepo);
        var command = new SingleHopCommand("leaf-1");

        TestLeaf? observedDuringNext = null;
        bool observedSameInstance = false;
        var holder = new AuthorizedResourceHolder<SingleHopCommand, TestLeaf>();
        global::Mediator.MessageHandlerDelegate<SingleHopCommand, Result<string>> next = (_, _) =>
        {
            holder.TryGetResource(out observedDuringNext).Should().BeTrue(
                "ResourceAuthorizationViaBehavior must populate the LEAF accessor before invoking next");
            observedSameInstance = ReferenceEquals(observedDuringNext, leaf);
            return new ValueTask<Result<string>>(Result.Ok("Done"));
        };

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        observedDuringNext.Should().NotBeNull();
        observedSameInstance.Should().BeTrue(
            "the LEAF accessor must return the SAME instance the leaf loader returned");
        holder.TryGetResource(out _).Should().BeFalse(
            "after Handle returns, the using-block has disposed and the accessor is empty");
    }

    [Fact]
    public async Task Handle_AuthorizeFails_LeafAccessorNotPopulated()
    {
        // Denied via-authorizations must not leak the leaf via the accessor.
        var leaf = new TestLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new TestOwner("owner-1", CreatedByActorId: "someone-else");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id, owner);
        var behavior = CreateBehavior<SingleHopCommand>("actor-1", leaf, ownerRepo);
        var command = new SingleHopCommand("leaf-1");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Forbidden>();
        tracker.WasInvoked.Should().BeFalse();
        new AuthorizedResourceHolder<SingleHopCommand, TestLeaf>()
            .TryGetResource(out _).Should().BeFalse(
                "denied via-authorization must not populate the leaf accessor");
    }

    [Fact]
    public async Task Handle_LeafLoadFails_LeafAccessorNotPopulated()
    {
        // No leaf loaded → no accessor populated. Pipeline returns a NotFound-shaped
        // failure (matches the documented "leaf load failure bubbles verbatim" semantics).
        var owner = new TestOwner("owner-1", CreatedByActorId: "actor-1");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id, owner);
        var behavior = CreateBehavior<SingleHopCommand>(
            actorId: "actor-1",
            leaf: null,
            ownerRepo: ownerRepo);

        var command = new SingleHopCommand("missing-leaf");
        var (next, tracker) = NextDelegate.TrackingAsync<SingleHopCommand, Result<string>>(
            Result.Ok("should not reach"));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        tracker.WasInvoked.Should().BeFalse();
        new AuthorizedResourceHolder<SingleHopCommand, TestLeaf>()
            .TryGetResource(out _).Should().BeFalse(
                "leaf load failure must not populate the accessor");
    }

    [Fact]
    public async Task Handle_AccessorPoppedAfterHandlerEvenOnHandlerFailure()
    {
        // The using-block guarantees pop even when next returns a failure result.
        var leaf = new TestLeaf("leaf-1", OwnerId: "owner-1");
        var owner = new TestOwner("owner-1", CreatedByActorId: "actor-1");
        var ownerRepo = new InMemoryRepo<TestOwner>(o => o.Id, owner);
        var behavior = CreateBehavior<SingleHopCommand>("actor-1", leaf, ownerRepo);
        var command = new SingleHopCommand("leaf-1");
        global::Mediator.MessageHandlerDelegate<SingleHopCommand, Result<string>> next = (_, _) =>
            new ValueTask<Result<string>>(Result.Fail<string>(
                new Error.Conflict(null, "test.conflict") { Detail = "Handler-level failure" }));

        var result = await behavior.Handle(command, next, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.Conflict>();
        new AuthorizedResourceHolder<SingleHopCommand, TestLeaf>()
            .TryGetResource(out _).Should().BeFalse(
                "the using-block must pop the accessor even when the handler returns a failure result");
    }

    #endregion

    #region Helpers — synthetic types and behavior factory

    /// <summary>Leaf resource the command identifies.</summary>
    public sealed record TestLeaf(string Id, string OwnerId)
        : IIdentifyRelatedResource<TestOwner, string>
    {
        public string GetRelatedResourceId() => OwnerId;
    }

    /// <summary>Owner resource authorization is evaluated against.</summary>
    public sealed record TestOwner(string Id, string CreatedByActorId);

    /// <summary>Single-hop, singular-terminal command. Match-like leaf → single owner.</summary>
    public sealed record SingleHopCommand(string LeafId)
        : global::Mediator.ICommand<Result<string>>,
          IAuthorizeResourceVia<TestOwner>,
          IIdentifyResource<TestLeaf, string>
    {
        public string GetResourceId() => LeafId;
        public IResult Authorize(Actor actor, IReadOnlyList<TestOwner> owners) =>
            owners.Any(o => o.CreatedByActorId == actor.Id)
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("not_owner")
                {
                    Detail = "Actor does not own any candidate resource.",
                });
    }

    /// <summary>Cricket-like leaf: Match with two team navigations.</summary>
    public sealed record CricketMatch(string Id, string Team1Id, string Team2Id)
        : IIdentifyRelatedResources<TestTeam, string>
    {
        public IReadOnlyList<string> GetRelatedResourceIds() => [Team1Id, Team2Id];
    }

    /// <summary>Cricket team resource.</summary>
    public sealed record TestTeam(string Id, string CreatedByActorId);

    /// <summary>Cricket command: authorize against either Team1 or Team2 (OR-ownership).</summary>
    public sealed record UploadScorecardCommand(string MatchId, Action<IReadOnlyList<TestTeam>>? OnAuthorize = null)
        : global::Mediator.ICommand<Result<string>>,
          IAuthorizeResourceVia<TestTeam>,
          IIdentifyResource<CricketMatch, string>
    {
        public string GetResourceId() => MatchId;
        public IResult Authorize(Actor actor, IReadOnlyList<TestTeam> owners)
        {
            OnAuthorize?.Invoke(owners);
            return owners.Any(t => t.CreatedByActorId == actor.Id)
                ? Result.Ok()
                : Result.Fail(new Error.Forbidden("not_team_owner")
                {
                    Detail = "Actor does not own either match team.",
                });
        }
    }

    /// <summary>Minimal in-memory repo accessible via key-selector.</summary>
    private sealed class InMemoryRepo<T>(Func<T, string> idSelector, params T[] items)
        where T : class
    {
        private readonly Dictionary<string, T> _items = items.ToDictionary(idSelector);

        public Result<T> GetById(string id) =>
            _items.TryGetValue(id, out var v)
                ? Result.Ok(v)
                : Result.Fail<T>(new Error.NotFound(new ResourceRef(typeof(T).Name, id)));
    }

    /// <summary>Per-test leaf loader implementing IResourceLoader directly.</summary>
    private sealed class FakeLeafLoader<TMsg, TLeaf>(TLeaf? leaf) : IResourceLoader<TMsg, TLeaf>
        where TLeaf : class
    {
        public Task<Result<TLeaf>> LoadAsync(TMsg message, CancellationToken cancellationToken)
            => Task.FromResult(leaf is not null
                ? Result.Ok(leaf)
                : Result.Fail<TLeaf>(new Error.NotFound(new ResourceRef(typeof(TLeaf).Name, null))));
    }

    /// <summary>Builds single-hop singular path TestLeaf.OwnerId -> TestOwner.</summary>
    private static ResolvedAuthorizationPath BuildLeafToOwnerPath(InMemoryRepo<TestOwner> ownerRepo)
    {
        var hop = new ResolvedAuthorizationHop(
            fromType: typeof(TestLeaf),
            toType: typeof(TestOwner),
            toIdType: typeof(string),
            extractIds: src => [((TestLeaf)src).OwnerId],
            loadAsync: (_, id, _) =>
            {
                var r = ownerRepo.GetById((string)id);
                return Task.FromResult(r.TryGetValue(out var v, out var err)
                    ? HopLoadResult.Success(v)
                    : HopLoadResult.Failure(err));
            },
            isPlural: false);

        return new ResolvedAuthorizationPath(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(TestLeaf),
            ownerType: typeof(TestOwner),
            hops: [hop]);
    }

    /// <summary>Builds single-hop plural-terminal path CricketMatch.{Team1Id,Team2Id} -> TestTeam.</summary>
    private static ResolvedAuthorizationPath BuildMatchToTeamsPath(InMemoryRepo<TestTeam> teamRepo)
    {
        var hop = new ResolvedAuthorizationHop(
            fromType: typeof(CricketMatch),
            toType: typeof(TestTeam),
            toIdType: typeof(string),
            extractIds: src =>
            {
                var m = (CricketMatch)src;
                return [m.Team1Id, m.Team2Id];
            },
            loadAsync: (_, id, _) =>
            {
                var r = teamRepo.GetById((string)id);
                return Task.FromResult(r.TryGetValue(out var v, out var err)
                    ? HopLoadResult.Success(v)
                    : HopLoadResult.Failure(err));
            },
            isPlural: true);

        return new ResolvedAuthorizationPath(
            messageType: typeof(UploadScorecardCommand),
            leafType: typeof(CricketMatch),
            ownerType: typeof(TestTeam),
            hops: [hop]);
    }

    private static ResourceAuthorizationViaBehavior<TMessage, TestLeaf, TestOwner, Result<string>>
        CreateBehavior<TMessage>(
            string actorId,
            TestLeaf? leaf,
            InMemoryRepo<TestOwner> ownerRepo)
        where TMessage : IAuthorizeResourceVia<TestOwner>, global::Mediator.IMessage
    {
        var actorProvider = FakeActorProvider.NoPermissions(actorId);

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<TMessage, TestLeaf>>(_ => new FakeLeafLoader<TMessage, TestLeaf>(leaf));
        var sp = services.BuildServiceProvider();

        var path = BuildLeafToOwnerPath(ownerRepo);

        return new ResourceAuthorizationViaBehavior<TMessage, TestLeaf, TestOwner, Result<string>>(
            actorProvider, sp, path);
    }

    private static ResourceAuthorizationViaBehavior<UploadScorecardCommand, CricketMatch, TestTeam, Result<string>>
        CreateCricketBehavior(
            string actorId,
            CricketMatch? match,
            InMemoryRepo<TestTeam> teamRepo)
    {
        var actorProvider = FakeActorProvider.NoPermissions(actorId);

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<UploadScorecardCommand, CricketMatch>>(
            _ => new FakeLeafLoader<UploadScorecardCommand, CricketMatch>(match));
        var sp = services.BuildServiceProvider();

        var path = BuildMatchToTeamsPath(teamRepo);

        return new ResourceAuthorizationViaBehavior<UploadScorecardCommand, CricketMatch, TestTeam, Result<string>>(
            actorProvider, sp, path);
    }

    #endregion
}