namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Integration tests for v4 typed-accessor DI registration. Verifies that every public
/// path which registers resource authorization also registers
/// <see cref="IAuthorizedResource{TMessage, TResource}"/> for the corresponding closed pair —
/// so handlers can rely on injection without having to wire it up themselves. Covers all
/// four registration entry points: explicit direct, scan-based, explicit via single-hop,
/// and explicit via multi-hop.
/// </summary>
public class AuthorizedResourceAccessorRegistrationTests
{
    #region Path 1 — explicit AddResourceAuthorization<TMessage, TResource, TResponse>()

    [Fact]
    public void AddResourceAuthorization_typed_registers_accessor_for_closed_pair()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<RegTestDirectCommand, RegTestResource, Result<string>>();

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizedResource<RegTestDirectCommand, RegTestResource>)
            && d.ImplementationType == typeof(AuthorizedResourceHolder<RegTestDirectCommand, RegTestResource>)
            && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddResourceAuthorization_typed_called_twice_registers_accessor_exactly_once()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization<RegTestDirectCommand, RegTestResource, Result<string>>();
        services.AddResourceAuthorization<RegTestDirectCommand, RegTestResource, Result<string>>();

        services
            .Count(d => d.ServiceType == typeof(IAuthorizedResource<RegTestDirectCommand, RegTestResource>))
            .Should().Be(1, "TryAddScoped must be idempotent on repeat registration");
    }

    [Fact]
    public void AddResourceAuthorization_typed_resolves_holder_via_accessor_interface()
    {
        var services = new ServiceCollection();
        services.AddResourceAuthorization<RegTestDirectCommand, RegTestResource, Result<string>>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var accessor = scope.ServiceProvider
            .GetRequiredService<IAuthorizedResource<RegTestDirectCommand, RegTestResource>>();

        accessor.Should().BeOfType<AuthorizedResourceHolder<RegTestDirectCommand, RegTestResource>>();
    }

    [Fact]
    public void AddResourceAuthorization_typed_accessor_reads_pushed_resource()
    {
        // End-to-end DI proof: resolve via the interface, push via the static helper on the
        // holder closed type (mirrors what the pipeline behavior does), assert the resolved
        // accessor reads the same instance.
        var services = new ServiceCollection();
        services.AddResourceAuthorization<RegTestDirectCommand, RegTestResource, Result<string>>();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var accessor = scope.ServiceProvider
            .GetRequiredService<IAuthorizedResource<RegTestDirectCommand, RegTestResource>>();

        var resource = new RegTestResource("r");
        using (AuthorizedResourceHolder<RegTestDirectCommand, RegTestResource>.Push(resource))
        {
            accessor.GetRequiredResource().Should().BeSameAs(resource);
        }

        accessor.TryGetResource(out _).Should().BeFalse("after dispose the accessor is empty again");
    }

    #endregion

    #region Path 2 — scan-based AddResourceAuthorization(Assembly[])

    [Fact]
    public void AddResourceAuthorization_scan_registers_accessor_for_each_authorize_resource_command()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(RegTestDirectCommand).Assembly);

        // The assembly contains both RegTestDirectCommand (IAuthorizeResource<RegTestResource>)
        // and RegTestViaCommand (IAuthorizeResourceVia<RegTestOwner> + IIdentifyResource<RegTestLeaf,>).
        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizedResource<RegTestDirectCommand, RegTestResource>),
            "direct-path scan must register the accessor for the resource type");

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizedResource<RegTestViaCommand, RegTestLeaf>),
            "via-path scan must register the accessor for the LEAF type (mutation target), not the owner");

        services.Should().NotContain(d =>
            d.ServiceType == typeof(IAuthorizedResource<RegTestViaCommand, RegTestOwner>),
            "via-path scan must NOT register the accessor for the owner — v4 deliberately defers the owner accessor");
    }

    #endregion

    #region Path 3 — explicit single-hop AddRelatedResourceAuthorization<,,,,,>(extractOwnerId)

    [Fact]
    public void AddRelatedResourceAuthorization_singleHop_registers_accessor_for_leaf()
    {
        var services = new ServiceCollection();

        services.AddRelatedResourceAuthorization<
            RegTestViaCommand, RegTestLeaf, string, RegTestOwner, string, Result<string>>(
            extractOwnerId: leaf => leaf.OwnerId);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizedResource<RegTestViaCommand, RegTestLeaf>)
            && d.ImplementationType == typeof(AuthorizedResourceHolder<RegTestViaCommand, RegTestLeaf>)
            && d.Lifetime == ServiceLifetime.Scoped,
            "the single-hop helper delegates to the multi-hop overload, which registers the leaf accessor");
    }

    #endregion

    #region Path 4 — explicit multi-hop AddRelatedResourceAuthorization<,,,>(path)

    [Fact]
    public void AddRelatedResourceAuthorization_path_registers_accessor_for_leaf()
    {
        var services = new ServiceCollection();
        var path = BuildSingleHopPath();

        services.AddRelatedResourceAuthorization<
            RegTestViaCommand, RegTestLeaf, RegTestOwner, Result<string>>(path);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizedResource<RegTestViaCommand, RegTestLeaf>)
            && d.ImplementationType == typeof(AuthorizedResourceHolder<RegTestViaCommand, RegTestLeaf>)
            && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddRelatedResourceAuthorization_path_called_twice_registers_accessor_exactly_once()
    {
        var services = new ServiceCollection();
        var path = BuildSingleHopPath();

        services.AddRelatedResourceAuthorization<
            RegTestViaCommand, RegTestLeaf, RegTestOwner, Result<string>>(path);
        services.AddRelatedResourceAuthorization<
            RegTestViaCommand, RegTestLeaf, RegTestOwner, Result<string>>(path);

        services
            .Count(d => d.ServiceType == typeof(IAuthorizedResource<RegTestViaCommand, RegTestLeaf>))
            .Should().Be(1, "TryAddScoped must be idempotent on repeat registration");
    }

    #endregion

    #region Test fixtures

    private static ResolvedAuthorizationPath BuildSingleHopPath()
    {
        var hop = new ResolvedAuthorizationHop(
            fromType: typeof(RegTestLeaf),
            toType: typeof(RegTestOwner),
            toIdType: typeof(string),
            extractIds: src => [((RegTestLeaf)src).OwnerId],
            loadAsync: (_, id, _) =>
                Task.FromResult(HopLoadResult.Success(new RegTestOwner((string)id))),
            isPlural: false);

        return new ResolvedAuthorizationPath(
            messageType: typeof(RegTestViaCommand),
            leafType: typeof(RegTestLeaf),
            ownerType: typeof(RegTestOwner),
            hops: [hop]);
    }

    #endregion
}

// Public so they're visible to assembly-scan in path-2 test.
public sealed record RegTestResource(string Id);

public sealed record RegTestLeaf(string Id, string OwnerId)
    : IIdentifyRelatedResource<RegTestOwner, string>
{
    public string GetRelatedResourceId() => OwnerId;
}

public sealed record RegTestOwner(string Id);

public sealed record RegTestDirectCommand(string ResourceId)
    : ICommand<Result<string>>, IAuthorizeResource<RegTestResource>
{
    public IResult Authorize(Actor actor, RegTestResource resource) => Result.Ok();
}

public sealed record RegTestViaCommand(string LeafId)
    : ICommand<Result<string>>,
      IAuthorizeResourceVia<RegTestOwner>,
      IIdentifyResource<RegTestLeaf, string>
{
    public string GetResourceId() => LeafId;
    public IResult Authorize(Actor actor, IReadOnlyList<RegTestOwner> owners) => Result.Ok();
}
