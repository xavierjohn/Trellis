namespace Trellis.EntityFrameworkCore.Outbox.Tests;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class OutboxRetryBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Cap = TimeSpan.FromHours(1);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    public void Compute_without_jitter_doubles_the_delay_each_attempt(int attempt, int expectedSeconds)
    {
        var delay = OutboxRetryBackoff.Compute(Guid.NewGuid(), attempt, Base, Cap, jitter: 0);

        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Compute_without_jitter_saturates_at_the_cap_without_overflowing(int attempt)
    {
        var delay = OutboxRetryBackoff.Compute(Guid.NewGuid(), attempt, Base, Cap, jitter: 0);

        delay.Should().Be(Cap);
    }

    [Fact]
    public void Compute_is_deterministic_for_the_same_id_and_attempt()
    {
        var id = Guid.NewGuid();

        var first = OutboxRetryBackoff.Compute(id, 3, Base, Cap, jitter: 0.5);
        var second = OutboxRetryBackoff.Compute(id, 3, Base, Cap, jitter: 0.5);

        first.Should().Be(second);
    }

    [Fact]
    public void Compute_with_zero_jitter_ignores_the_id()
    {
        var a = OutboxRetryBackoff.Compute(Guid.NewGuid(), 3, Base, Cap, jitter: 0);
        var b = OutboxRetryBackoff.Compute(Guid.NewGuid(), 3, Base, Cap, jitter: 0);

        a.Should().Be(TimeSpan.FromMinutes(2));
        b.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Compute_with_jitter_only_subtracts_and_never_exceeds_the_computed_delay()
    {
        var computed = TimeSpan.FromMinutes(2); // attempt 3, 30s base

        for (var i = 0; i < 500; i++)
        {
            var delay = OutboxRetryBackoff.Compute(Guid.NewGuid(), 3, Base, Cap, jitter: 0.5);

            delay.Should().BeGreaterThanOrEqualTo(computed * 0.5, "equal jitter subtracts at most half");
            delay.Should().BeLessThanOrEqualTo(computed, "jitter never adds to the delay");
        }
    }

    [Fact]
    public void Compute_with_full_jitter_stays_within_the_cap_and_above_zero()
    {
        for (var i = 0; i < 500; i++)
        {
            var delay = OutboxRetryBackoff.Compute(Guid.NewGuid(), 50, Base, Cap, jitter: 1);

            delay.Should().BeLessThanOrEqualTo(Cap, "subtractive jitter keeps the delay at or below the cap");
            delay.Should().BeGreaterThan(TimeSpan.Zero, "a retry is never scheduled for 'now'");
        }
    }

    [Fact]
    public void Compute_de_correlates_different_ids_so_a_recovered_dependency_is_not_flooded()
    {
        var distinct = Enumerable.Range(0, 200)
            .Select(_ => OutboxRetryBackoff.Compute(Guid.NewGuid(), 10, Base, Cap, jitter: 0.5))
            .Distinct()
            .Count();

        distinct.Should().BeGreaterThan(100, "id-keyed jitter must spread retries across the window");
    }
}
