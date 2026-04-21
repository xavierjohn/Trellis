namespace Trellis.Asp.Authorization.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddEntraActorProvider"/>
/// and <see cref="ServiceCollectionExtensions.AddClaimsActorProvider"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEntraActorProvider_RegistersIActorProvider()
    {
        var services = new ServiceCollection();

        services.AddEntraActorProvider();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IActorProvider));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be<EntraActorProvider>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddEntraActorProvider_RegistersHttpContextAccessor()
    {
        var services = new ServiceCollection();

        services.AddEntraActorProvider();

        services.Should().Contain(d =>
            d.ServiceType == typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor));
    }

    [Fact]
    public void AddEntraActorProvider_RegistersDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddEntraActorProvider();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EntraActorOptions>>();

        options.Value.Should().NotBeNull();
        options.Value.IdClaimType.Should().Contain("objectidentifier");
    }

    [Fact]
    public void AddEntraActorProvider_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddEntraActorProvider(opts => opts.IdClaimType = "sub");
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EntraActorOptions>>();

        options.Value.IdClaimType.Should().Be("sub");
    }

    [Fact]
    public void AddClaimsActorProvider_RegistersIActorProvider()
    {
        var services = new ServiceCollection();

        services.AddClaimsActorProvider();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IActorProvider));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be<ClaimsActorProvider>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddClaimsActorProvider_RegistersHttpContextAccessor()
    {
        var services = new ServiceCollection();

        services.AddClaimsActorProvider();

        services.Should().Contain(d =>
            d.ServiceType == typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor));
    }

    [Fact]
    public void AddClaimsActorProvider_RegistersDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ClaimsActorOptions>>();

        options.Value.Should().NotBeNull();
        options.Value.ActorIdClaim.Should().Be("sub");
        options.Value.PermissionsClaim.Should().Be("permissions");
    }

    [Fact]
    public void AddClaimsActorProvider_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider(opts =>
        {
            opts.ActorIdClaim = "oid";
            opts.PermissionsClaim = "roles";
        });
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ClaimsActorOptions>>();

        options.Value.ActorIdClaim.Should().Be("oid");
        options.Value.PermissionsClaim.Should().Be("roles");
    }

    [Fact]
    public void AddCachingActorProvider_RegistersIActorProviderAsCachingDecorator()
    {
        var services = new ServiceCollection();

        services.AddCachingActorProvider<FakeActorProvider>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IActorProvider));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddCachingActorProvider_RegistersInnerProviderAsScoped()
    {
        var services = new ServiceCollection();

        services.AddCachingActorProvider<FakeActorProvider>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(FakeActorProvider));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    private sealed class FakeActorProvider : IActorProvider
    {
        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Actor.Create("test", new HashSet<string>()));
    }
}