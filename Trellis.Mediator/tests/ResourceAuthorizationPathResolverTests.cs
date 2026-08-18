namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="ResourceAuthorizationPathResolver"/>. Verifies the DFS path
/// enumeration, distinct-simple-path counting, and the registration-time invariants
/// (no path, ambiguous paths, plural-in-middle, cycle tolerance, identity-leaf-owner reject).
/// </summary>
public class ResourceAuthorizationPathResolverTests
{
    #region Single-hop singular

    [Fact]
    public void Resolve_SingleHopSingular_ReturnsOneHopPath()
    {
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(LeafSingular),
            ownerType: typeof(Owner),
            candidateEntityTypes: [typeof(LeafSingular), typeof(Owner)]);

        path.Hops.Should().HaveCount(1);
        path.Hops[0].FromType.Should().Be<LeafSingular>();
        path.Hops[0].ToType.Should().Be<Owner>();
        path.Hops[0].ToIdType.Should().Be<string>();
        path.Hops[0].IsPlural.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SingleHopSingular_ExtractorReadsCorrectId()
    {
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(LeafSingular),
            ownerType: typeof(Owner),
            candidateEntityTypes: [typeof(LeafSingular)]);

        var src = new LeafSingular("owner-42");
        var ids = path.Hops[0].ExtractIds(src);

        ids.Should().Equal(["owner-42"]);
    }

    [Fact]
    public void Resolve_DuplicateCandidateTypes_StillReturnsSinglePath()
    {
        // Caller may legitimately pass overlapping assemblies or scan results containing
        // the same type more than once. Deduplication must happen inside the resolver so
        // the ambiguity check doesn't falsely trip on duplicate edges from duplicate
        // candidate-type entries.
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(LeafSingular),
            ownerType: typeof(Owner),
            candidateEntityTypes: [typeof(LeafSingular), typeof(LeafSingular), typeof(Owner), typeof(Owner)]);

        path.Hops.Should().HaveCount(1);
        path.Hops[0].FromType.Should().Be<LeafSingular>();
        path.Hops[0].ToType.Should().Be<Owner>();
    }

    #endregion

    #region Single-hop plural terminal (cricket)

    [Fact]
    public void Resolve_SingleHopPlural_ReturnsOnePluralHop()
    {
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(CricketCommand),
            leafType: typeof(CricketMatch),
            ownerType: typeof(Team),
            candidateEntityTypes: [typeof(CricketMatch), typeof(Team)]);

        path.Hops.Should().HaveCount(1);
        path.Hops[0].IsPlural.Should().BeTrue();
        path.Hops[0].FromType.Should().Be<CricketMatch>();
        path.Hops[0].ToType.Should().Be<Team>();
    }

    [Fact]
    public void Resolve_SingleHopPlural_ExtractorReadsAllIds()
    {
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(CricketCommand),
            leafType: typeof(CricketMatch),
            ownerType: typeof(Team),
            candidateEntityTypes: [typeof(CricketMatch)]);

        var match = new CricketMatch("home-team", "away-team");
        var ids = path.Hops[0].ExtractIds(match);

        ids.Should().Equal(["home-team", "away-team"]);
    }

    #endregion

    #region Multi-hop chain

    [Fact]
    public void Resolve_MultiHopChain_ReturnsAllHopsInOrder()
    {
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(ChainCommand),
            leafType: typeof(ChainA),
            ownerType: typeof(ChainC),
            candidateEntityTypes: [typeof(ChainA), typeof(ChainB), typeof(ChainC)]);

        path.Hops.Should().HaveCount(2);
        path.Hops[0].FromType.Should().Be<ChainA>();
        path.Hops[0].ToType.Should().Be<ChainB>();
        path.Hops[1].FromType.Should().Be<ChainB>();
        path.Hops[1].ToType.Should().Be<ChainC>();
    }

    #endregion

    #region No path

    [Fact]
    public void Resolve_NoPathExists_Throws()
    {
        var act = () => ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(LeafSingular),
            ownerType: typeof(Unreachable),
            candidateEntityTypes: [typeof(LeafSingular), typeof(Owner), typeof(Unreachable)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no path exists from LeafSingular to Unreachable*");
    }

    #endregion

    #region Ambiguous paths (diamond)

    [Fact]
    public void Resolve_DiamondGraphTwoPaths_ThrowsWithBothPathsListed()
    {
        // DiamondA → DiamondB → DiamondD AND DiamondA → DiamondC → DiamondD
        var act = () => ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(DiamondCommand),
            leafType: typeof(DiamondA),
            ownerType: typeof(DiamondD),
            candidateEntityTypes: [typeof(DiamondA), typeof(DiamondB), typeof(DiamondC), typeof(DiamondD)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*2 distinct simple paths*DiamondA*DiamondD*");
    }

    #endregion

    #region Plural in middle

    [Fact]
    public void Resolve_PluralHopInMiddle_Throws()
    {
        // PluralMid → PluralStep (plural), PluralStep → PluralEnd (singular)
        // Target = PluralEnd. Plural hop is hop[0], which is non-terminal → rejected.
        var act = () => ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(PluralMidCommand),
            leafType: typeof(PluralMid),
            ownerType: typeof(PluralEnd),
            candidateEntityTypes: [typeof(PluralMid), typeof(PluralStep), typeof(PluralEnd)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*plural but is not the terminal hop*");
    }

    #endregion

    #region Cycle tolerance

    [Fact]
    public void Resolve_CycleInGraphButValidPathExists_ReturnsValidPath()
    {
        // CyA → CyB → CyA (cycle), AND CyA → CyB → CyTarget.
        // DFS must skip the cycle and find the valid path.
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(CycleCommand),
            leafType: typeof(CyA),
            ownerType: typeof(CyTarget),
            candidateEntityTypes: [typeof(CyA), typeof(CyB), typeof(CyTarget)]);

        path.Hops.Should().HaveCount(2);
        path.Hops[0].ToType.Should().Be<CyB>();
        path.Hops[1].ToType.Should().Be<CyTarget>();
    }

    #endregion

    #region Identity (leaf == owner) rejection

    [Fact]
    public void Resolve_LeafEqualsOwner_Throws()
    {
        var act = () => ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(LeafSingular),
            ownerType: typeof(LeafSingular),
            candidateEntityTypes: [typeof(LeafSingular)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Use IAuthorizeResource<LeafSingular>*");
    }

    #endregion

    #region Hop loader — null-payload defense

    [Fact]
    public async Task ResolvedHop_LoaderReturnsSuccessWithNullValue_CollapsesToForbidden()
    {
        // Defense-in-depth: a SharedResourceLoaderById that violates its Result<T> contract
        // by returning a successful Result carrying a null value must NOT crash the
        // authorization pipeline with ArgumentNullException from HopLoadResult.Success.
        // The documented invariant on ResourceAuthorizationViaBehavior is "intermediate /
        // owner load failures collapse to Error.Forbidden"; that guarantee must hold even
        // when the loader's success contract is broken, otherwise an internal exception
        // bubbles as a 500 instead of a 403.
        var path = ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(SingleHopCommand),
            leafType: typeof(LeafSingular),
            ownerType: typeof(Owner),
            candidateEntityTypes: [typeof(LeafSingular), typeof(Owner)]);

        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<Owner, string>>(_ => new NullPayloadOwnerLoader());
        var sp = services.BuildServiceProvider();

        var hop = path.Hops[0];
        var outcome = await hop.LoadAsync(sp, "any-owner-id", CancellationToken.None);

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error.Should().BeOfType<Error.Forbidden>();
        outcome.Error!.Code.Should().Be("resource.authorization-via.null-payload");
        outcome.Value.Should().BeNull();
    }

    private sealed class NullPayloadOwnerLoader : SharedResourceLoaderById<Owner, string>
    {
        public override Task<Result<Owner>> GetByIdAsync(string id, CancellationToken cancellationToken)
            => Task.FromResult(Result.Ok<Owner>(null!));
    }

    #endregion

    #region Hop loader — nullable TId rejection

    [Fact]
    public void Resolve_NullableTId_ThrowsClearStartupError()
    {
        // Nullable<T> as TId would crash MakeGenericMethod on the `where TId : notnull`
        // constraint in SingularExtractorImpl with a confusing reflection error. The resolver
        // detects this at startup and throws an InvalidOperationException naming the offending
        // type and explaining the supported shape. The candidate type must NOT escape this
        // class so other tests' assembly-scan registrations do not see it.
        var act = () => ResourceAuthorizationPathResolver.Resolve(
            messageType: typeof(NullableIdCommand),
            leafType: typeof(NullableIdLeaf),
            ownerType: typeof(NullableIdOwner),
            candidateEntityTypes: [typeof(NullableIdLeaf), typeof(NullableIdOwner)]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Nullable<*>*not supported*");
    }

    // Nested-private fixtures. `Assembly.GetTypes()` (used by `GetLoadableTypes`) does return
    // non-public types, so these CAN be enumerated by `AddResourceAuthorization(Assembly)` in
    // unrelated tests. The actual reason they don't crash other tests' resolver passes is
    // structural: `ResourceAuthorizationPathResolver.BuildHop` rejects `Nullable<TId>` only for
    // hops on the *resolved* path (see resolver source for the explicit comment), not at
    // graph-build time, so an unrelated entity declaring an optional navigation never causes a
    // resolver pass for a different command to fail. Keeping the fixtures private is purely
    // for namespace hygiene.
    private sealed class NullableIdOwner
    {
        public string Id { get; } = "";
    }

    private sealed class NullableIdLeaf : IIdentifyRelatedResource<NullableIdOwner, Guid?>
    {
        public Guid? GetRelatedResourceId() => null;
    }

    private sealed record NullableIdCommand(string Id) { }

    #endregion

    #region Test fixtures — entities and commands

    /// <summary>Owner aggregate (terminal target for most tests).</summary>
    public sealed class Owner
    {
        public string Id { get; } = "";
    }

    public sealed class Unreachable
    {
        public string Id { get; } = "";
    }

    /// <summary>Leaf with singular navigation to Owner.</summary>
    public sealed class LeafSingular(string ownerId) : IIdentifyRelatedResource<Owner, string>
    {
        public string OwnerId { get; } = ownerId;
        public string GetRelatedResourceId() => OwnerId;
    }

    public sealed record SingleHopCommand(string LeafId)
    { }

    // --- Cricket fan-out ---

    public sealed class Team
    {
        public string Id { get; } = "";
    }

    public sealed class CricketMatch(string team1, string team2) : IIdentifyRelatedResources<Team, string>
    {
        public string Team1Id { get; } = team1;
        public string Team2Id { get; } = team2;
        public IReadOnlyList<string> GetRelatedResourceIds() => [Team1Id, Team2Id];
    }

    public sealed record CricketCommand(string MatchId)
    { }

    // --- Multi-hop chain ---

    public sealed class ChainC
    {
        public string Id { get; } = "";
    }

    public sealed class ChainB(string cId) : IIdentifyRelatedResource<ChainC, string>
    {
        public string CId { get; } = cId;
        public string GetRelatedResourceId() => CId;
    }

    public sealed class ChainA(string bId) : IIdentifyRelatedResource<ChainB, string>
    {
        public string BId { get; } = bId;
        public string GetRelatedResourceId() => BId;
    }

    public sealed record ChainCommand(string Id)
    { }

    // --- Diamond (ambiguous paths) ---

    public sealed class DiamondD
    {
        public string Id { get; } = "";
    }

    public sealed class DiamondB(string dId) : IIdentifyRelatedResource<DiamondD, string>
    {
        public string DId { get; } = dId;
        public string GetRelatedResourceId() => DId;
    }

    public sealed class DiamondC(string dId) : IIdentifyRelatedResource<DiamondD, string>
    {
        public string DId { get; } = dId;
        public string GetRelatedResourceId() => DId;
    }

    public sealed class DiamondA(string bId, string cId)
        : IIdentifyRelatedResource<DiamondB, string>, IIdentifyRelatedResource<DiamondC, string>
    {
        public string BId { get; } = bId;
        public string CId { get; } = cId;

        // Explicit interface impls so the two GetRelatedResourceId methods can coexist.
        string IIdentifyRelatedResource<DiamondB, string>.GetRelatedResourceId() => BId;
        string IIdentifyRelatedResource<DiamondC, string>.GetRelatedResourceId() => CId;
    }

    // (Removed redundant note: DiamondA now has both outbound edges declared above.)

    public sealed record DiamondCommand(string Id)
    { }

    // --- Plural-in-middle ---

    public sealed class PluralEnd
    {
        public string Id { get; } = "";
    }

    public sealed class PluralStep(string endId) : IIdentifyRelatedResource<PluralEnd, string>
    {
        public string EndId { get; } = endId;
        public string GetRelatedResourceId() => EndId;
    }

    public sealed class PluralMid(IReadOnlyList<string> stepIds) : IIdentifyRelatedResources<PluralStep, string>
    {
        public IReadOnlyList<string> StepIds { get; } = stepIds;
        public IReadOnlyList<string> GetRelatedResourceIds() => StepIds;
    }

    public sealed record PluralMidCommand(string Id)
    { }

    // --- Cycle tolerance ---

    public sealed class CyTarget
    {
        public string Id { get; } = "";
    }

    public sealed partial class CyB(string aId, string targetId)
        : IIdentifyRelatedResource<CyTarget, string>
    {
        public string AId { get; } = aId;
        public string TargetId { get; } = targetId;
        public string GetRelatedResourceId() => TargetId;
    }

    // CyA points to CyB; CyB also has a back-edge to CyA via a second interface impl.
    // Forms a cycle CyA -> CyB -> CyA -> ... in the graph. DFS visited-set must avoid
    // following the back-edge so the valid path CyA -> CyB -> CyTarget is still found
    // and the cycle is silently skipped.
    public sealed class CyA(string bId) : IIdentifyRelatedResource<CyB, string>
    {
        public string BId { get; } = bId;
        public string GetRelatedResourceId() => BId;
    }

    public sealed partial class CyB : IIdentifyRelatedResource<CyA, string>
    {
        string IIdentifyRelatedResource<CyA, string>.GetRelatedResourceId() => AId;
    }

    public sealed record CycleCommand(string Id)
    { }

    #endregion
}