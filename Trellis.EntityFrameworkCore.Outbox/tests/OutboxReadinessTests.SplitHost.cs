namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Trellis.Mediator;

public sealed partial class OutboxReadinessTests
{
    [Fact]
    public void CreateMetadataOnlyProfile_ExplicitWorkerMaxAttempts_PreservesImmutableSelection()
    {
        var local = OutboxReadinessOptions.CreateStagedOperationalProfile();
        var metadata = OutboxReadinessOptions.CreateMetadataOnlyProfile(3);
        var copy = metadata with { PendingDegradedThreshold = 100 };

        local.RelayMonitoring.Should().Be(OutboxRelayMonitoring.LocalRelay);
        local.WorkerMaxAttempts.Should().BeNull();
        metadata.RelayMonitoring.Should().Be(OutboxRelayMonitoring.Disabled);
        metadata.WorkerMaxAttempts.Should().Be(3);
        copy.RelayMonitoring.Should().Be(OutboxRelayMonitoring.Disabled);
        copy.WorkerMaxAttempts.Should().Be(3);
        typeof(OutboxReadinessOptions).GetProperty(nameof(OutboxReadinessOptions.RelayMonitoring))!
            .CanWrite.Should().BeFalse();
        typeof(OutboxReadinessOptions).GetProperty(nameof(OutboxReadinessOptions.WorkerMaxAttempts))!
            .CanWrite.Should().BeFalse();
        metadata.Should().Be(OutboxReadinessOptions.CreateMetadataOnlyProfile(3));
        metadata.Should().NotBe(OutboxReadinessOptions.CreateMetadataOnlyProfile(4));
        metadata.Should().NotBe(local);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateMetadataOnlyProfile_InvalidWorkerMaxAttempts_ThrowsWithParameterName(int workerMaxAttempts)
    {
        var act = () => OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(workerMaxAttempts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddTrellisOutboxReadiness_ModeOrWorkerMaxAttemptsConflict_PreservesFirstSelection(bool modeConflict)
    {
        var services = new ServiceCollection();
        var first = OutboxReadinessOptions.CreateMetadataOnlyProfile(3);
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(first);
        var originalCount = services.Count;
        var conflicting = modeConflict
            ? OutboxReadinessOptions.CreateStagedOperationalProfile()
            : OutboxReadinessOptions.CreateMetadataOnlyProfile(4);

        var act = () => services.AddTrellisOutboxReadiness<OutboxTestDbContext>(conflicting);

        act.Should().Throw<InvalidOperationException>();
        services.Should().HaveCount(originalCount);
        services.Single(d => d.ServiceType == typeof(OutboxReadinessOptions)).ImplementationInstance.Should().Be(first);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddTrellisOutboxReadiness_MetadataOnlyDifferentRelayContext_AllowsEitherOrder(bool readinessFirst)
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateMetadataOnlyProfile(3);
        if (readinessFirst)
            services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        services.AddTrellisOutbox<OtherOutboxContext>();
        if (!readinessFirst)
            services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);

        services.Should().NotContain(d => d.ServiceType == typeof(OutboxRelayObservations<OutboxTestDbContext>));
        var conflict = () => services.AddTrellisOutboxReadiness<OtherOutboxContext>(profile);
        conflict.Should().Throw<InvalidOperationException>().WithMessage("*one*outbox readiness*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddTrellisOutboxReadiness_EqualSelections_RegisterExactlyOneValidator(bool metadataOnly)
    {
        var services = new ServiceCollection();
        var profile = SelectProfile(metadataOnly);
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile with { });

        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(OutboxReadinessRegistrationValidator<OutboxTestDbContext>));
    }

    [Fact]
    public async Task CheckHealthAsync_MetadataOnlyWithoutRelayDependencies_ReportsOnlyMetadata()
    {
        await using var fixture = await Fixture.CreateAsync(OutboxReadinessOptions.CreateMetadataOnlyProfile(3));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Outbox metadata is healthy.");
        result.Data["profile"].Should().Be("metadata-only-reference");
        result.Data["sampling"].Should().Be("separate-metadata-queries");
        result.Data["relayMonitoring"].Should().Be("disabled");
        result.Data["maxAttempts"].Should().Be(3);
        result.Data["maxAttemptsSource"].Should().Be("profile");
        result.Data["captureAge"].Should().Be("disabled");
        result.Data["consumerLag"].Should().Be("disabled");
        result.Data.Keys.Should().NotContain(key =>
            (key.StartsWith("relay", StringComparison.Ordinal) && key != "relayMonitoring")
            || key.StartsWith("lastDurable", StringComparison.Ordinal)
            || key.StartsWith("lastPositive", StringComparison.Ordinal)
            || key == "deliveredRows"
            || key == "startupGracePeriod");
        Reasons(result).Should().BeEmpty();
        fixture.Provider.GetService<OutboxOptions>().Should().BeNull();
        fixture.Provider.GetService<OutboxRelayObservations<OutboxTestDbContext>>().Should().BeNull();
        fixture.Provider.GetServices<IHostedService>().Should().NotContain(s => s is OutboxRelay<OutboxTestDbContext>);
        fixture.Probe.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckHealthAsync_MetadataOnlyThrowingLocalDependencies_NeverResolvesThem()
    {
        var resolutions = 0;
        await using var fixture = await Fixture.CreateAsync(
            OutboxReadinessOptions.CreateMetadataOnlyProfile(3), configure: services =>
            {
                services.AddSingleton<OutboxOptions>(_ =>
                {
                    resolutions++;
                    throw new InvalidOperationException("Irrelevant local options must not be resolved.");
                });
                services.AddSingleton<OutboxRelayObservations<OutboxTestDbContext>>(_ =>
                {
                    resolutions++;
                    throw new InvalidOperationException("Explicit local observations must not be resolved.");
                });
            });
        var validator = fixture.Provider.GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>().Single();

        await validator.StartingAsync(Cancellation);
        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        resolutions.Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_MetadataOnlyIncidentalOptions_UsesOnlyWorkerMaxAttempts()
    {
        var options = new OutboxOptions { MaxAttempts = 1 };
        await using var fixture = await Fixture.CreateAsync(
            OutboxReadinessOptions.CreateMetadataOnlyProfile(2),
            configure: services => services.AddSingleton(options));
        var retryable = NewRow();
        retryable.RecordFailure("private", Start.AddHours(1).UtcDateTime);
        var parked = NewRow();
        parked.RecordFailure("private", Start.AddHours(1).UtcDateTime);
        parked.RecordFailure("private", Start.AddHours(1).UtcDateTime);
        await fixture.SeedAsync(retryable, parked);

        var first = await fixture.CheckAsync();
        options.MaxAttempts = 10;
        var second = await fixture.CheckAsync();

        first.Status.Should().Be(HealthStatus.Degraded);
        first.Data["pendingCount"].Should().Be(1);
        first.Data["parkedCount"].Should().Be(1);
        second.Data["maxAttempts"].Should().Be(2);
        second.Data["maxAttemptsSource"].Should().Be("profile");
        second.Data["parkedCount"].Should().Be(1);
        fixture.Probe.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task DrainAsync_MetadataOnlyWithEmbeddedRelay_UsesNoImplicitObservations()
    {
        await using var fixture = await Fixture.CreateAsync(
            OutboxReadinessOptions.CreateMetadataOnlyProfile(10), configure: services =>
            {
                services.AddDomainEventDispatch();
                services.AddTrellisOutbox<OutboxTestDbContext>();
            });

        fixture.Provider.GetService<OutboxRelayObservations<OutboxTestDbContext>>().Should().BeNull();
        (await fixture.Relay.DrainAsync(Cancellation)).Should().Be(0);
        (await fixture.CheckAsync()).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_LocalRelay_PreservesLocalDiagnostics()
    {
        await using var fixture = await Fixture.CreateAsync(maxAttempts: 3);

        var result = await fixture.CheckAsync();

        result.Data["relayMonitoring"].Should().Be("local-relay");
        result.Data["maxAttemptsSource"].Should().Be("outbox-options");
        result.Data["sampling"].Should().Be("separate-metadata-queries-and-local-relay");
        result.Data["profile"].Should().Be("staged-operational-reference");
    }

    [Theory]
    [InlineData(1, 60, "metadata-clock-invalid")]
    [InlineData(2, 60, "metadata-clock-invalid")]
    [InlineData(1, -30, "metadata-stale")]
    [InlineData(2, -30, "metadata-stale")]
    [InlineData(1, -29, null)]
    [InlineData(2, -29, null)]
    public async Task CheckHealthAsync_MetadataOnlySamples_UsesSharedClockAndFreshnessEvaluation(
        int queryNumber, int sampleOffsetSeconds, string? expectedReason)
    {
        var clock = new MetadataSampleTimeProvider(new FakeTimeProvider(Start));
        await using var fixture = await Fixture.CreateAsync(OutboxReadinessOptions.CreateMetadataOnlyProfile(3),
            configure: services => services.AddSingleton<TimeProvider>(clock));
        fixture.Probe.BeforeRead = (number, _) =>
        {
            if (number == queryNumber)
                clock.NextSampleUtcNow = Start.AddSeconds(sampleOffsetSeconds);
            return ValueTask.CompletedTask;
        };

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(expectedReason is null ? HealthStatus.Healthy : HealthStatus.Degraded);
        if (expectedReason is null)
            Reasons(result).Should().BeEmpty();
        else
            Reasons(result).Should().ContainSingle().Which.Should().Be(expectedReason);
        result.Data["pendingSampledAt"].Should().Be(queryNumber == 1 ? Start.AddSeconds(sampleOffsetSeconds) : Start);
        result.Data["parkedSampledAt"].Should().Be(queryNumber == 2 ? Start.AddSeconds(sampleOffsetSeconds) : Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckHealthAsync_MetadataOnlyProbeFault_NeverReturnsHealthy(bool priorEvidence)
    {
        await using var fixture = await Fixture.CreateAsync(OutboxReadinessOptions.CreateMetadataOnlyProfile(3));
        HealthCheckResult? previous = null;
        if (priorEvidence)
        {
            previous = await fixture.CheckAsync();
            fixture.Time.Advance(TimeSpan.FromSeconds(10));
        }

        fixture.Probe.BeforeRead = (_, _) => throw new InvalidOperationException("private provider detail");

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("probe-failed");
        result.Data["metadataAvailable"].Should().Be(false);
        result.Exception.Should().BeNull();
        result.Description.Should().NotContain("private provider detail");
        if (previous is { } cached)
            result.Data["pendingSampledAt"].Should().Be(cached.Data["pendingSampledAt"]);
        else
            result.Data.Should().NotContainKey("pendingSampledAt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckHealthAsync_MetadataOnlyProbeTimeout_NeverReturnsHealthyOrRecoversFromLateWork(
        bool priorEvidence)
    {
        await using var fixture = await Fixture.CreateAsync(OutboxReadinessOptions.CreateMetadataOnlyProfile(3));
        HealthCheckResult? previous = null;
        if (priorEvidence)
        {
            previous = await fixture.CheckAsync();
            fixture.Time.Advance(TimeSpan.FromSeconds(10));
            fixture.Probe.Reset();
        }

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe.BeforeRead = async (number, _) =>
        {
            if (number == 1)
            {
                entered.SetResult();
                await release.Task;
            }
        };
        var check = fixture.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        var result = await check.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("probe-timeout");
        result.Data["metadataAvailable"].Should().Be(false);
        if (previous is { } cached)
            result.Data["pendingSampledAt"].Should().Be(cached.Data["pendingSampledAt"]);
        else
            result.Data.Should().NotContainKey("pendingSampledAt");
        release.SetResult();
        await fixture.Probe.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        (await fixture.CheckAsync()).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartingAsync_ContextRegistration_DoesNotConstructContext(bool metadataOnly)
    {
        var constructed = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<OutboxTestDbContext>(_ =>
        {
            constructed++;
            throw new InvalidOperationException("A context must not be constructed during validation.");
        });
        if (!metadataOnly)
            services.AddTrellisOutbox<OutboxTestDbContext>();
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(metadataOnly));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var validator = provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>().Single();

        await validator.StartingAsync(Cancellation);

        constructed.Should().Be(0);
    }

    [Fact]
    public async Task StartingAsync_ProviderWithoutRegistrationCapability_SkipsCheckWithoutConstructingContext()
    {
        var constructed = 0;
        var services = new ServiceCollection();
        services.AddScoped<OutboxTestDbContext>(_ =>
        {
            constructed++;
            throw new InvalidOperationException("The fallback must not construct the context.");
        });
        await using var provider = services.BuildServiceProvider();
        var wrapped = new ProviderWithoutRegistrationCapability(provider);
        wrapped.GetService(typeof(IServiceProviderIsService)).Should().BeNull();
        var validator = new OutboxReadinessRegistrationValidator<OutboxTestDbContext>(
            wrapped, OutboxReadinessOptions.CreateMetadataOnlyProfile(3));

        var start = () => validator.StartingAsync(Cancellation);

        await start.Should().NotThrowAsync();
        constructed.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_MissingContextRegistration_FailsBeforeAnyServiceStarts(bool metadataOnly)
    {
        var builder = Host.CreateApplicationBuilder();
        var observer = new StartCountingService();
        builder.Services.AddSingleton<IHostedService>(observer);
        if (!metadataOnly)
            builder.Services.AddTrellisOutbox<OutboxTestDbContext>();
        builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(metadataOnly));
        using var host = builder.Build();

        var start = () => host.StartAsync(Cancellation);

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OutboxTestDbContext*");
        observer.Starts.Should().Be(0);
        if (!metadataOnly)
            host.Services.GetRequiredService<OutboxRelayObservations<OutboxTestDbContext>>()
                .Read().StartedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_MissingOrRemovedLocalRelay_FailsBeforeAnyServiceStarts(bool removedRelay)
    {
        var builder = Host.CreateApplicationBuilder();
        var observer = new StartCountingService();
        builder.Services.AddSingleton<IHostedService>(observer);
        builder.Services.AddScoped<OutboxTestDbContext>(_ => throw new InvalidOperationException("No context construction."));
        if (removedRelay)
        {
            builder.Services.AddTrellisOutbox<OutboxTestDbContext>();
            var descriptor = builder.Services.Single(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(OutboxRelay<OutboxTestDbContext>));
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(false));
        using var host = builder.Build();

        var start = () => host.StartAsync(Cancellation);

        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OutboxRelay*AddTrellisOutbox*");
        observer.Starts.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task StartAsync_RelayAndReadinessInEitherOrder_PassesHostValidation(
        bool readinessFirst, bool metadataOnly)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<OutboxTestDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        builder.Services.AddDomainEventDispatch();
        if (readinessFirst)
            builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(metadataOnly));
        builder.Services.AddTrellisOutbox<OutboxTestDbContext>(o => o.PollInterval = TimeSpan.FromHours(1));
        if (!readinessFirst)
            builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(metadataOnly));
        using var host = builder.Build();

        await host.StartAsync(Cancellation);
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task StartAsync_MetadataOnlyWithoutRelay_StartsWithoutConstructingContext()
    {
        var constructed = 0;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<OutboxTestDbContext>(_ =>
        {
            constructed++;
            throw new InvalidOperationException("Startup must not construct a context.");
        });
        builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(true));
        using var host = builder.Build();

        await host.StartAsync(Cancellation);
        await host.StopAsync(Cancellation);

        constructed.Should().Be(0);
    }

    [Theory]
    [InlineData(0, HealthStatus.Healthy)]
    [InlineData(2, HealthStatus.Degraded)]
    [InlineData(4, HealthStatus.Unhealthy)]
    [InlineData(5, HealthStatus.Unhealthy)]
    public async Task CheckHealthAsync_MetadataOnlyDepth_UsesInclusiveBoundsAndCap(int count, HealthStatus status)
    {
        var profile = OutboxReadinessOptions.CreateMetadataOnlyProfile(3) with
        {
            PendingDegradedThreshold = 2,
            PendingUnhealthyThreshold = 4,
        };
        await using var fixture = await Fixture.CreateAsync(profile);
        await fixture.SeedManyAsync(count);

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(status);
        result.Data["pendingCount"].Should().Be(Math.Min(count, 4));
        result.Data["pendingCountIsLowerBound"].Should().Be(count >= 4);
        fixture.Probe.Commands.Should().OnlyContain(c => c.TrackedEntries == 0
            && !c.Sql.Contains("Payload", StringComparison.Ordinal));
    }

    [Fact]
    public void AddTrellisOutboxReadiness_MetadataOnlyInvalidUnusedRelayTiming_StillRejectsProfile()
    {
        var profile = OutboxReadinessOptions.CreateMetadataOnlyProfile(3) with
        {
            RelayUnhealthyAfter = TimeSpan.FromMinutes(1),
        };

        var act = () => new ServiceCollection().AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(profile.RelayUnhealthyAfter));
    }

    [Fact]
    public async Task StartAsync_WrappedLocalRelay_RejectsHiddenInstanceBeforeServiceStart()
    {
        var builder = Host.CreateApplicationBuilder();
        var observer = new StartCountingService();
        builder.Services.AddSingleton<IHostedService>(observer);
        builder.Services.AddScoped<OutboxTestDbContext>(_ => throw new InvalidOperationException("No context construction."));
        builder.Services.AddTrellisOutbox<OutboxTestDbContext>();
        var relay = builder.Services.Single(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(OutboxRelay<OutboxTestDbContext>));
        builder.Services.Remove(relay);
        builder.Services.AddSingleton<IHostedService>(provider => new HiddenRelayService(
            ActivatorUtilities.CreateInstance<OutboxRelay<OutboxTestDbContext>>(provider)));
        builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(SelectProfile(false));
        using var host = builder.Build();

        var start = () => host.StartAsync(Cancellation);

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*wrapper*hides*");
        observer.Starts.Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_LocalProviderWithoutHostStart_ReportsRelayNotStarted()
    {
        await using var fixture = await Fixture.CreateAsync(startRelay: false);

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().Contain("relay-not-started");
    }

    [Fact]
    public async Task CheckHealthAsync_MetadataOnlyExplicitObservationStore_PreservesButDoesNotReadStore()
    {
        var clock = new MetadataSampleTimeProvider(new FakeTimeProvider(Start));
        var store = new OutboxRelayObservations<OutboxTestDbContext>(clock);
        await using var fixture = await Fixture.CreateAsync(SelectProfile(true),
            configure: services => services.AddSingleton(store));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        fixture.Provider.GetRequiredService<OutboxRelayObservations<OutboxTestDbContext>>().Should().BeSameAs(store);
        clock.UtcNowReads.Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_MetadataOnlyEvaluation_ReadsClockOncePerEvaluation()
    {
        var clock = new MetadataSampleTimeProvider(new FakeTimeProvider(Start));
        await using var fixture = await Fixture.CreateAsync(SelectProfile(true),
            configure: services => services.AddSingleton<TimeProvider>(clock));
        var before = clock.UtcNowReads;

        await fixture.CheckAsync();
        clock.UtcNowReads.Should().Be(before + 3);
        await fixture.CheckAsync();

        clock.UtcNowReads.Should().Be(before + 4);
    }

    private static OutboxReadinessOptions SelectProfile(bool metadataOnly) => metadataOnly
        ? OutboxReadinessOptions.CreateMetadataOnlyProfile(3)
        : OutboxReadinessOptions.CreateStagedOperationalProfile();

    private sealed class ProviderWithoutRegistrationCapability(IServiceProvider inner) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceProviderIsService) ? null : inner.GetService(serviceType);
    }

    private sealed class StartCountingService : IHostedService
    {
        public int Starts { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HiddenRelayService(IHostedService relay) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => relay.StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => relay.StopAsync(cancellationToken);
    }
}