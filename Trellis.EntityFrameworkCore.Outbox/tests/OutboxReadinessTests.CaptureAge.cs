namespace Trellis.EntityFrameworkCore.Outbox.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed partial class OutboxReadinessTests
{
    [Fact]
    public async Task CheckHealthAsync_CaptureAgeEnabledEmptyQueue_HasNoAgeBreach()
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile());

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["captureAge"].Should().Be("enabled");
        result.Data["captureAgeDegradedAfter"].Should().Be(TimeSpan.FromMinutes(2));
        result.Data["captureAgeUnhealthyAfter"].Should().Be(TimeSpan.FromMinutes(10));
        result.Data["unknownCaptureAgeCount"].Should().Be(0);
        result.Data.Should().NotContainKey("oldestPendingCapturedAt");
        result.Data.Should().NotContainKey("oldestPendingCaptureAge");
        fixture.Probe.Commands.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(119, HealthStatus.Healthy, null)]
    [InlineData(120, HealthStatus.Degraded, "capture-age-degraded")]
    [InlineData(599, HealthStatus.Degraded, "capture-age-degraded")]
    [InlineData(600, HealthStatus.Unhealthy, "capture-age-unhealthy")]
    public async Task CheckHealthAsync_KnownCaptureAge_UsesInclusiveThresholds(
        int ageSeconds, HealthStatus expectedStatus, string? expectedReason)
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile());
        var capturedAt = Start.AddSeconds(-ageSeconds);
        await fixture.SeedAsync(NewRow(capturedAt.UtcDateTime));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(expectedStatus);
        result.Data["oldestPendingCapturedAt"].Should().Be(capturedAt);
        result.Data["oldestPendingCaptureAge"].Should().Be(TimeSpan.FromSeconds(ageSeconds));
        result.Data["unknownCaptureAgeCount"].Should().Be(0);
        if (expectedReason is null)
            Reasons(result).Should().BeEmpty();
        else
            Reasons(result).Should().Contain(expectedReason);
    }

    [Fact]
    public async Task CheckHealthAsync_UnknownLegacyRows_DegradesAndReportsBoundedCoverage()
    {
        var profile = CaptureAgeProfile() with
        {
            PendingDegradedThreshold = 3,
            PendingUnhealthyThreshold = 4,
        };
        await using var fixture = await Fixture.CreateAsync(profile);
        await fixture.SeedManyAsync(5);

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain("capture-age-unknown");
        result.Data["unknownCaptureAgeCount"].Should().Be(4);
        result.Data["unknownCaptureAgeCountCap"].Should().Be(4);
        result.Data["unknownCaptureAgeCountIsLowerBound"].Should().Be(true);
        result.Data.Should().NotContainKey("oldestPendingCapturedAt");
    }

    [Fact]
    public async Task CheckHealthAsync_KnownUnhealthyAge_TakesPrecedenceOverUnknownCoverage()
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile());
        await fixture.SeedAsync(NewRow(Start.AddMinutes(-10).UtcDateTime), NewRow());

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        Reasons(result).Should().Contain(["capture-age-unhealthy", "capture-age-unknown"]);
        result.Data["unknownCaptureAgeCount"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_FutureCaptureTimestamp_DegradesAsInvalidClockEvidence()
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile());
        await fixture.SeedAsync(NewRow(Start.AddSeconds(1).UtcDateTime));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().ContainSingle().Which.Should().Be("capture-age-clock-invalid");
        result.Data["oldestPendingCaptureAge"].Should().Be(TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public async Task CheckHealthAsync_ProcessedAndParkedRows_DoNotContributeCaptureAge()
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile(), maxAttempts: 2);
        var processed = NewRow();
        processed.MarkProcessed(Start);
        var parked = NewRow();
        parked.RecordFailure("private", Start.UtcDateTime);
        parked.RecordFailure("private", Start.UtcDateTime);
        await fixture.SeedAsync(processed, parked);

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().ContainSingle().Which.Should().Be("parked-messages");
        result.Data["unknownCaptureAgeCount"].Should().Be(0);
        result.Data.Should().NotContainKey("oldestPendingCapturedAt");
    }

    [Fact]
    public async Task CheckHealthAsync_LeasedAndBackoffRows_RemainInCaptureAgeEvidence()
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile(), maxAttempts: 2);
        var leased = NewRow(Start.AddMinutes(-10).UtcDateTime);
        var backoff = NewRow(Start.AddMinutes(-2).UtcDateTime);
        backoff.RecordFailure("private", Start.AddHours(1).UtcDateTime);
        await fixture.SeedAsync(leased, backoff);
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OutboxTestDbContext>();
            await context.Set<OutboxMessage>().Where(m => m.Id == leased.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedBy, Guid.NewGuid())
                    .SetProperty(m => m.LockedUntil, Start.AddHours(1).UtcDateTime), Cancellation);
        }

        fixture.Probe.Reset();
        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["oldestPendingCapturedAt"].Should().Be(Start.AddMinutes(-10));
        result.Data["oldestPendingCaptureAge"].Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task CheckHealthAsync_MetadataOnlyCaptureAgeEnabled_UsesProviderCompatibleUtcQuery()
    {
        var profile = OutboxReadinessOptions.CreateMetadataOnlyProfile(3) with
        {
            CaptureAgeEnabled = true,
        };
        await using var fixture = await Fixture.CreateAsync(profile);
        await fixture.SeedAsync(NewRow(Start.AddMinutes(-2).UtcDateTime));

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().Contain("capture-age-degraded");
        result.Data["relayMonitoring"].Should().Be("disabled");
        fixture.Probe.Commands.Should().HaveCount(4);
        fixture.Probe.Commands.Should().OnlyContain(c =>
            c.Sql.StartsWith("SELECT", StringComparison.Ordinal)
            && !c.Sql.Contains("Payload", StringComparison.Ordinal)
            && !c.Sql.Contains("LastError", StringComparison.Ordinal)
            && c.TrackedEntries == 0);
    }

    [Fact]
    public async Task CheckHealthAsync_CachedCaptureEvidence_PreservesSamplesAndReevaluatesAge()
    {
        await using var fixture = await Fixture.CreateAsync(CaptureAgeProfile());
        await fixture.SeedAsync(NewRow(Start.AddSeconds(-119).UtcDateTime));
        var first = await fixture.CheckAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(1));

        var second = await fixture.CheckAsync();

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Degraded);
        Reasons(second).Should().Contain("capture-age-degraded");
        second.Data["captureAgeSampledAt"].Should().Be(first.Data["captureAgeSampledAt"]);
        fixture.Probe.Commands.Should().HaveCount(4);
    }

    [Fact]
    public async Task CheckHealthAsync_CaptureAgeSample_ValidatesFreshnessIndependently()
    {
        var clock = new MetadataSampleTimeProvider(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Start));
        await using var fixture = await Fixture.CreateAsync(
            CaptureAgeProfile(),
            configure: services => services.AddSingleton<TimeProvider>(clock));
        fixture.Probe.BeforeRead = (number, _) =>
        {
            if (number == 3)
                clock.NextSampleUtcNow = Start.AddSeconds(-30);
            return ValueTask.CompletedTask;
        };

        var result = await fixture.CheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        Reasons(result).Should().Contain("metadata-stale");
        result.Data["captureAgeSampledAt"].Should().Be(Start.AddSeconds(-30));
        result.Data["pendingSampledAt"].Should().Be(Start);
        result.Data["parkedSampledAt"].Should().Be(Start);
    }

    private static OutboxReadinessOptions CaptureAgeProfile() =>
        OutboxReadinessOptions.CreateStagedOperationalProfile() with { CaptureAgeEnabled = true };

    private static OutboxMessage NewRow(DateTime capturedAt) => OutboxMessage.Create(
        Guid.NewGuid(), DateTimeOffset.UnixEpoch, "Missing.Type, Missing.Assembly", "{\"private\":\"payload\"}",
        OutboxMessageKind.Domain, capturedAt: capturedAt);
}
