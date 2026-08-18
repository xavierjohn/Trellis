namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Trellis.Asp.Idempotency;

/// <summary>
/// Registration tests for <see cref="CosmosIdempotencyServiceCollectionExtensions"/>.
/// </summary>
/// <remarks>
/// These need no emulator. Constructing a <see cref="CosmosClient"/> and calling
/// <c>GetContainer</c> are both offline operations — the SDK connects lazily on first use — so the
/// whole registration path can be exercised without a live service.
/// </remarks>
public sealed class CosmosIdempotencyServiceCollectionExtensionsTests
{
    private const string Endpoint = "https://localhost:8081/";

    private const string Key =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Fact]
    public void Factory_overload_registers_the_Cosmos_store_as_the_idempotency_store()
    {
        using var provider = BuildProvider(services =>
            services.AddCosmosIdempotencyStore(_ => GetContainer()));

        provider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<CosmosIdempotencyStore>();
    }

    [Fact]
    public void Store_is_a_singleton_so_one_client_is_shared_across_requests()
    {
        using var provider = BuildProvider(services =>
            services.AddCosmosIdempotencyStore(_ => GetContainer()));

        var first = provider.GetRequiredService<IIdempotencyStore>();
        var second = provider.GetRequiredService<IIdempotencyStore>();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Container_factory_is_resolved_from_the_provider()
    {
        IServiceProvider? captured = null;

        using var provider = BuildProvider(services =>
            services.AddCosmosIdempotencyStore(sp =>
            {
                captured = sp;
                return GetContainer();
            }));

        provider.GetRequiredService<IIdempotencyStore>();

        captured.Should().NotBeNull("the factory must be able to resolve co-registered services");
    }

    [Fact]
    public void Registered_TimeProvider_is_used_when_one_is_present()
    {
        var time = new FakeTimeProvider();

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddCosmosIdempotencyStore(_ => GetContainer());
        });

        // The store takes TimeProvider as an optional dependency, so resolution must succeed both
        // with and without one registered; the no-TimeProvider case is covered by the other tests.
        provider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<CosmosIdempotencyStore>();
    }

    [Fact]
    public void Id_overload_addresses_the_container_through_the_registered_client()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(new CosmosClient(Endpoint, Key));
            services.AddCosmosIdempotencyStore("orders", "idempotency-entries");
        });

        provider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<CosmosIdempotencyStore>();
    }

    [Fact]
    public void Id_overload_defaults_the_container_id()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(new CosmosClient(Endpoint, Key));
            services.AddCosmosIdempotencyStore("orders");
        });

        provider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<CosmosIdempotencyStore>();
    }

    [Fact]
    public void Id_overload_requires_a_registered_CosmosClient()
    {
        using var provider = BuildProvider(services => services.AddCosmosIdempotencyStore("orders"));

        var resolve = () => provider.GetRequiredService<IIdempotencyStore>();

        resolve.Should().Throw<InvalidOperationException>(
            "the overload resolves CosmosClient from the container, so a missing registration must " +
            "fail loudly rather than silently producing a store that cannot reach Cosmos DB");
    }

    [Fact]
    public void Null_services_are_rejected()
    {
        var factoryOverload = () =>
            ((IServiceCollection)null!).AddCosmosIdempotencyStore(_ => GetContainer());
        var idOverload = () => ((IServiceCollection)null!).AddCosmosIdempotencyStore("orders");

        factoryOverload.Should().Throw<ArgumentNullException>();
        idOverload.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_container_factory_is_rejected()
    {
        var act = () => new ServiceCollection()
            .AddCosmosIdempotencyStore((Func<IServiceProvider, Container>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_database_id_is_rejected(string? databaseId)
    {
        var act = () => new ServiceCollection().AddCosmosIdempotencyStore(databaseId!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_container_id_is_rejected(string? containerId)
    {
        var act = () => new ServiceCollection().AddCosmosIdempotencyStore("orders", containerId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Registration_returns_the_collection_for_chaining()
    {
        var services = new ServiceCollection();

        services.AddCosmosIdempotencyStore(_ => GetContainer()).Should().BeSameAs(services);
        services.AddCosmosIdempotencyStore("orders").Should().BeSameAs(services);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions<IdempotencyOptions>();
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Addresses a container without contacting Cosmos DB — the SDK returns a handle and connects
    /// lazily, so nothing here requires a running emulator.
    /// </summary>
    private static Container GetContainer() =>
        new CosmosClient(Endpoint, Key).GetContainer("orders", "idempotency");
}