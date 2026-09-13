namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trellis.Mediator;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class OutboxRegistrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartAsync_DomainOnlyHost_DoesNotRequireIntegrationPublisher(bool tracked, bool supportsProbe)
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxTestDbContext>(options => options.UseSqlite(connection));
        if (tracked)
            services.AddTrackedAggregateDomainEventDispatch();
        else
            services.AddDomainEventDispatch();
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreatedAsync(ct);
        using var relay = CreateRelay(provider, supportsProbe);
        provider.GetService<IIntegrationEventPublisher>().Should().BeNull();
        provider.GetRequiredService<IReportingDomainEventPublisher>()
            .Should().BeSameAs(provider.GetRequiredService<IDomainEventPublisher>());
        await relay.StartAsync(ct);
        await relay.StopAsync(ct);
        (await relay.DrainAsync(ct)).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_MissingReportingPublisher_FailsBeforeBackgroundDrain(bool supportsProbe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var relay = CreateRelay(provider, supportsProbe);
        var act = () => relay.StartAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IReportingDomainEventPublisher*");
        relay.ExecuteTask.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_TranslationEnabledWithoutIntegrationPublisher_FailsBeforeBackgroundDrain(bool supportsProbe)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainEventDispatch();
        services.AddIntegrationEventDispatch();
        services.RemoveAll<IIntegrationEventPublisher>();
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var relay = CreateRelay(provider, supportsProbe);
        var act = () => relay.StartAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IIntegrationEventPublisher*");
        relay.ExecuteTask.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartAsync_IntegrationPublisher_ConstructsOnceAndDisposesAsync(
        bool supportsProbe, bool registerCollector)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainEventDispatch();
        services.AddIntegrationEventDispatch();
        if (!registerCollector)
            services.RemoveAll<IIntegrationEventCollector>();
        services.RemoveAll<IIntegrationEventPublisher>();
        var instances = new List<StartupIntegrationPublisher>();
        services.AddTransient<IIntegrationEventPublisher>(_ =>
        {
            var instance = new StartupIntegrationPublisher();
            instances.Add(instance);
            return instance;
        });
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var relay = CreateRelay(provider, supportsProbe);

        await relay.StartAsync(TestContext.Current.CancellationToken);
        await relay.StopAsync(TestContext.Current.CancellationToken);

        instances.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartAsync_IntegrationPublisherFactoryThrows_PropagatesAndDisposesScope(
        bool supportsProbe, bool registerCollector)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainEventDispatch();
        services.AddIntegrationEventDispatch();
        if (!registerCollector)
            services.RemoveAll<IIntegrationEventCollector>();
        services.RemoveAll<IIntegrationEventPublisher>();
        services.AddScoped<StartupIntegrationPublisher>();
        StartupIntegrationPublisher? created = null;
        var failure = new InvalidOperationException("Publisher construction failed.");
        services.AddScoped<IIntegrationEventPublisher>(scope =>
        {
            created = scope.GetRequiredService<StartupIntegrationPublisher>();
            throw failure;
        });
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var relay = CreateRelay(provider, supportsProbe);

        var act = () => relay.StartAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        created.Should().NotBeNull();
        created!.Disposed.Should().BeTrue();
        relay.ExecuteTask.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_ProviderSupportsProbe_DoesNotConstructCollector()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainEventDispatch();
        services.AddScoped<IIntegrationEventCollector>(_ => throw new InvalidOperationException("Collector constructed."));
        services.AddScoped<IIntegrationEventPublisher, StartupIntegrationPublisher>();
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var relay = CreateRelay(provider, supportsProbe: true);

        await relay.StartAsync(TestContext.Current.CancellationToken);
        await relay.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_NoProbeAndCollectorFactoryThrows_PropagatesFailure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDomainEventDispatch();
        var failure = new InvalidOperationException("Collector construction failed.");
        services.AddScoped<IIntegrationEventCollector>(_ => throw failure);
        services.AddTrellisOutbox<OutboxTestDbContext>();
        await using var provider = services.BuildServiceProvider();
        using var relay = CreateRelay(provider, supportsProbe: false);

        var act = () => relay.StartAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        relay.ExecuteTask.Should().BeNull();
    }

    [Fact]
    public void AddTrellisOutbox_called_twice_registers_a_single_relay()
    {
        var services = new ServiceCollection();

        services.AddTrellisOutbox<OutboxTestDbContext>();
        services.AddTrellisOutbox<OutboxTestDbContext>();

        services.Count(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(OutboxRelay<OutboxTestDbContext>))
            .Should().Be(1);
    }

    [Fact]
    public void AddTrellisOutbox_called_twice_applies_the_second_configure()
    {
        var services = new ServiceCollection();

        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 11);
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.MaxAttempts = 7);

        var options = services.BuildServiceProvider().GetRequiredService<OutboxOptions>();

        options.BatchSize.Should().Be(11);
        options.MaxAttempts.Should().Be(7);
    }

    [Fact]
    public void AddTrellisOutbox_later_configure_wins_for_the_same_setting()
    {
        var services = new ServiceCollection();

        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 11);
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 22);

        services.BuildServiceProvider().GetRequiredService<OutboxOptions>()
            .BatchSize.Should().Be(22);
    }

    [Fact]
    public void AddTrellisOutbox_for_two_contexts_registers_a_relay_for_each()
    {
        var services = new ServiceCollection();

        services.AddTrellisOutbox<OutboxTestDbContext>();
        services.AddTrellisOutbox<SecondOutboxTestDbContext>();

        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(2);
    }

    [Fact]
    public void AddTrellisOutbox_validates_the_accumulated_options()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddTrellisOutbox_failed_second_configure_leaves_earlier_options_intact()
    {
        var services = new ServiceCollection();
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 11);

        var act = () => services.AddTrellisOutbox<OutboxTestDbContext>(o =>
        {
            o.MaxAttempts = 3;
            o.BatchSize = 0; // invalid — Validate() must reject the whole layer
        });

        act.Should().Throw<ArgumentOutOfRangeException>();

        var options = services.BuildServiceProvider().GetRequiredService<OutboxOptions>();
        options.BatchSize.Should().Be(11, "a rejected registration must not be partially applied");
        options.MaxAttempts.Should().Be(10, "the earlier valid state must survive intact");
    }

    [Fact]
    public void OutboxOptions_Clone_copies_every_public_settable_property()
    {
        // Guards the hand-written Clone() against a property being added and not copied,
        // which would silently drop that setting on a repeated registration.
        var source = new OutboxOptions
        {
            PollInterval = TimeSpan.FromSeconds(17),
            BatchSize = 33,
            MaxAttempts = 4,
            LeaseDuration = TimeSpan.FromMinutes(9),
            RetryBackoff = TimeSpan.FromSeconds(11),
            MaxRetryBackoff = TimeSpan.FromMinutes(44),
            RetryBackoffJitter = 0.25,
        };

        var clone = source.Clone();

        var defaults = new OutboxOptions();
        foreach (var property in typeof(OutboxOptions).GetProperties().Where(p => p.CanWrite && p.CanRead))
        {
            property.GetValue(source).Should().NotBe(property.GetValue(defaults),
                $"the test must set {property.Name} to a non-default value for the assertion below to be meaningful");
            property.GetValue(clone).Should().Be(property.GetValue(source),
                $"OutboxOptions.Clone() must copy {property.Name}");
        }
    }

    [Fact]
    public void AddTrellisOutbox_with_configure_throws_when_a_factory_owns_the_options_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new OutboxOptions { BatchSize = 99 });

        var act = () => services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 11);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered by a factory or implementation type*");
    }

    [Fact]
    public void AddTrellisOutbox_without_configure_leaves_a_consumer_owned_options_registration_intact()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new OutboxOptions { BatchSize = 99 });

        services.AddTrellisOutbox<OutboxTestDbContext>();

        services.Count(d => d.ServiceType == typeof(OutboxOptions)).Should().Be(1);
        services.BuildServiceProvider().GetRequiredService<OutboxOptions>().BatchSize.Should().Be(99);
    }

    [Fact]
    public void AddTrellisOutbox_configures_the_options_instance_the_container_actually_resolves()
    {
        // A later descriptor wins in MS DI, so the helper must layer onto the last one rather than
        // configuring an earlier instance the relay would never receive.
        var services = new ServiceCollection();
        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 11);
        services.AddSingleton(new OutboxOptions { BatchSize = 99 });

        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.MaxAttempts = 7);

        var options = services.BuildServiceProvider().GetRequiredService<OutboxOptions>();
        options.BatchSize.Should().Be(99);
        options.MaxAttempts.Should().Be(7);
    }

    [Fact]
    public void AddTrellisOutbox_ignores_keyed_options_registrations()
    {
        // Keyed descriptors take no part in unkeyed resolution, and reading ImplementationInstance
        // on one throws — so they must not be mistaken for the relay's options registration.
        var services = new ServiceCollection();
        services.AddKeyedSingleton("other", new OutboxOptions { BatchSize = 99 });

        services.AddTrellisOutbox<OutboxTestDbContext>(o => o.BatchSize = 11);

        services.BuildServiceProvider().GetRequiredService<OutboxOptions>().BatchSize.Should().Be(11);
    }

    private static OutboxRelay<OutboxTestDbContext> CreateRelay(ServiceProvider provider, bool supportsProbe) =>
        new(
            new CapabilityScopeFactory(provider.GetRequiredService<IServiceScopeFactory>(), supportsProbe),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<OutboxOptions>(),
            provider.GetRequiredService<ILogger<OutboxRelay<OutboxTestDbContext>>>());

    private sealed class CapabilityScopeFactory(IServiceScopeFactory inner, bool supportsProbe) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new CapabilityScope(inner.CreateScope(), supportsProbe);
    }

    private sealed class CapabilityScope(IServiceScope inner, bool supportsProbe) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new CapabilityProvider(inner.ServiceProvider, supportsProbe);
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => new AsyncServiceScope(inner).DisposeAsync();
    }

    private sealed class CapabilityProvider(IServiceProvider inner, bool supportsProbe) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceProviderIsService) && !supportsProbe
                ? null
                : inner.GetService(serviceType);
    }

    private sealed class StartupIntegrationPublisher : IIntegrationEventPublisher, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask PublishAsync(OutboundIntegrationMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Startup must not publish messages.");

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondOutboxTestDbContext(DbContextOptions<SecondOutboxTestDbContext> options)
        : DbContext(options);
}