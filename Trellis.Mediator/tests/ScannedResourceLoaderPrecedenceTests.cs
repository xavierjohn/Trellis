namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;
using Trellis.Testing;
using Unit = global::Mediator.Unit;
using static Trellis.Mediator.Tests.SharedResourceLoaderTests;

public class ScannedResourceLoaderPrecedenceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Scan_TypedSharedRegistration_CustomLoaderWinsInEitherOrder(bool loadersOnly, bool scanFirst)
    {
        var services = new ServiceCollection();
        if (scanFirst)
            Scan(services, loadersOnly);

        services.AddSharedResourceAuthorization<ExplicitLoaderCommand, SharedOrder, string, Result<Unit>>();

        if (!scanFirst)
            Scan(services, loadersOnly);

        Scan(services, loadersOnly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>();
        var result = await loader.LoadAsync(new ExplicitLoaderCommand("order-42"), TestContext.Current.CancellationToken);

        loader.Should().BeOfType<ExplicitOrderLoader>();
        result.Unwrap().Should().Be(new SharedOrder("order-42", "explicit-owner"));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IResourceLoader<ExplicitLoaderCommand, SharedOrder>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scan_SharedAdapterWithKeyedLoader_ReplacesOnlyUnkeyedAdapter(bool loadersOnly)
    {
        var services = new ServiceCollection();
        var keyedLoader = new ConfiguredLoader<int>();
        services.AddKeyedSingleton<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>("other", keyedLoader);
        services.AddSharedResourceAuthorization<ExplicitLoaderCommand, SharedOrder, string, Result<Unit>>();

        Scan(services, loadersOnly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>();
        var result = await loader.LoadAsync(new ExplicitLoaderCommand("order-42"), TestContext.Current.CancellationToken);

        loader.Should().BeOfType<ExplicitOrderLoader>();
        result.Unwrap().OwnerId.Should().Be("explicit-owner");
        scope.ServiceProvider.GetRequiredKeyedService<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>("other")
            .Should().BeSameAs(keyedLoader);
        services.Count(d => d.ServiceType == typeof(IResourceLoader<ExplicitLoaderCommand, SharedOrder>)).Should().Be(2);
    }

    [Theory]
    [InlineData(false, false, "type")]
    [InlineData(false, false, "factory")]
    [InlineData(false, false, "instance")]
    [InlineData(false, true, "type")]
    [InlineData(false, true, "factory")]
    [InlineData(false, true, "instance")]
    [InlineData(true, false, "type")]
    [InlineData(true, false, "factory")]
    [InlineData(true, false, "instance")]
    [InlineData(true, true, "type")]
    [InlineData(true, true, "factory")]
    [InlineData(true, true, "instance")]
    public async Task Scan_ApplicationRegistration_PreservesDescriptorAndRemovesFallback(
        bool loadersOnly, bool adapterFirst, string registrationKind)
    {
        IServiceCollection services = new ServiceCollection();
        if (adapterFirst)
            services.AddSharedResourceAuthorization<ExplicitLoaderCommand, SharedOrder, string, Result<Unit>>();

        var applicationDescriptor = registrationKind switch
        {
            "type" => ServiceDescriptor.Scoped<IResourceLoader<ExplicitLoaderCommand, SharedOrder>, ConfiguredLoader<int>>(),
            "factory" => ServiceDescriptor.Scoped<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>(_ => new ConfiguredLoader<int>()),
            "instance" => ServiceDescriptor.Singleton<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>(new ConfiguredLoader<int>()),
            _ => throw new ArgumentOutOfRangeException(nameof(registrationKind)),
        };
        services.Add(applicationDescriptor);

        if (!adapterFirst)
            services.AddSharedResourceAuthorization<ExplicitLoaderCommand, SharedOrder, string, Result<Unit>>();

        Scan(services, loadersOnly);
        Scan(services, loadersOnly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var loader = scope.ServiceProvider.GetRequiredService<IResourceLoader<ExplicitLoaderCommand, SharedOrder>>();
        var result = await loader.LoadAsync(new ExplicitLoaderCommand("order-42"), TestContext.Current.CancellationToken);

        result.Unwrap().OwnerId.Should().Be("configured-owner");
        services.Single(d => d.ServiceType == typeof(IResourceLoader<ExplicitLoaderCommand, SharedOrder>))
            .Should().BeSameAs(applicationDescriptor);
    }

    private static void Scan(IServiceCollection services, bool loadersOnly)
    {
        if (loadersOnly)
            services.AddResourceLoaders(typeof(ExplicitOrderLoader).Assembly);
        else
            services.AddResourceAuthorization(typeof(ExplicitOrderLoader).Assembly);
    }

    // Open-generic so scanning does not discover this application-registration test double.
    private sealed class ConfiguredLoader<T> : IResourceLoader<ExplicitLoaderCommand, SharedOrder>
    {
        public Task<Result<SharedOrder>> LoadAsync(ExplicitLoaderCommand message, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Ok(new SharedOrder(message.OrderId, "configured-owner")));
    }
}
