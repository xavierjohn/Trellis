namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Trellis.Mediator;

#pragma warning disable CA1707

public sealed partial class OutboxReadinessTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void CreateStagedOperationalProfile_Defaults_MatchesApprovedProfile()
    {
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();

        profile.PendingDegradedThreshold.Should().Be(1_000);
        profile.PendingUnhealthyThreshold.Should().Be(10_000);
        profile.CaptureAgeEnabled.Should().BeFalse();
        profile.CaptureAgeDegradedAfter.Should().Be(TimeSpan.FromMinutes(2));
        profile.CaptureAgeUnhealthyAfter.Should().Be(TimeSpan.FromMinutes(10));
        profile.RelayDegradedAfter.Should().Be(TimeSpan.FromMinutes(1));
        profile.RelayUnhealthyAfter.Should().Be(TimeSpan.FromMinutes(5));
        profile.StartupGracePeriod.Should().Be(TimeSpan.FromSeconds(30));
        profile.ProbeTimeout.Should().Be(TimeSpan.FromSeconds(2));
        profile.CacheDuration.Should().Be(TimeSpan.FromSeconds(10));
        profile.EvidenceMaxAge.Should().Be(TimeSpan.FromSeconds(30));
    }

    public static TheoryData<OutboxReadinessOptions> InvalidProfiles => new()
    {
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { PendingDegradedThreshold = 0 },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { PendingUnhealthyThreshold = 1_000 },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { CaptureAgeDegradedAfter = TimeSpan.Zero },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { CaptureAgeUnhealthyAfter = TimeSpan.FromMinutes(2) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { RelayDegradedAfter = TimeSpan.Zero },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { RelayUnhealthyAfter = TimeSpan.FromMinutes(1) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { StartupGracePeriod = TimeSpan.FromSeconds(-1) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { StartupGracePeriod = TimeSpan.FromMinutes(1) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { ProbeTimeout = TimeSpan.Zero },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { ProbeTimeout = TimeSpan.FromSeconds(11) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { CacheDuration = TimeSpan.Zero },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { CacheDuration = TimeSpan.FromSeconds(30) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { EvidenceMaxAge = TimeSpan.Zero },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { EvidenceMaxAge = TimeSpan.MinValue },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { EvidenceMaxAge = TimeSpan.FromSeconds(12) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { EvidenceMaxAge = TimeSpan.FromSeconds(12) - TimeSpan.FromTicks(1) },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with
        {
            CacheDuration = TimeSpan.FromSeconds(20),
            ProbeTimeout = TimeSpan.FromSeconds(10),
            EvidenceMaxAge = TimeSpan.FromSeconds(21),
        },
        OutboxReadinessOptions.CreateStagedOperationalProfile() with
        {
            CacheDuration = TimeSpan.MaxValue,
            EvidenceMaxAge = TimeSpan.MaxValue,
        },
    };

    [Fact]
    public void AddTrellisOutboxReadiness_FreshnessAboveCombinedBudget_AcceptsOneTickMargin()
    {
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
        {
            EvidenceMaxAge = TimeSpan.FromSeconds(12) + TimeSpan.FromTicks(1),
        };

        var act = () => new ServiceCollection().AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddTrellisOutboxReadiness_ExtremeFreshnessAndCache_RejectsWithoutOverflow()
    {
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
        {
            CacheDuration = TimeSpan.MaxValue - TimeSpan.FromSeconds(1),
            EvidenceMaxAge = TimeSpan.MaxValue,
        };

        var act = () => new ServiceCollection().AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("EvidenceMaxAge");
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public void AddTrellisOutboxReadiness_InvalidProfile_ThrowsWithoutRegisteringServices(OutboxReadinessOptions profile)
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);

        act.Should().Throw<ArgumentOutOfRangeException>();
        services.Should().BeEmpty();
    }

    [Fact]
    public void AddTrellisOutbox_WithoutReadiness_DoesNotEnableHealthChecks()
    {
        var services = new ServiceCollection();

        services.AddTrellisOutbox<OutboxTestDbContext>();

        services.Should().NotContain(d => d.ServiceType == typeof(HealthCheckService));
        services.Should().NotContain(d => d.ServiceType == typeof(OutboxRelayObservations<OutboxTestDbContext>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddTrellisOutboxReadiness_RepeatedRegistration_IsIdempotentAndOrderIndependent(bool readinessFirst)
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
        if (readinessFirst)
            services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        services.AddTrellisOutbox<OutboxTestDbContext>();
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile with { });

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        registrations.Should().ContainSingle(r => r.Name == "trellis-outbox");
        services.Should().ContainSingle(d => d.ServiceType == typeof(OutboxRelayObservations<OutboxTestDbContext>));
    }

    [Fact]
    public void AddTrellisOutboxReadiness_ConflictingProfile_ThrowsAndPreservesFirstRegistration()
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
        services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        var originalCount = services.Count;

        var act = () => services.AddTrellisOutboxReadiness<OutboxTestDbContext>(
            profile with { PendingDegradedThreshold = 100 });

        act.Should().Throw<InvalidOperationException>();
        services.Should().HaveCount(originalCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddTrellisOutboxReadiness_MismatchedRelayContext_ThrowsInEitherOrder(bool readinessFirst)
    {
        var services = new ServiceCollection();
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile();
        if (readinessFirst)
            services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        else
            services.AddTrellisOutbox<OtherOutboxContext>();
        var originalCount = services.Count;

        var act = () =>
        {
            if (readinessFirst)
                services.AddTrellisOutbox<OtherOutboxContext>();
            else
                services.AddTrellisOutboxReadiness<OutboxTestDbContext>(profile);
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*context*");
        services.Should().HaveCount(originalCount);
    }

    [Fact]
    public void AddTrellisOutboxReadiness_UnrelatedKeyedHostedService_DoesNotTreatItAsARelay()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IHostedService>("unrelated",
            (_, _) => throw new InvalidOperationException("A keyed service is not started by the host."));

        var act = () => services.AddTrellisOutboxReadiness<OutboxTestDbContext>(
            OutboxReadinessOptions.CreateStagedOperationalProfile());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, HealthStatus.Healthy, false)]
    [InlineData(999, HealthStatus.Healthy, false)]
    [InlineData(1_000, HealthStatus.Degraded, false)]
    [InlineData(9_999, HealthStatus.Degraded, false)]
    [InlineData(10_000, HealthStatus.Unhealthy, true)]
    [InlineData(10_001, HealthStatus.Unhealthy, true)]
    public async Task CheckHealthAsync_PendingDepth_UsesInclusiveThresholdsAndCappedCounts(
        int count, HealthStatus expectedStatus, bool lowerBound)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedManyAsync(count);

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(expectedStatus);
        result.Data["pendingCount"].Should().Be(Math.Min(count, 10_000));
        result.Data["pendingCountIsLowerBound"].Should().Be(lowerBound);
        result.Data["parkedCount"].Should().Be(0);
        result.Data["captureAge"].Should().Be("disabled");
        result.Data["consumerLag"].Should().Be("disabled");
        fixture.Probe.Commands.Should().HaveCount(2);
        fixture.Probe.Commands.Should().OnlyContain(c =>
            c.Sql.StartsWith("SELECT", StringComparison.Ordinal)
            && !c.Sql.Contains("Payload", StringComparison.Ordinal)
            && !c.Sql.Contains("LastError", StringComparison.Ordinal)
            && c.TrackedEntries == 0);
    }

    [Fact]
    public async Task CheckHealthAsync_LeasedBackoffAndParkedRows_UsesEffectiveAttemptCapWithoutMutation()
    {
        await using var fixture = await Fixture.CreateAsync(maxAttempts: 2);
        var pending = NewRow();
        var backoff = NewRow();
        backoff.RecordFailure("private failure", Start.AddHours(1).UtcDateTime);
        var parked = NewRow();
        parked.RecordFailure("private failure", Start.AddHours(1).UtcDateTime);
        parked.RecordFailure("private failure", Start.AddHours(1).UtcDateTime);
        var processed = NewRow();
        processed.MarkProcessed(Start);
        await fixture.SeedAsync(pending, backoff, parked, processed);
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Set<OutboxMessage>().Where(m => m.Id == pending.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedBy, Guid.NewGuid())
                    .SetProperty(m => m.LockedUntil, Start.AddHours(1).UtcDateTime), Cancellation);
        }

        fixture.Probe.Reset();

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["pendingCount"].Should().Be(2);
        result.Data["parkedCount"].Should().Be(1);
        result.Data["parkedCountIsLowerBound"].Should().Be(true);
        result.Data["maxAttempts"].Should().Be(2);
        result.Data.Values.OfType<string>().Should().NotContain(v => v.Contains("private failure", StringComparison.Ordinal));
        await using var verification = fixture.Provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<OutboxTestDbContext>()
            .Set<OutboxMessage>().AsNoTracking().OrderBy(m => m.Sequence).ToListAsync(Cancellation);
        rows[0].LockedBy.Should().NotBeNull();
        rows[0].Attempts.Should().Be(0);
        rows[1].Attempts.Should().Be(1);
        rows[1].LockedUntil.Should().Be(Start.AddHours(1).UtcDateTime);
        rows[2].Attempts.Should().Be(2);
        rows[3].ProcessedAt.Should().Be(Start);
    }

    [Theory]
    [InlineData(15, HealthStatus.Degraded, "relay-warming-up")]
    [InlineData(30, HealthStatus.Degraded, "relay-observation-missing")]
    [InlineData(45, HealthStatus.Degraded, "relay-observation-missing")]
    [InlineData(300, HealthStatus.Unhealthy, "relay-observation-overdue")]
    public async Task CheckHealthAsync_NoInitialObservation_UsesHostStartWithoutResettingGrace(
        int seconds, HealthStatus status, string reason)
    {
        await using var fixture = await Fixture.CreateAsync(observeRelay: false);
        fixture.Time.Advance(TimeSpan.FromSeconds(seconds));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(status);
        Reasons(result).Should().Contain(reason);
        result.Data["relayStartedAt"].Should().Be(Start);
        result.Data.Should().NotContainKey("lastPositiveRelayObservationAt");
    }

    [Fact]
    public async Task CheckHealthAsync_StartupGrace_DoesNotMaskAnUnhealthyDepth()
    {
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
        {
            PendingDegradedThreshold = 1,
            PendingUnhealthyThreshold = 2,
        };
        await using var fixture = await Fixture.CreateAsync(profile, observeRelay: false);
        await fixture.SeedManyAsync(2);

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("pending-depth-unhealthy").And.Contain("relay-warming-up");
    }

    [Theory]
    [InlineData(45, HealthStatus.Healthy)]
    [InlineData(60, HealthStatus.Degraded)]
    [InlineData(300, HealthStatus.Unhealthy)]
    public async Task CheckHealthAsync_FreshSampleWithOldPositiveObservation_EvaluatesActualGap(
        int seconds, HealthStatus status)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(seconds));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(status);
        result.Data["lastPositiveRelayObservationAt"].Should().Be(Start);
        result.Data["relaySampledAt"].Should().Be(Start.AddSeconds(seconds));
        result.Data["relayGap"].Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task CheckHealthAsync_CachedReads_PreserveSamplesAndReevaluateSeverity()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(59));
        var first = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        var second = await fixture.CheckAsync();

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Degraded);
        second.Data["pendingSampledAt"].Should().Be(first.Data["pendingSampledAt"]);
        second.Data["lastPositiveRelayObservationAt"].Should().Be(Start);
        fixture.Probe.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckHealthAsync_ChangedMaxAttempts_InvalidatesClassificationCache()
    {
        await using var fixture = await Fixture.CreateAsync(maxAttempts: 3);
        var row = NewRow();
        row.RecordFailure("private", Start.AddHours(1).UtcDateTime);
        row.RecordFailure("private", Start.AddHours(1).UtcDateTime);
        await fixture.SeedAsync(row);
        var first = await fixture.CheckAsync();
        fixture.Provider.GetRequiredService<OutboxOptions>().MaxAttempts = 2;

        var second = await fixture.CheckAsync();

        first.Data["pendingCount"].Should().Be(1);
        second.Status.Should().Be(HealthStatus.Degraded);
        second.Data["pendingCount"].Should().Be(0);
        second.Data["parkedCount"].Should().Be(1);
        second.Data["maxAttempts"].Should().Be(2);
        fixture.Probe.Commands.Should().HaveCount(4);
    }

    [Fact]
    public async Task CheckHealthAsync_ApplicationContextHasUnsavedRows_UsesIndependentReadScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var applicationScope = fixture.Provider.CreateAsyncScope();
        var applicationContext = applicationScope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
        applicationContext.Set<OutboxMessage>().Add(NewRow());

        var result = await fixture.CheckAsync();

        result.Data["pendingCount"].Should().Be(0);
        fixture.Probe.Commands.Should().OnlyContain(c => c.ContextId != applicationContext.ContextId.InstanceId);
        applicationContext.ChangeTracker.Entries<OutboxMessage>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckHealthAsync_ConcurrentCallersWithOneCancellation_KeepSharedRefreshAndOtherWaitersAlive(
        bool metadataOnly)
    {
        await using var fixture = await Fixture.CreateAsync(SelectProfile(metadataOnly));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe.BeforeRead = async (number, token) =>
        {
            if (number != 1)
                return;
            entered.SetResult();
            await release.Task.WaitAsync(token);
        };
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var cancelledWaiter = fixture.CheckAsync(caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        var otherWaiter = fixture.CheckAsync();

        await caller.CancelAsync();
        var cancellation = () => cancelledWaiter;
        await cancellation.Should().ThrowAsync<OperationCanceledException>();
        otherWaiter.IsCompleted.Should().BeFalse();
        release.SetResult();
        var result = await otherWaiter;
        var cached = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        cached.Status.Should().Be(HealthStatus.Healthy);
        fixture.Probe.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckHealthAsync_AlreadyCancelledCaller_DoesNotStartAProbe()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var check = () => fixture.CheckAsync(caller.Token);

        await check.Should().ThrowAsync<OperationCanceledException>();
        fixture.Probe.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckHealthAsync_ProbeTimeout_IsUnhealthyEvenWhenProviderIgnoresCancellation(bool ignoreCancellation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe.BeforeRead = async (number, token) =>
        {
            if (number != 1)
                return;
            entered.SetResult();
            await release.Task.WaitAsync(ignoreCancellation ? CancellationToken.None : token);
        };
        var check = fixture.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        var result = await check.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("probe-timeout");
        result.Exception.Should().BeNull();
        result.Data.Should().NotContainKey("pendingCount");
        if (ignoreCancellation)
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(10));
            (await fixture.CheckAsync()).Status.Should().Be(HealthStatus.Unhealthy);
            fixture.Probe.Commands.Should().ContainSingle();
        }

        release.TrySetResult();
        await fixture.Probe.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
    }

    [Fact]
    public async Task CheckHealthAsync_TwoQueries_ShareOneProbeBudgetAndKeepSeparateSampleTimes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Probe.BeforeRead = async (number, token) =>
        {
            if (number == 1)
                fixture.Time.Advance(TimeSpan.FromSeconds(1));
            else
            {
                secondEntered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
        };
        var check = fixture.CheckAsync();
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var result = await check.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("probe-timeout");
    }

    [Fact]
    public async Task CheckHealthAsync_SequentialMetadataQueries_PreserveTheirSeparateSampleTimes()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Probe.BeforeRead = (number, _) =>
        {
            fixture.Time.Advance(number == 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(500));
            return ValueTask.CompletedTask;
        };

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["pendingSampledAt"].Should().Be(Start.AddSeconds(1));
        result.Data["parkedSampledAt"].Should().Be(Start.AddMilliseconds(1_500));
    }

    [Theory]
    [InlineData(1, 60, "metadata-clock-invalid")]
    [InlineData(2, 60, "metadata-clock-invalid")]
    [InlineData(1, -30, "metadata-stale")]
    [InlineData(2, -30, "metadata-stale")]
    [InlineData(1, -29, null)]
    [InlineData(2, -29, null)]
    public async Task CheckHealthAsync_IndependentlySampledMetadata_ValidatesBothClockAndFreshness(
        int queryNumber, int sampleOffsetSeconds, string? expectedReason)
    {
        var clock = new MetadataSampleTimeProvider(new FakeTimeProvider(Start));
        await using var fixture = await Fixture.CreateAsync(
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
        result.Data["relaySampledAt"].Should().Be(Start);
        result.Data["metadataAvailable"].Should().Be(true);
    }

    [Fact]
    public async Task CheckHealthAsync_RefreshFault_PreservesOldEvidenceWithoutPresentingItAsFresh()
    {
        var profile = OutboxReadinessOptions.CreateStagedOperationalProfile() with
        {
            PendingDegradedThreshold = 1,
            PendingUnhealthyThreshold = 2,
        };
        await using var fixture = await Fixture.CreateAsync(profile);
        await fixture.SeedManyAsync(2);
        var first = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        fixture.Probe.BeforeRead = (_, _) => throw new InvalidOperationException("Password=secret; raw LastError");

        var failed = await fixture.CheckAsync();

        failed.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(failed).Should().Contain("probe-failed");
        failed.Data["pendingCount"].Should().Be(2);
        failed.Data["pendingSampledAt"].Should().Be(first.Data["pendingSampledAt"]);
        failed.Data["metadataAvailable"].Should().Be(false);
        failed.Exception.Should().BeNull();
        JsonSerializer.Serialize(failed.Data).Should().NotContain("secret").And.NotContain("LastError");
        failed.Description.Should().NotContain("secret");
    }

    [Fact]
    public async Task CheckHealthAsync_SeparateServiceProviders_DoNotShareRelayEvidenceOrCache()
    {
        await using var responsive = await Fixture.CreateAsync();
        await using var missing = await Fixture.CreateAsync(observeRelay: false);
        await responsive.SeedManyAsync(1);

        var first = await responsive.CheckAsync();
        var second = await missing.CheckAsync();

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Degraded);
        second.Data["pendingCount"].Should().Be(0);
        second.Data.Should().NotContainKey("lastPositiveRelayObservationAt");
    }

    [Fact]
    public async Task DrainAsync_EmptySuccessfulPoll_RecordsResponsivenessWithoutDelivery()
    {
        await using var fixture = await Fixture.CreateAsync(observeRelay: false);
        fixture.Time.Advance(TimeSpan.FromSeconds(15));

        (await fixture.Relay.DrainAsync(Cancellation)).Should().Be(0);
        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["lastPositiveRelayObservationAt"].Should().Be(Start.AddSeconds(15));
        result.Data["deliveredRows"].Should().Be(0L);
        result.Data.Should().NotContainKey("lastDurableDeliveryAt");
    }

    [Fact]
    public async Task DrainAsync_PersistedFailedAttempt_RecordsResponsivenessButNotDelivery()
    {
        await using var fixture = await Fixture.CreateAsync(observeRelay: false);
        await fixture.SeedAsync(NewRow());

        (await fixture.Relay.DrainAsync(Cancellation)).Should().Be(1);
        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["lastPositiveRelayObservationAt"].Should().Be(Start);
        result.Data["deliveredRows"].Should().Be(0L);
        result.Data.Should().NotContainKey("lastDurableDeliveryAt");
    }

    [Fact]
    public async Task DrainAsync_BookkeepingFailure_DoesNotRenewPositiveOrDeliveryEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(OutboxMessage.Create(Guid.NewGuid(), Start,
            typeof(ReadinessDomainEvent).AssemblyQualifiedName!,
            JsonSerializer.Serialize(new ReadinessDomainEvent(Start)), OutboxMessageKind.Domain));
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        fixture.Save.ThrowOnSave = true;

        var drain = () => fixture.Relay.DrainAsync(Cancellation);
        await drain.Should().ThrowAsync<InvalidOperationException>();
        var result = await fixture.CheckAsync();

        result.Data["lastPositiveRelayObservationAt"].Should().Be(Start);
        result.Data["deliveredRows"].Should().Be(0L);
        result.Data.Should().NotContainKey("lastDurableDeliveryAt");
    }

    [Theory]
    [InlineData(OutboxMessageKind.Domain)]
    [InlineData(OutboxMessageKind.Integration)]
    public async Task DrainAsync_SuccessfulPublication_RecordsDeliveryOnlyAfterBookkeeping(OutboxMessageKind kind)
    {
        await using var fixture = await Fixture.CreateAsync(configure: services => services.AddIntegrationEventDispatch());
        object message = kind == OutboxMessageKind.Domain
            ? new ReadinessDomainEvent(Start)
            : new ReadinessIntegrationEvent(Start);
        await fixture.SeedAsync(OutboxMessage.Create(Guid.NewGuid(), Start, message.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(message, message.GetType()), kind));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        (await fixture.Relay.DrainAsync(Cancellation)).Should().Be(1);
        var result = await fixture.CheckAsync();

        result.Data["deliveredRows"].Should().Be(1L);
        result.Data["lastDurableDeliveryAt"].Should().Be(Start.AddSeconds(1));
    }

    [Fact]
    public async Task DrainAsync_LostLease_DoesNotRecordUndurableDelivery()
    {
        await using var fixture = await Fixture.CreateAsync(configure: services =>
            services.AddDomainEventHandler<ReadinessDomainEvent, ReadinessLeaseStealingHandler>());
        await fixture.SeedAsync(OutboxMessage.Create(Guid.NewGuid(), Start,
            typeof(ReadinessDomainEvent).AssemblyQualifiedName!, JsonSerializer.Serialize(new ReadinessDomainEvent(Start)),
            OutboxMessageKind.Domain));

        (await fixture.Relay.DrainAsync(Cancellation)).Should().Be(0);
        var result = await fixture.CheckAsync();

        result.Data["deliveredRows"].Should().Be(0L);
        result.Data.Should().NotContainKey("lastDurableDeliveryAt");
    }

    private static OutboxMessage NewRow() => OutboxMessage.Create(
        Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Missing.Type, Missing.Assembly", "{\"private\":\"payload\"}",
        OutboxMessageKind.Domain);

    private static string[] Reasons(HealthCheckResult result) => result.Data["reasons"].Should().BeOfType<string[]>().Which;

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ServiceProvider provider, FakeTimeProvider time,
            ProbeCommandInterceptor probe, ProbeSaveInterceptor save)
        {
            _connection = connection;
            Provider = provider;
            Time = time;
            Probe = probe;
            Save = save;
        }

        public ServiceProvider Provider { get; }
        public FakeTimeProvider Time { get; }
        public ProbeCommandInterceptor Probe { get; }
        public ProbeSaveInterceptor Save { get; }
        public OutboxRelay<OutboxTestDbContext> Relay =>
            Provider.GetServices<IHostedService>().OfType<OutboxRelay<OutboxTestDbContext>>().Single();

        public static async Task<Fixture> CreateAsync(OutboxReadinessOptions? profile = null,
            bool observeRelay = true, int maxAttempts = 10, Action<IServiceCollection>? configure = null,
            bool startRelay = true)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(Cancellation);
            var time = new FakeTimeProvider(Start);
            var probe = new ProbeCommandInterceptor();
            var save = new ProbeSaveInterceptor();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(time);
            services.AddDbContext<OutboxTestDbContext>(o => o.UseSqlite(connection).AddInterceptors(probe, save));
            var selectedProfile = profile ?? OutboxReadinessOptions.CreateStagedOperationalProfile();
            if (selectedProfile.RelayMonitoring == OutboxRelayMonitoring.LocalRelay)
            {
                services.AddDomainEventDispatch();
                services.AddTrellisOutbox<OutboxTestDbContext>(o => o.MaxAttempts = maxAttempts);
            }

            services.AddTrellisOutboxReadiness<OutboxTestDbContext>(selectedProfile);
            configure?.Invoke(services);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>().Database.EnsureCreatedAsync(Cancellation);
            probe.Reset();
            if (selectedProfile.RelayMonitoring == OutboxRelayMonitoring.LocalRelay && startRelay)
            {
                var observations = provider.GetRequiredService<OutboxRelayObservations<OutboxTestDbContext>>();
                observations.Start();
                if (observeRelay)
                    observations.RecordPositiveObservation(0);
            }

            return new Fixture(connection, provider, time, probe, save);
        }

        public Task<HealthCheckResult> CheckAsync(CancellationToken? token = null)
        {
            var registration = Provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations.Single(r => r.Name == "trellis-outbox");
            return registration.Factory(Provider).CheckHealthAsync(new HealthCheckContext { Registration = registration },
                token ?? Cancellation);
        }

        public async Task SeedAsync(params OutboxMessage[] rows)
        {
            await using var scope = Provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            context.Set<OutboxMessage>().AddRange(rows);
            await context.SaveChangesAsync(Cancellation);
            Probe.Reset();
        }

        public async Task SeedManyAsync(int count)
        {
            if (count == 0)
                return;
            await using var scope = Provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Database.ExecuteSqlAsync($$"""
                WITH RECURSIVE rows(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM rows WHERE n < {{count}})
                INSERT INTO TrellisOutboxMessages (Id, Kind, OccurredAt, EventType, Payload, Attempts)
                SELECT printf('00000000-0000-0000-0000-%012d', n), 'Domain', '2000-01-01 00:00:00+00:00',
                       'Missing.Type, Missing.Assembly', '{"private":"payload"}', 0
                FROM rows
                """, Cancellation);
            Probe.Reset();
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ProbeCommandInterceptor : DbCommandInterceptor
    {
        private int _readCount;
        public ConcurrentQueue<(string Sql, Guid ContextId, int TrackedEntries)> Commands { get; } = new();
        public Func<int, CancellationToken, ValueTask>? BeforeRead { get; set; }
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Reset()
        {
            Commands.Clear();
            _readCount = 0;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue((command.CommandText, eventData.Context!.ContextId.InstanceId,
                eventData.Context.ChangeTracker.Entries().Count()));
            var number = Interlocked.Increment(ref _readCount);
            try
            {
                if (BeforeRead is not null)
                    await BeforeRead(number, cancellationToken);
            }
            finally
            {
                if (BeforeRead is not null)
                    Completed.TrySetResult();
            }

            return result;
        }
    }

    private sealed class ProbeSaveInterceptor : SaveChangesInterceptor
    {
        public bool ThrowOnSave { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ThrowOnSave ? throw new InvalidOperationException("bookkeeping failed") : new(result);
    }

    private sealed class MetadataSampleTimeProvider(TimeProvider clock) : TimeProvider
    {
        public DateTimeOffset? NextSampleUtcNow { get; set; }
        public int UtcNowReads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            UtcNowReads++;
            var sample = NextSampleUtcNow;
            NextSampleUtcNow = null;
            return sample ?? clock.GetUtcNow();
        }

        public override long TimestampFrequency => clock.TimestampFrequency;
        public override long GetTimestamp() => clock.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            clock.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class OtherOutboxContext : DbContext;
}

internal sealed record ReadinessDomainEvent(DateTimeOffset OccurredAt) : IDomainEvent;
internal sealed record ReadinessIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed class ReadinessLeaseStealingHandler(OutboxTestDbContext context) : IDomainEventHandler<ReadinessDomainEvent>
{
    public async ValueTask HandleAsync(ReadinessDomainEvent domainEvent, CancellationToken cancellationToken) =>
        await context.Set<OutboxMessage>().Where(m => m.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedBy, Guid.NewGuid()), cancellationToken);
}

#pragma warning restore CA1707