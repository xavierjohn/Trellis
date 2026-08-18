namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Tests for the assembly-scanning <see cref="ServiceCollectionExtensions.AddResourceAuthorization(IServiceCollection, System.Reflection.Assembly[])"/>
/// extension when it encounters <see cref="IAuthorizeResourceVia{TOwner}"/> commands.
/// Verifies the via-command discovery, path resolution, pipeline registration, and the
/// dual-security-mode rejection (a command may not implement both
/// <see cref="IAuthorizeResource{TResource}"/> and <see cref="IAuthorizeResourceVia{TOwner}"/>).
/// </summary>
public class ResourceAuthorizationViaScanningTests
{
    [Fact]
    public void Scanning_ViaCommandWithIIdentifyResource_RegistersViaBehavior()
    {
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("a"));
        services.AddResourceAuthorization(typeof(ScanCricketCommand).Assembly);

        // Closed pipeline behavior registered for the via-command.
        var descriptor = services.SingleOrDefault(d =>
            d.ServiceType == typeof(IPipelineBehavior<ScanCricketCommand, Result<string>>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Scanning_SameAssemblyPassedMultipleTimes_RegistersBehaviorOnce()
    {
        // Duplicate assemblies in the params array must be deduplicated. Without dedup the
        // scanner would `services.Insert(...)` a second closed-generic IPipelineBehavior
        // descriptor for the same (TMessage, TResponse), causing the resource-authorization
        // behavior to run twice per request (and the via-behavior to load the leaf twice,
        // re-walk the path, etc.).
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("a"));

        var assembly = typeof(ScanCricketCommand).Assembly;
        services.AddResourceAuthorization(assembly, assembly, assembly);

        var descriptors = services.Where(d =>
            d.ServiceType == typeof(IPipelineBehavior<ScanCricketCommand, Result<string>>))
            .ToList();

        descriptors.Should().ContainSingle(
            "duplicate assemblies must not produce duplicate closed-generic pipeline behaviors");
    }

    [Fact]
    public void EnsureNotDualSecurityMode_BothInterfacesPresent_Throws()
    {
        // A command implementing BOTH IAuthorizeResource<T> AND IAuthorizeResourceVia<TOwner>
        // is rejected. Security primitives are not silently composed.
        var authIface = typeof(DualModeFixture).GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuthorizeResource<>));
        var viaIface = typeof(DualModeFixture).GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuthorizeResourceVia<>));

        var act = () => ServiceCollectionExtensions.EnsureNotDualSecurityMode(
            typeof(DualModeFixture), authIface, viaIface);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*both IAuthorizeResource*and IAuthorizeResourceVia*");
    }

    [Fact]
    public void EnsureNotDualSecurityMode_OnlyAuthResource_DoesNotThrow()
        => ServiceCollectionExtensions.EnsureNotDualSecurityMode(
            typeof(object),
            authIface: typeof(IAuthorizeResource<ScanTeam>),
            viaIface: null);

    [Fact]
    public void EnsureNotDualSecurityMode_OnlyVia_DoesNotThrow()
        => ServiceCollectionExtensions.EnsureNotDualSecurityMode(
            typeof(object),
            authIface: null,
            viaIface: typeof(IAuthorizeResourceVia<ScanTeam>));

    [Fact]
    public async Task EndToEnd_CricketFanout_AuthorizesAndRunsHandler()
    {
        // Full pipeline integration: scanned via-command + scanned SharedResourceLoaderById<Team,_>
        // + actor provider + handler. Asserts the resolved path is wired and the cricket
        // OR-ownership rule passes when the actor owns one of the teams.
        var services = new ServiceCollection();
        services.AddTrellisBehaviors();
        services.AddScoped<IActorProvider>(_ => FakeActorProvider.NoPermissions("actor-1"));

        // Pre-register repos so the scanned SharedResourceLoaderById<,> implementations can resolve.
        var matchRepo = new FakeMatchRepo();
        matchRepo.Add(new ScanMatch("match-1", "team-1", "team-2"));
        services.AddSingleton(matchRepo);

        var teamRepo = new FakeTeamRepo();
        teamRepo.Add(new ScanTeam("team-1", "actor-1"));   // actor owns this one
        teamRepo.Add(new ScanTeam("team-2", "rival"));
        services.AddSingleton(teamRepo);

        services.AddResourceAuthorization(typeof(ScanCricketCommand).Assembly);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var behavior = scope.ServiceProvider
            .GetRequiredService<IPipelineBehavior<ScanCricketCommand, Result<string>>>();

        var (next, tracker) = NextDelegate.TrackingAsync<ScanCricketCommand, Result<string>>(
            Result.Ok("Done"));

        var result = await behavior.Handle(new ScanCricketCommand("match-1"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tracker.WasInvoked.Should().BeTrue();
    }

    [Fact]
    public void AddResourceAuthorization_ExplicitOverload_DualMode_Throws()
    {
        // Explicit zero-hop registration must also reject dual-mode commands. The dual-mode
        // invariant cannot be bypassed by using the explicit (non-scanning) entry point.
        // DualModeNonCommand intentionally does NOT implement Mediator.ICommand<> so the
        // assembly scanner skips it (tResponse is null path), avoiding cross-test pollution;
        // we exercise the explicit API surface directly here.
        var services = new ServiceCollection();
        var act = () => services.AddResourceAuthorization<DualModeNonCommand, ScanTeam, Result<string>>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*both IAuthorizeResource*and IAuthorizeResourceVia*");
    }

    [Fact]
    public void AddRelatedResourceAuthorization_ExplicitOverload_DualMode_Throws()
    {
        // Same invariant for the via-shaped explicit overload.
        var services = new ServiceCollection();
        var hop = new ResolvedAuthorizationHop(
            fromType: typeof(ScanMatch),
            toType: typeof(ScanTeam),
            toIdType: typeof(string),
            extractIds: src => [((ScanMatch)src).Team1Id],
            loadAsync: (_, _, _) => Task.FromResult(HopLoadResult.Failure(new Error.Forbidden("x"))),
            isPlural: false);
        var path = new ResolvedAuthorizationPath(
            typeof(DualModeNonCommand), typeof(ScanMatch), typeof(ScanTeam), [hop]);

        var act = () => services.AddRelatedResourceAuthorization<DualModeNonCommand, ScanMatch, ScanTeam, Result<string>>(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*both IAuthorizeResource*and IAuthorizeResourceVia*");
    }

    [Fact]
    public void EnsureExactlyOneIIdentifyResourceForVia_ZeroIdentify_Throws()
    {
        // No IIdentifyResource on a via-command would silently leave the marker unprotected
        // at runtime; fail loud at scan time.
        var viaIface = typeof(IAuthorizeResourceVia<ScanTeam>);

        var act = () => ServiceCollectionExtensions.EnsureExactlyOneIIdentifyResourceForVia(
            typeof(ScanCricketCommand),
            viaIface,
            identifyIfaces: []);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement IIdentifyResource<TLeaf, TLeafId>*");
    }

    [Fact]
    public void EnsureExactlyOneIIdentifyResourceForVia_TwoIdentify_Throws()
    {
        // Multiple IIdentifyResource on one via-command makes leaf selection ambiguous —
        // picking the first arbitrarily could authorize the wrong resource chain. Fail
        // loud at scan time with both candidates named.
        var viaIface = typeof(IAuthorizeResourceVia<ScanTeam>);
        var ifaces = new[]
        {
            typeof(IIdentifyResource<ScanMatch, string>),
            typeof(IIdentifyResource<ScanTeam, string>),
        };

        var act = () => ServiceCollectionExtensions.EnsureExactlyOneIIdentifyResourceForVia(
            typeof(ScanCricketCommand),
            viaIface,
            ifaces);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*2 IIdentifyResource<,> interfaces*IIdentifyResource<ScanMatch, String>*IIdentifyResource<ScanTeam, String>*");
    }

    [Fact]
    public void EnsureExactlyOneIIdentifyResourceForVia_ExactlyOne_DoesNotThrow()
        => ServiceCollectionExtensions.EnsureExactlyOneIIdentifyResourceForVia(
            typeof(ScanCricketCommand),
            typeof(IAuthorizeResourceVia<ScanTeam>),
            identifyIfaces: [typeof(IIdentifyResource<ScanMatch, string>)]);

    [Fact]
    public void EnsureAtMostOneClosedAuthorizationMarker_TwoClosedAuthResource_Throws()
    {
        // A scanned mediator message that implements IAuthorizeResource<A> AND
        // IAuthorizeResource<B> would, under the prior FirstOrDefault scan, register the
        // closed-generic ResourceAuthorizationBehavior for only one of the two resources
        // and silently ignore the other — a security marker would never fire. Reject at
        // scan time.
        var ifaces = new[]
        {
            typeof(IAuthorizeResource<ScanTeam>),
            typeof(IAuthorizeResource<ScanMatch>),
        };

        var act = () => ServiceCollectionExtensions.EnsureAtMostOneClosedAuthorizationMarker(
            typeof(MultiClosedAuthResourceFixture),
            ifaces,
            markerInterfaceName: "IAuthorizeResource");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MultiClosedAuthResourceFixture*IAuthorizeResource*ScanTeam*ScanMatch*");
    }

    [Fact]
    public void EnsureAtMostOneClosedAuthorizationMarker_TwoClosedAuthorizeResourceVia_Throws()
    {
        // Symmetric rejection for via-commands.
        var ifaces = new[]
        {
            typeof(IAuthorizeResourceVia<ScanTeam>),
            typeof(IAuthorizeResourceVia<ScanMatch>),
        };

        var act = () => ServiceCollectionExtensions.EnsureAtMostOneClosedAuthorizationMarker(
            typeof(MultiClosedViaFixture),
            ifaces,
            markerInterfaceName: "IAuthorizeResourceVia");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MultiClosedViaFixture*IAuthorizeResourceVia*ScanTeam*ScanMatch*");
    }

    [Fact]
    public void EnsureAtMostOneClosedAuthorizationMarker_OneClosed_DoesNotThrow()
        => ServiceCollectionExtensions.EnsureAtMostOneClosedAuthorizationMarker(
            typeof(object),
            [typeof(IAuthorizeResource<ScanTeam>)],
            markerInterfaceName: "IAuthorizeResource");

    [Fact]
    public void EnsureAtMostOneClosedAuthorizationMarker_Zero_DoesNotThrow()
        => ServiceCollectionExtensions.EnsureAtMostOneClosedAuthorizationMarker(
            typeof(object),
            [],
            markerInterfaceName: "IAuthorizeResource");

    [Fact]
    public void ValidateResourceAuthorizationResponseType_ViaCommand_BadResponse_ErrorNamesViaMarker()
    {
        // The diagnostic must point at IAuthorizeResourceVia and ResourceAuthorizationViaBehavior
        // for via-commands so consumers fix the right marker. The previously-hardcoded
        // "implements IAuthorizeResource<...>" text was misleading.
        var act = () => ServiceCollectionExtensions.ValidateResourceAuthorizationResponseType(
            messageType: typeof(ScanCricketCommand),
            resourceType: typeof(ScanTeam),
            responseType: typeof(string),  // not IResult / IFailureFactory
            markerInterfaceName: "IAuthorizeResourceVia",
            behaviorTypeName: "ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAuthorizeResourceVia<ScanTeam>*ResourceAuthorizationViaBehavior<TMessage, TLeaf, TOwner, TResponse>*");
    }
}

#region Top-level entity fixtures

// The fixtures are emitted as TOP-LEVEL types in this test file (not nested in the test class)
// so the assembly scan exercises the same shape a production consumer would have. They are
// internal to keep them out of the public surface; the scanner ignores public-vs-internal.

internal sealed record ScanCricketCommand(string MatchId)
    : ICommand<Result<string>>,
      IAuthorizeResourceVia<ScanTeam>,
      IIdentifyResource<ScanMatch, string>
{
    public string GetResourceId() => MatchId;

    public IResult Authorize(Actor actor, IReadOnlyList<ScanTeam> owners) =>
        owners.Any(t => t.CreatedByActorId == actor.Id)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("not_team_owner"));
}

internal sealed record DualModeFixture(string Id)
    : IAuthorizeResource<ScanTeam>,
      IAuthorizeResourceVia<ScanTeam>
{
    // Not a mediator ICommand<> so the assembly scanner skips it during normal scanning;
    // we exercise the dual-mode rejection invariant by calling the internal helper directly.
    public IResult Authorize(Actor actor, ScanTeam resource) => Result.Ok();
    public IResult Authorize(Actor actor, IReadOnlyList<ScanTeam> owners) => Result.Ok();
}

internal sealed class ScanMatch(string id, string team1Id, string team2Id)
    : IIdentifyRelatedResources<ScanTeam, string>
{
    public string Id { get; } = id;
    public string Team1Id { get; } = team1Id;
    public string Team2Id { get; } = team2Id;
    public IReadOnlyList<string> GetRelatedResourceIds() => [Team1Id, Team2Id];
}

internal sealed class ScanTeam(string id, string createdByActorId)
{
    public string Id { get; } = id;
    public string CreatedByActorId { get; } = createdByActorId;
}

internal sealed class FakeMatchRepo
{
    private readonly Dictionary<string, ScanMatch> _items = [];
    public void Add(ScanMatch m) => _items[m.Id] = m;
    public Result<ScanMatch> GetById(string id) => _items.TryGetValue(id, out var v)
        ? Result.Ok(v)
        : Result.Fail<ScanMatch>(new Error.NotFound(new ResourceRef("ScanMatch", id)));
}

internal sealed class FakeTeamRepo
{
    private readonly Dictionary<string, ScanTeam> _items = [];
    public void Add(ScanTeam t) => _items[t.Id] = t;
    public Result<ScanTeam> GetById(string id) => _items.TryGetValue(id, out var v)
        ? Result.Ok(v)
        : Result.Fail<ScanTeam>(new Error.NotFound(new ResourceRef("ScanTeam", id)));
}

internal sealed class ScannedMatchLoader(FakeMatchRepo repo) : SharedResourceLoaderById<ScanMatch, string>
{
    public override Task<Result<ScanMatch>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(repo.GetById(id));
}

internal sealed class ScannedTeamLoader(FakeTeamRepo repo) : SharedResourceLoaderById<ScanTeam, string>
{
    public override Task<Result<ScanTeam>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(repo.GetById(id));
}

/// <summary>
/// Dual-mode fixture for direct-overload testing. Implements both authorization markers
/// plus <see cref="IMessage"/> (required by the explicit-overload generic constraints) but
/// deliberately omits <see cref="ICommand{T}"/> so the assembly scanner skips it during scans
/// triggered by other tests in this file. The dual-mode rejection is exercised by invoking the
/// explicit registration overloads directly.
/// </summary>
internal sealed record DualModeNonCommand(string Id)
    : IMessage,
      IAuthorizeResource<ScanTeam>,
      IAuthorizeResourceVia<ScanTeam>
{
    public IResult Authorize(Actor actor, ScanTeam resource) => Result.Ok();
    public IResult Authorize(Actor actor, IReadOnlyList<ScanTeam> owners) => Result.Ok();
}

/// <summary>
/// Fixture that closes <see cref="IAuthorizeResource{T}"/> over two distinct resource types.
/// Used to verify the scanner rejects multi-closed-marker messages instead of silently
/// registering authorization for only the first one discovered. Not a mediator message so
/// assembly scans triggered by other tests in this file skip it.
/// </summary>
internal sealed record MultiClosedAuthResourceFixture(string Id)
    : IAuthorizeResource<ScanTeam>,
      IAuthorizeResource<ScanMatch>
{
    IResult IAuthorizeResource<ScanTeam>.Authorize(Actor actor, ScanTeam resource) => Result.Ok();
    IResult IAuthorizeResource<ScanMatch>.Authorize(Actor actor, ScanMatch resource) => Result.Ok();
}

/// <summary>
/// Fixture that closes <see cref="IAuthorizeResourceVia{TOwner}"/> over two distinct owner
/// types. Symmetric to <see cref="MultiClosedAuthResourceFixture"/>; verifies the scanner
/// rejects ambiguous via-markers.
/// </summary>
internal sealed record MultiClosedViaFixture(string Id)
    : IAuthorizeResourceVia<ScanTeam>,
      IAuthorizeResourceVia<ScanMatch>
{
    IResult IAuthorizeResourceVia<ScanTeam>.Authorize(Actor actor, IReadOnlyList<ScanTeam> owners) => Result.Ok();
    IResult IAuthorizeResourceVia<ScanMatch>.Authorize(Actor actor, IReadOnlyList<ScanMatch> owners) => Result.Ok();
}

#endregion