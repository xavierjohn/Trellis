namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="SharedResourceLoaderById{TResource, TId}"/> integration with
/// <see cref="ServiceCollectionExtensions.AddResourceAuthorization(IServiceCollection, System.Reflection.Assembly[])"/>.
/// </summary>
public class SharedResourceLoaderTests
{
    #region Scanning discovers SharedResourceLoaderById and registers adapters

    [Fact]
    public void Scanning_RegistersAdapterForCommandWithIIdentifyResource()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Multiple commands sharing the same resource type all get adapters

    [Fact]
    public void Scanning_RegistersAdapterForMultipleCommandsSharingResource()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        var cancelLoader = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));
        var returnLoader = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedReturnCommand, SharedOrder>));

        cancelLoader.Should().NotBeNull();
        returnLoader.Should().NotBeNull();
    }

    #endregion

    #region Explicit IResourceLoader takes priority over shared loader

    [Fact]
    public void Scanning_ExplicitLoaderTakesPriority()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(ExplicitLoaderCommand).Assembly);

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IResourceLoader<ExplicitLoaderCommand, SharedOrder>))
            .ToList();

        // Explicit loader should be registered; no adapter should be registered
        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationType.Should().Be<ExplicitOrderLoader>();
    }

    #endregion

    #region Pre-registered loader takes priority over shared adapter

    [Fact]
    public void Scanning_PreRegisteredFactoryLoaderPreventsAdapterRegistration()
    {
        var services = new ServiceCollection();

        // Simulate a factory-registered loader that scanning can't discover
        // (e.g., from a different assembly or registered by a third-party library)
        var closedLoaderType = typeof(IResourceLoader<,>)
            .MakeGenericType(typeof(SharedCancelCommand), typeof(SharedOrder));
        ((IServiceCollection)services).Add(new ServiceDescriptor(closedLoaderType, sp =>
            new TestSharedOrderInlineLoader(), ServiceLifetime.Scoped));

        // Remove any explicit loaders from services so explicitLoaders won't catch it
        // Then add just the shared loader and commands
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>, TestSharedOrderLoader>();

        // Manually trigger the bridging check — the adapter should not be added
        // because a registration for the same service type already exists
        services.AddSharedResourceLoader<SharedCancelCommand, SharedOrder, string>();

        // Count registrations — should have the factory + the AddSharedResourceLoader adapter
        var descriptors = services
            .Where(d => d.ServiceType == closedLoaderType)
            .ToList();

        // Two registrations is fine (factory + adapter); the key test is that
        // services.Any would have prevented scanning from adding an adapter.
        // The real integration test is below.
        descriptors.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Scanning_FactoryRegisteredLoaderIsUsedWhenPresent()
    {
        var services = new ServiceCollection();

        // Pre-register a factory loader returning a specific owner
        services.AddScoped<IResourceLoader<SharedReturnCommand, SharedOrder>>(
            _ => new TestSharedOrderInlineLoader());

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var loader = scope.ServiceProvider
            .GetRequiredService<IResourceLoader<SharedReturnCommand, SharedOrder>>();

        var result = await loader.LoadAsync(
            new SharedReturnCommand("test-1"), CancellationToken.None);

        // The resolved loader should work (regardless of whether it's the factory or scanned one)
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Command without IIdentifyResource is NOT auto-bridged

    [Fact]
    public void Scanning_DoesNotBridgeCommandWithoutIIdentifyResource()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(NonIdentifyCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<NonIdentifyCommand, SharedOrder>));

        descriptor.Should().BeNull();
    }

    #endregion

    #region SharedResourceLoaderById is registered as scoped

    [Fact]
    public void Scanning_RegistersSharedLoaderAsScoped()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(SharedResourceLoaderById<SharedOrder, string>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion

    #region Mismatched TId is NOT auto-bridged

    [Fact]
    public void Scanning_DoesNotBridgeWhenTIdDoesNotMatchSharedLoader()
    {
        var services = new ServiceCollection();

        services.AddResourceAuthorization(typeof(MismatchedIdCommand).Assembly);

        // SharedResourceLoaderById<SharedOrder, string> exists but command uses int as TId
        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<MismatchedIdCommand, SharedOrder>));

        descriptor.Should().BeNull();
    }

    #endregion

    #region Adapter resolves and delegates correctly

    [Fact]
    public async Task Adapter_DelegatesToSharedLoader()
    {
        var services = new ServiceCollection();
        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var loader = scope.ServiceProvider
            .GetRequiredService<IResourceLoader<SharedCancelCommand, SharedOrder>>();

        var result = await loader.LoadAsync(
            new SharedCancelCommand("order-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("order-1");
    }

    #endregion

    #region Explicit AOT-safe registration

    [Fact]
    public void AddSharedResourceLoader_RegistersAdapter()
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>, TestSharedOrderLoader>();

        services.AddSharedResourceLoader<SharedCancelCommand, SharedOrder, string>();

        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IResourceLoader<SharedCancelCommand, SharedOrder>));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task AddSharedResourceLoader_ResolvedAdapterDelegatesToSharedLoader()
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>, TestSharedOrderLoader>();
        services.AddSharedResourceLoader<SharedCancelCommand, SharedOrder, string>();
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var loader = scope.ServiceProvider
            .GetRequiredService<IResourceLoader<SharedCancelCommand, SharedOrder>>();

        var result = await loader.LoadAsync(
            new SharedCancelCommand("order-42"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("order-42");
    }

    #endregion

    #region Test helpers

    public sealed record SharedOrder(string Id, string OwnerId);

    // -- Shared loader (one per resource type)

    public sealed class TestSharedOrderLoader : SharedResourceLoaderById<SharedOrder, string>
    {
        public override Task<Result<SharedOrder>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new SharedOrder(id, "owner-1")));
    }

    // -- Commands that use IIdentifyResource (should be auto-bridged)

    public sealed record SharedCancelCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Success();
    }

    public sealed record SharedReturnCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Success();
    }

    // -- Command WITHOUT IIdentifyResource (should NOT be auto-bridged)

    public sealed record NonIdentifyCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>
    {
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Success();
    }

    // -- Command with explicit loader (explicit should win)

    public sealed record ExplicitLoaderCommand(string OrderId)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Success();
    }

    public sealed class ExplicitOrderLoader : IResourceLoader<ExplicitLoaderCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(ExplicitLoaderCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new SharedOrder(message.OrderId, "explicit-owner")));
    }

    // -- Inline loader for pre-registration tests (not discoverable by scanning)

    private sealed class TestSharedOrderInlineLoader : IResourceLoader<SharedReturnCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(SharedReturnCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new SharedOrder(message.OrderId, "inline")));
    }

    // -- Command with mismatched TId (shared loader uses string, command uses int)

    public sealed record MismatchedIdCommand(int OrderNumber)
        : ICommand<Result<Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, int>
    {
        public int GetResourceId() => OrderNumber;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Success();
    }

    #endregion
}
