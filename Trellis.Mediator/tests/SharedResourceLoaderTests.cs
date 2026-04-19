using Trellis.Testing;
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
    public void Scanning_DoesNotOverridePreRegisteredLoader()
    {
        var services = new ServiceCollection();

        // Pre-register a factory loader BEFORE scanning
        services.AddScoped<IResourceLoader<SharedReturnCommand, SharedOrder>>(
            _ => new TestSharedOrderInlineLoader());

        services.AddResourceAuthorization(typeof(SharedCancelCommand).Assembly);

        // Scanning should NOT add another registration for the same service type
        // (neither the scanned concrete type nor the adapter)
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IResourceLoader<SharedReturnCommand, SharedOrder>))
            .ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationFactory.Should().NotBeNull("the pre-registered factory should be the only registration");
    }

    [Fact]
    public void AddSharedResourceLoader_DoesNotOverrideExistingLoader()
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedResourceLoaderById<SharedOrder, string>, TestSharedOrderLoader>();

        // Pre-register an explicit loader
        services.AddScoped<IResourceLoader<SharedReturnCommand, SharedOrder>>(
            _ => new TestSharedOrderInlineLoader());

        // AddSharedResourceLoader should not override it
        services.AddSharedResourceLoader<SharedReturnCommand, SharedOrder, string>();

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IResourceLoader<SharedReturnCommand, SharedOrder>))
            .ToList();

        descriptors.Should().ContainSingle();
        descriptors[0].ImplementationFactory.Should().NotBeNull("the pre-registered factory should be the only registration");
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
        result.Unwrap().Id.Should().Be("order-1");
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
        result.Unwrap().Id.Should().Be("order-42");
    }

    #endregion

    #region Test helpers

    public sealed record SharedOrder(string Id, string OwnerId);

    // -- Shared loader (one per resource type)

    public sealed class TestSharedOrderLoader : SharedResourceLoaderById<SharedOrder, string>
    {
        public override Task<Result<SharedOrder>> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(id, "owner-1")));
    }

    // -- Commands that use IIdentifyResource (should be auto-bridged)

    public sealed record SharedCancelCommand(string OrderId)
        : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    public sealed record SharedReturnCommand(string OrderId)
        : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    // -- Command WITHOUT IIdentifyResource (should NOT be auto-bridged)

    public sealed record NonIdentifyCommand(string OrderId)
        : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<SharedOrder>
    {
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    // -- Command with explicit loader (explicit should win)

    public sealed record ExplicitLoaderCommand(string OrderId)
        : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, string>
    {
        public string GetResourceId() => OrderId;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    public sealed class ExplicitOrderLoader : IResourceLoader<ExplicitLoaderCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(ExplicitLoaderCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(message.OrderId, "explicit-owner")));
    }

    // -- Loader for pre-registration tests (scanning will also discover this via Assembly.GetTypes(),
    //    but TryAddScoped ensures the pre-registered factory wins)

    private sealed class TestSharedOrderInlineLoader : IResourceLoader<SharedReturnCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(SharedReturnCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(message.OrderId, "inline")));
    }

    // -- Command with mismatched TId (shared loader uses string, command uses int)

    public sealed record MismatchedIdCommand(int OrderNumber)
        : ICommand<Result<Trellis.Unit>>, IAuthorizeResource<SharedOrder>, IIdentifyResource<SharedOrder, int>
    {
        public int GetResourceId() => OrderNumber;
        public IResult Authorize(Actor actor, SharedOrder order) => Result.Ok();
    }

    #endregion
}
