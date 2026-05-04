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

    [Fact]
    public void AddClaimsActorProvider_after_AddEntraActorProvider_leaves_single_IActorProvider_descriptor()
    {
        // Composition contract: each AddXxxActorProvider helper REPLACES the IActorProvider
        // registration rather than appending. Without this, two helpers leave two descriptors:
        // GetRequiredService<IActorProvider>() silently returns the last-registered one but
        // GetServices<IActorProvider>() exposes both, and the call ordering is invisible to
        // consumers reading composition root code. Replace makes intent explicit.
        var services = new ServiceCollection();
        services.AddEntraActorProvider();
        services.AddClaimsActorProvider();

        var actorProviders = services
            .Where(d => d.ServiceType == typeof(IActorProvider))
            .ToList();
        actorProviders.Should().HaveCount(1, "AddXxxActorProvider must Replace, not append");
    }

    [Fact]
    public void AddEntraActorProvider_after_AddClaimsActorProvider_leaves_single_IActorProvider_descriptor()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddEntraActorProvider();

        services
            .Where(d => d.ServiceType == typeof(IActorProvider))
            .Should().HaveCount(1);
    }

    [Fact]
    public void AddDevelopmentActorProvider_after_AddEntraActorProvider_leaves_single_IActorProvider_descriptor()
    {
        var services = new ServiceCollection();
        services.AddEntraActorProvider();
        services.AddDevelopmentActorProvider();

        services
            .Where(d => d.ServiceType == typeof(IActorProvider))
            .Should().HaveCount(1);
    }

    [Fact]
    public void AddCachingActorProvider_after_AddEntraActorProvider_leaves_single_IActorProvider_descriptor()
    {
        var services = new ServiceCollection();
        services.AddEntraActorProvider();
        services.AddCachingActorProvider<FakeActorProvider>();

        services
            .Where(d => d.ServiceType == typeof(IActorProvider))
            .Should().HaveCount(1, "AddCachingActorProvider must also Replace the IActorProvider registration");
    }

    [Fact]
    public void Last_AddXxxActorProvider_wins_after_replacement()
    {
        // After Replace, the resolved IActorProvider must be the LAST registered.
        // (Same observable last-wins outcome, but achieved via explicit replacement
        // instead of relying on resolution-order semantics over multiple descriptors.)
        // Asserts via descriptor introspection to keep the test isolated from each
        // provider's full constructor dependency graph (e.g. IHostEnvironment).
        var services = new ServiceCollection();
        services.AddEntraActorProvider();
        services.AddClaimsActorProvider();

        var descriptor = services.Single(d => d.ServiceType == typeof(IActorProvider));
        descriptor.ImplementationType.Should().Be<ClaimsActorProvider>();
    }

    [Fact]
    public void AddCachingActorProvider_called_twice_does_not_accumulate_inner_T_descriptors()
    {
        // Composition contract: a library and an app must each be safe to call
        // AddCachingActorProvider<X>() without accumulating duplicate inner-T scoped
        // descriptors. Without TryAddScoped, GetServices<X>()/IEnumerable<X> would
        // expose multiple instances even though IActorProvider (replaced) does not.
        var services = new ServiceCollection();

        services.AddCachingActorProvider<FakeActorProvider>();
        services.AddCachingActorProvider<FakeActorProvider>();

        services
            .Where(d => d.ServiceType == typeof(FakeActorProvider))
            .Should().HaveCount(1, "AddCachingActorProvider must not accumulate duplicate inner-T descriptors");
    }

    private sealed class FakeActorProvider : IActorProvider
    {
        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Actor.Create("test", new HashSet<string>()));
    }
}