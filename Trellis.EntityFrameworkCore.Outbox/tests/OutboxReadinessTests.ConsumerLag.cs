namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

public sealed partial class OutboxReadinessTests
{
    [Fact]
    public void ConsumerLagEvidence_InvalidShape_Throws()
    {
        var blankConsumer = () => new ConsumerLagEvidence(
            "", "orders", "subscription", Start, ConsumerLagMeasures.Count, 1, 1,
            oldestUnaccountedAt: null, unaccountedCount: 0);
        var noMeasures = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start, ConsumerLagMeasures.None, 1, 1,
            oldestUnaccountedAt: null, unaccountedCount: null);
        var missingCount = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start, ConsumerLagMeasures.Count, 1, 1,
            oldestUnaccountedAt: null, unaccountedCount: null);
        var partialOverflow = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start, ConsumerLagMeasures.Age, 1, 2,
            oldestUnaccountedAt: null, unaccountedCount: null);
        var nonUtcSample = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start.ToOffset(TimeSpan.FromHours(1)),
            ConsumerLagMeasures.Age, 1, 1, oldestUnaccountedAt: null, unaccountedCount: null);
        var nonUtcOldest = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start, ConsumerLagMeasures.Age, 1, 1,
            oldestUnaccountedAt: Start.ToOffset(TimeSpan.FromHours(1)), unaccountedCount: null);
        var ageWithoutMeasure = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start, ConsumerLagMeasures.Count, 1, 1,
            oldestUnaccountedAt: Start, unaccountedCount: 0);
        var countWithoutMeasure = () => new ConsumerLagEvidence(
            "billing", "orders", "subscription", Start, ConsumerLagMeasures.Age, 1, 1,
            oldestUnaccountedAt: null, unaccountedCount: 0);

        blankConsumer.Should().Throw<ArgumentException>().WithParameterName("consumerId");
        noMeasures.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("measures");
        missingCount.Should().Throw<ArgumentException>().WithParameterName("unaccountedCount");
        partialOverflow.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("coveredPartitionCount");
        nonUtcSample.Should().Throw<ArgumentException>().WithParameterName("sampledAt");
        nonUtcOldest.Should().Throw<ArgumentException>().WithParameterName("oldestUnaccountedAt");
        ageWithoutMeasure.Should().Throw<ArgumentException>().WithParameterName("oldestUnaccountedAt");
        countWithoutMeasure.Should().Throw<ArgumentException>().WithParameterName("unaccountedCount");
    }

    [Fact]
    public async Task StartAsync_EnabledConsumerLagWithoutProvider_FailsBeforeAnyServiceStarts()
    {
        var builder = Host.CreateApplicationBuilder();
        var observer = new StartCountingService();
        builder.Services.AddSingleton<IHostedService>(observer);
        builder.Services.AddScoped<OutboxTestDbContext>(
            _ => throw new InvalidOperationException("The context must not be constructed."));
        builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(LagProfile());
        using var host = builder.Build();

        var start = () => host.StartAsync(Cancellation);

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IConsumerLagProvider*");
        observer.Starts.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_EnabledConsumerLag_DoesNotConstructProvider()
    {
        var constructed = false;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<OutboxTestDbContext>(
            _ => throw new InvalidOperationException("The context must not be constructed."));
        builder.Services.AddScoped<IConsumerLagProvider>(_ =>
        {
            constructed = true;
            throw new InvalidOperationException("The provider must not be constructed.");
        });
        builder.Services.AddTrellisOutboxReadiness<OutboxTestDbContext>(LagProfile());
        using var host = builder.Build();

        await host.StartAsync(Cancellation);

        constructed.Should().BeFalse();
        await host.StopAsync(Cancellation);
    }

    [Theory]
    [InlineData(ConsumerLagMeasures.Age)]
    [InlineData(ConsumerLagMeasures.Count)]
    [InlineData(ConsumerLagMeasures.Age | ConsumerLagMeasures.Count)]
    public async Task CheckHealthAsync_ConsumerLagQuietSourceWithCompleteCoverage_IsHealthy(
        ConsumerLagMeasures measures)
    {
        var lag = new StubConsumerLagProvider(Evidence(
            measures: measures,
            oldestUnaccountedAt: null,
            unaccountedCount: measures.HasFlag(ConsumerLagMeasures.Count) ? 0 : null));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(measures), configure: services => services.AddScoped<IConsumerLagProvider>(_ => lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["consumerLag"].Should().Be("enabled");
        result.Data["consumerLagAvailable"].Should().Be(true);
        result.Data["consumerLagMeasures"].Should().BeOfType<string[]>().Which.Should().Equal(MeasureNames(measures));
        result.Data["consumerLagEvidenceMeasures"].Should().BeOfType<string[]>().Which
            .Should().Equal(MeasureNames(measures));
        result.Data["consumerLagConsumerId"].Should().Be("billing");
        result.Data["consumerLagSource"].Should().Be("orders");
        result.Data["consumerLagScope"].Should().Be("subscription");
        result.Data["consumerLagRequiredPartitionCount"].Should().Be(2);
        result.Data["consumerLagCoveredPartitionCount"].Should().Be(2);
        result.Data.Should().NotContainKey("consumerLagOldestUnaccountedAt");
        Reasons(result).Should().BeEmpty();
        lag.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(119, HealthStatus.Healthy, null)]
    [InlineData(120, HealthStatus.Degraded, "consumer-lag-age-degraded")]
    [InlineData(599, HealthStatus.Degraded, "consumer-lag-age-degraded")]
    [InlineData(600, HealthStatus.Unhealthy, "consumer-lag-age-unhealthy")]
    public async Task CheckHealthAsync_ConsumerLagAge_UsesInclusiveThresholds(
        int ageSeconds, HealthStatus status, string? reason)
    {
        var lag = new StubConsumerLagProvider(Evidence(
            measures: ConsumerLagMeasures.Age,
            oldestUnaccountedAt: Start.AddSeconds(-ageSeconds),
            unaccountedCount: null));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(ConsumerLagMeasures.Age),
            configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(status);
        result.Data["consumerLagOldestUnaccountedAt"].Should().Be(Start.AddSeconds(-ageSeconds));
        result.Data["consumerLagOldestUnaccountedAge"].Should().Be(TimeSpan.FromSeconds(ageSeconds));
        if (reason is null)
            Reasons(result).Should().BeEmpty();
        else
            Reasons(result).Should().Contain(reason);
    }

    [Theory]
    [InlineData(999, HealthStatus.Healthy, null)]
    [InlineData(1_000, HealthStatus.Degraded, "consumer-lag-count-degraded")]
    [InlineData(9_999, HealthStatus.Degraded, "consumer-lag-count-degraded")]
    [InlineData(10_000, HealthStatus.Unhealthy, "consumer-lag-count-unhealthy")]
    public async Task CheckHealthAsync_ConsumerLagCount_UsesInclusiveThresholds(
        long count, HealthStatus status, string? reason)
    {
        var lag = new StubConsumerLagProvider(Evidence(
            measures: ConsumerLagMeasures.Count,
            oldestUnaccountedAt: null,
            unaccountedCount: count,
            countIsLowerBound: count == 10_000));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(ConsumerLagMeasures.Count),
            configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(status);
        result.Data["consumerLagUnaccountedCount"].Should().Be(count);
        result.Data["consumerLagUnaccountedCountIsLowerBound"].Should().Be(count == 10_000);
        if (reason is null)
            Reasons(result).Should().BeEmpty();
        else
            Reasons(result).Should().Contain(reason);
    }

    [Fact]
    public async Task CheckHealthAsync_ConsumerLagCountLowerBoundBelowThreshold_Degrades()
    {
        var lag = new StubConsumerLagProvider(Evidence(
            measures: ConsumerLagMeasures.Count,
            oldestUnaccountedAt: null,
            unaccountedCount: 400,
            countIsLowerBound: true));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(ConsumerLagMeasures.Count),
            configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().ContainSingle().Which.Should().Be("consumer-lag-count-lower-bound");
        result.Data["consumerLagUnaccountedCount"].Should().Be(400);
        result.Data["consumerLagUnaccountedCountIsLowerBound"].Should().Be(true);
    }

    [Fact]
    public async Task CheckHealthAsync_ConsumerLagMissingCoverageOrSelectedMeasure_Degrades()
    {
        var lag = new StubConsumerLagProvider(Evidence(
            measures: ConsumerLagMeasures.Age,
            oldestUnaccountedAt: null,
            unaccountedCount: null,
            coveredPartitions: 1));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(ConsumerLagMeasures.Age | ConsumerLagMeasures.Count),
            configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().Contain(["consumer-lag-coverage-missing", "consumer-lag-evidence-missing"]);
        result.Data["consumerLagAvailable"].Should().Be(true);
    }

    [Theory]
    [InlineData(-30, "consumer-lag-stale")]
    [InlineData(1, "consumer-lag-clock-invalid")]
    public async Task CheckHealthAsync_ConsumerLagSample_ValidatesOriginalFreshness(
        int sampleOffsetSeconds, string reason)
    {
        var lag = new StubConsumerLagProvider(Evidence(sampledAt: Start.AddSeconds(sampleOffsetSeconds)));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().Contain(reason);
        result.Data["consumerLagSampledAt"].Should().Be(Start.AddSeconds(sampleOffsetSeconds));
    }

    [Fact]
    public async Task CheckHealthAsync_CachedConsumerLagEvidence_PreservesSampleAndReevaluatesAge()
    {
        var lag = new StubConsumerLagProvider(Evidence(oldestUnaccountedAt: Start.AddSeconds(-119)));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));
        var first = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        var second = await fixture.CheckAsync();

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Degraded);
        Reasons(second).Should().Contain("consumer-lag-age-degraded");
        second.Data["consumerLagSampledAt"].Should().Be(first.Data["consumerLagSampledAt"]);
        lag.Calls.Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_ConsumerLagProviderFailure_IsUnhealthyAndKeepsMetadataAvailable()
    {
        var lag = new StubConsumerLagProvider(_ => throw new InvalidOperationException("private provider detail"));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));
        await fixture.SeedAsync(NewRow());

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("consumer-lag-provider-failed");
        result.Data["metadataAvailable"].Should().Be(true);
        result.Data["pendingCount"].Should().Be(1);
        result.Data["parkedCount"].Should().Be(0);
        result.Data["consumerLagAvailable"].Should().Be(false);
        result.Data["consumerLagFaultType"].Should().Be(nameof(InvalidOperationException));
        result.Description.Should().NotContain("private provider detail");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_ConsumerLagProviderReturnsNull_IsUnhealthyWithSafeCategory()
    {
        var lag = new StubConsumerLagProvider(
            _ => ValueTask.FromResult<ConsumerLagEvidence>(null!));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().ContainSingle().Which.Should().Be("consumer-lag-provider-failed");
        result.Data["metadataAvailable"].Should().Be(true);
        result.Data["consumerLagAvailable"].Should().Be(false);
        result.Data["consumerLagFaultType"].Should().Be("NullEvidence");
    }

    [Fact]
    public async Task CheckHealthAsync_DisabledConsumerLag_DoesNotResolveRegisteredProvider()
    {
        await using var fixture = await Fixture.CreateAsync(
            OutboxReadinessOptions.CreateMetadataOnlyProfile(3),
            configure: services => services.AddScoped<IConsumerLagProvider>(
                _ => throw new InvalidOperationException("The disabled provider must not be resolved.")));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["consumerLag"].Should().Be("disabled");
        result.Data.Should().NotContainKey("consumerLagAvailable");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task CheckHealthAsync_ConsumerLagProviderTimeout_PreservesFreshQueueEvidence(
        bool honorsCancellation, bool priorEvidence)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = 0;
        var lag = new StubConsumerLagProvider(async token =>
        {
            read++;
            if (priorEvidence && read == 1)
                return Evidence();

            entered.SetResult();
            if (honorsCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            else
                await release.Task;
            return Evidence();
        });
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));
        var parked = NewRow();
        parked.RecordFailure("private", Start.UtcDateTime);
        parked.RecordFailure("private", Start.UtcDateTime);
        parked.RecordFailure("private", Start.UtcDateTime);
        await fixture.SeedAsync(NewRow(), parked);
        HealthCheckResult? previous = null;
        if (priorEvidence)
        {
            previous = await fixture.CheckAsync();
            fixture.Time.Advance(TimeSpan.FromSeconds(10));
        }

        var check = fixture.CheckAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        var result = await check.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("probe-timeout");
        result.Data["metadataAvailable"].Should().Be(true);
        result.Data["pendingCount"].Should().Be(1);
        result.Data["parkedCount"].Should().Be(1);
        result.Data["consumerLagAvailable"].Should().Be(false);
        if (previous is { } prior)
            result.Data["pendingSampledAt"].Should().NotBe(prior.Data["pendingSampledAt"]);

        if (!honorsCancellation)
        {
            fixture.Time.Advance(TimeSpan.FromSeconds(10));

            var blocked = await fixture.CheckAsync();

            blocked.Status.Should().Be(HealthStatus.Unhealthy);
            Reasons(blocked).Should().Contain("probe-timeout");
            blocked.Data["metadataAvailable"].Should().Be(false);
            blocked.Data["pendingSampledAt"].Should().Be(result.Data["pendingSampledAt"]);
            blocked.Data["parkedSampledAt"].Should().Be(result.Data["parkedSampledAt"]);
            lag.Calls.Should().Be(priorEvidence ? 2 : 1);
            release.SetResult();
        }
    }

    [Fact]
    public async Task CheckHealthAsync_FutureConsumerLagSampleAndAge_ReportsClockReasonOnce()
    {
        var lag = new StubConsumerLagProvider(Evidence(
            sampledAt: Start.AddSeconds(1),
            oldestUnaccountedAt: Start.AddSeconds(1)));
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().ContainSingle().Which.Should().Be("consumer-lag-clock-invalid");
    }

    [Fact]
    public async Task CheckHealthAsync_ConsumerLagFailureRetainsEvidenceAndLaterRefreshRecovers()
    {
        var read = 0;
        var lag = new StubConsumerLagProvider(_ =>
        {
            read++;
            return read == 2
                ? throw new InvalidOperationException("private provider detail")
                : ValueTask.FromResult(Evidence(sampledAt: Start.AddSeconds(read == 1 ? 0 : 20)));
        });
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));
        var first = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(10));

        var failed = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        var recovered = await fixture.CheckAsync();

        first.Status.Should().Be(HealthStatus.Healthy);
        failed.Status.Should().Be(HealthStatus.Unhealthy);
        failed.Data["consumerLagAvailable"].Should().Be(false);
        failed.Data["consumerLagSampledAt"].Should().Be(Start);
        recovered.Status.Should().Be(HealthStatus.Healthy);
        recovered.Data["consumerLagAvailable"].Should().Be(true);
        recovered.Data["consumerLagSampledAt"].Should().Be(Start.AddSeconds(20));
        lag.Calls.Should().Be(3);
    }

    [Fact]
    public async Task CheckHealthAsync_ScopedConsumerLagProvider_IsDistinctAndDisposedPerRefresh()
    {
        var created = new List<Guid>();
        var disposed = new List<Guid>();
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(),
            configure: services => services.AddScoped<IConsumerLagProvider>(provider =>
                new TrackingScopedLagProvider(
                    Evidence(sampledAt: provider.GetRequiredService<TimeProvider>().GetUtcNow()),
                    created,
                    disposed)));

        var first = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(10));
        var second = await fixture.CheckAsync();

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Healthy);
        created.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        disposed.Should().Equal(created);
    }

    [Fact]
    public async Task CheckHealthAsync_CancelledLagWaiter_DoesNotCancelSharedProviderProbe()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lag = new StubConsumerLagProvider(async token =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return Evidence();
        });
        await using var fixture = await Fixture.CreateAsync(
            LagProfile(), configure: services => services.AddSingleton<IConsumerLagProvider>(lag));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var cancelled = fixture.CheckAsync(caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        var survivor = fixture.CheckAsync();

        await caller.CancelAsync();
        Func<Task> cancelledAction = () => cancelled;
        await cancelledAction.Should().ThrowAsync<OperationCanceledException>();
        survivor.IsCompleted.Should().BeFalse();
        release.SetResult();

        (await survivor).Status.Should().Be(HealthStatus.Healthy);
        lag.Calls.Should().Be(1);
    }

    private static OutboxReadinessOptions LagProfile(
        ConsumerLagMeasures measures = ConsumerLagMeasures.Age | ConsumerLagMeasures.Count) =>
        OutboxReadinessOptions.CreateMetadataOnlyProfile(3) with { ConsumerLagMeasures = measures };

    private static ConsumerLagEvidence Evidence(
        ConsumerLagMeasures measures = ConsumerLagMeasures.Age | ConsumerLagMeasures.Count,
        DateTimeOffset? sampledAt = null,
        DateTimeOffset? oldestUnaccountedAt = null,
        long? unaccountedCount = 0,
        bool countIsLowerBound = false,
        int coveredPartitions = 2) =>
        new(
            "billing",
            "orders",
            "subscription",
            sampledAt ?? Start,
            measures,
            requiredPartitionCount: 2,
            coveredPartitions,
            oldestUnaccountedAt,
            unaccountedCount,
            countIsLowerBound);

    private static string[] MeasureNames(ConsumerLagMeasures measures) =>
        measures == (ConsumerLagMeasures.Age | ConsumerLagMeasures.Count)
            ? ["age", "count"]
            : measures == ConsumerLagMeasures.Age ? ["age"] : ["count"];

    private sealed class StubConsumerLagProvider : IConsumerLagProvider
    {
        private readonly Func<CancellationToken, ValueTask<ConsumerLagEvidence>> _read;

        public StubConsumerLagProvider(ConsumerLagEvidence evidence)
            : this(_ => ValueTask.FromResult(evidence))
        {
        }

        public StubConsumerLagProvider(Func<CancellationToken, ValueTask<ConsumerLagEvidence>> read) => _read = read;

        public int Calls { get; private set; }

        public ValueTask<ConsumerLagEvidence> GetLagAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return _read(cancellationToken);
        }
    }

    private sealed class TrackingScopedLagProvider(
        ConsumerLagEvidence evidence,
        ICollection<Guid> created,
        ICollection<Guid> disposed) : IConsumerLagProvider, IAsyncDisposable
    {
        private readonly Guid _id = RecordCreation(created);

        public ValueTask<ConsumerLagEvidence> GetLagAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(evidence);

        public ValueTask DisposeAsync()
        {
            disposed.Add(_id);
            return ValueTask.CompletedTask;
        }

        private static Guid RecordCreation(ICollection<Guid> created)
        {
            var id = Guid.NewGuid();
            created.Add(id);
            return id;
        }
    }
}