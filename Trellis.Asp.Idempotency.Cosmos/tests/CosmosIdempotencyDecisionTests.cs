namespace Trellis.Asp.Idempotency.Cosmos.Tests;

using System;
using Trellis.Asp.Idempotency;

/// <summary>
/// Exhaustive tests for the store's decision logic and key encoding. These need no emulator, so
/// the ordering rules stay covered on machines and CI agents where the Cosmos suite skips.
/// </summary>
public class CosmosIdempotencyDecisionTests
{
    private static readonly IdempotencyOptions Options = new()
    {
        Ttl = TimeSpan.FromHours(1),
        ReservationTimeout = TimeSpan.FromSeconds(30),
    };

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CosmosIdempotencyDocument Reserved(string fingerprint, DateTimeOffset reservedAt) =>
        new()
        {
            Fingerprint = fingerprint,
            ReservationId = "res-1",
            ReservedAtUnixMs = reservedAt.ToUnixTimeMilliseconds(),
        };

    private static CosmosIdempotencyDocument Completed(string fingerprint, DateTimeOffset completedAt) =>
        new()
        {
            Fingerprint = fingerprint,
            CompletedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
            Snapshot = new CosmosResponseSnapshotDocument { StatusCode = 201, Fingerprint = fingerprint },
        };

    [Fact]
    public void Completed_entry_within_ttl_and_matching_fingerprint_replays() =>
        CosmosIdempotencyDecision.Classify(Completed("fp", Now), "fp", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.Replay);

    [Fact]
    public void Completed_entry_within_ttl_and_different_fingerprint_is_a_mismatch() =>
        CosmosIdempotencyDecision.Classify(Completed("fp", Now), "other", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.BodyHashMismatch);

    [Fact]
    public void Completed_entry_past_ttl_is_absent() =>
        CosmosIdempotencyDecision.Classify(
                Completed("fp", Now - Options.Ttl), "fp", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.TreatAsAbsent);

    /// <summary>
    /// Expiry is checked before fingerprint for completed entries, so a key whose snapshot has
    /// aged out may legitimately be reused with a different body.
    /// </summary>
    [Fact]
    public void Completed_entry_past_ttl_is_absent_even_for_a_different_fingerprint() =>
        CosmosIdempotencyDecision.Classify(
                Completed("fp", Now - Options.Ttl), "other", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.TreatAsAbsent);

    [Fact]
    public void Live_reservation_with_matching_fingerprint_is_in_flight()
    {
        var decision = CosmosIdempotencyDecision.Classify(
            Reserved("fp", Now - TimeSpan.FromSeconds(5)), "fp", Now, Options);

        decision.Action.Should().Be(IdempotencyDocumentAction.AlreadyInFlight);
        decision.RetryAfter.Should().Be(TimeSpan.FromSeconds(25));
    }

    /// <summary>
    /// A reservation written by a host whose clock runs ahead lands in the future, making the
    /// elapsed time negative. Left unclamped, <c>ReservationTimeout - elapsed</c> would exceed the
    /// configured timeout and break the contract rule that <c>RetryAfter</c> falls within
    /// <c>(0, ReservationTimeout]</c> — telling the client to wait longer than the reservation can
    /// possibly live. The conformance suite cannot catch this, because it drives a single fake
    /// clock and so has no skew to produce.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(3600)]
    public void Reservation_from_a_clock_ahead_of_ours_clamps_retry_after(int skewSeconds)
    {
        var decision = CosmosIdempotencyDecision.Classify(
            Reserved("fp", Now + TimeSpan.FromSeconds(skewSeconds)), "fp", Now, Options);

        decision.Action.Should().Be(IdempotencyDocumentAction.AlreadyInFlight);
        decision.RetryAfter.Should().BePositive();
        decision.RetryAfter.Should().BeLessThanOrEqualTo(Options.ReservationTimeout);
    }

    [Fact]
    public void Live_reservation_with_different_fingerprint_is_a_mismatch() =>
        CosmosIdempotencyDecision.Classify(
                Reserved("fp", Now - TimeSpan.FromSeconds(5)), "other", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.BodyHashMismatch);

    [Fact]
    public void Timed_out_reservation_with_matching_fingerprint_is_taken_over() =>
        CosmosIdempotencyDecision.Classify(
                Reserved("fp", Now - Options.ReservationTimeout), "fp", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.TakeOver);

    /// <summary>
    /// Fingerprint is checked before the timeout for reserved entries, so a different body cannot
    /// claim a slot by waiting for the reservation to lapse.
    /// </summary>
    [Fact]
    public void Timed_out_reservation_with_different_fingerprint_is_still_a_mismatch() =>
        CosmosIdempotencyDecision.Classify(
                Reserved("fp", Now - Options.ReservationTimeout), "other", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.BodyHashMismatch);

    [Fact]
    public void Reservation_exactly_at_the_timeout_is_taken_over() =>
        CosmosIdempotencyDecision.Classify(
                Reserved("fp", Now - Options.ReservationTimeout), "fp", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.TakeOver);

    [Fact]
    public void Reservation_one_tick_before_the_timeout_is_still_in_flight() =>
        CosmosIdempotencyDecision.Classify(
                Reserved("fp", Now - Options.ReservationTimeout + TimeSpan.FromMilliseconds(1)),
                "fp", Now, Options)
            .Action.Should().Be(IdempotencyDocumentAction.AlreadyInFlight);
}

/// <summary>
/// Cosmos DB rejects item ids containing <c>/</c>, <c>\</c>, <c>?</c>, or <c>#</c>, but
/// idempotency keys are client-supplied and may contain any of them.
/// </summary>
public class CosmosIdempotencyKeyEncodingTests
{
    [Theory]
    [InlineData("simple-key")]
    [InlineData("with/slash")]
    [InlineData("with\\backslash")]
    [InlineData("with?question")]
    [InlineData("with#hash")]
    [InlineData("with spaces and trailing ")]
    [InlineData("unicode-\u00e9\u00e8-\u4e2d\u6587-\U0001f600")]
    [InlineData("")]
    public void Encoded_ids_round_trip_and_contain_no_reserved_characters(string key)
    {
        var id = CosmosIdempotencyStore.EncodeId(key);

        id.Should().NotContainAny("/", "\\", "?", "#");
        CosmosIdempotencyStore.DecodeId(id).Should().Be(key);
    }

    [Fact]
    public void Distinct_keys_encode_to_distinct_ids() =>
        CosmosIdempotencyStore.EncodeId("key-a").Should()
            .NotBe(CosmosIdempotencyStore.EncodeId("key-b"));

    /// <summary>
    /// Cosmos DB caps item ids at 1023 bytes. Base64 inflates by 4/3, so the longest key
    /// `IdempotencyOptions.MaxKeyLength` permits must still fit.
    /// </summary>
    [Fact]
    public void The_longest_permitted_key_fits_within_the_cosmos_id_limit()
    {
        var longestKey = new string('k', new IdempotencyOptions().MaxKeyLength);

        CosmosIdempotencyStore.EncodeId(longestKey).Length.Should().BeLessThan(1023);
    }
}