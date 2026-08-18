namespace Trellis.Asp.Tests.Idempotency;

using System;
using System.Linq;
using System.Net.Http;
using Trellis.Asp.Idempotency;

/// <summary>
/// Pins the public type surface of the idempotency feature: the [Idempotent] attribute targets,
/// IdempotencyOptions defaults, IdempotencyResponseSnapshot record-equality, and the four
/// IdempotencyReservationOutcome variants. These guarantees are part of the public contract;
/// changing them is a breaking change.
/// </summary>
public sealed class IdempotencyTypesTests
{
    [Fact]
    public void IdempotentAttribute_targets_class_and_method_and_is_not_inherited()
    {
        var usage = typeof(IdempotentAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage.Inherited.Should().BeFalse(
            "endpoint metadata is read from the closest declaration; inherited application " +
            "would silently opt sub-classes in without an explicit marker");
    }

    [Fact]
    public void IdempotencyOptions_defaults_match_published_contract()
    {
        var options = new IdempotencyOptions();

        options.HeaderName.Should().Be("Idempotency-Key");
        options.ReplayHeaderName.Should().Be("Idempotent-Replayed");
        options.Ttl.Should().Be(TimeSpan.FromHours(24));
        options.ReservationTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.MaxKeyLength.Should().Be(200);
        options.MaxRequestBodyBytes.Should().Be(1L * 1024 * 1024);
        options.MaxResponseBodyBytes.Should().Be(1L * 1024 * 1024);
        options.MismatchStatusCode.Should().Be(422);
        options.RequireKeyOnOptedInEndpoints.Should().BeTrue();
        options.IncludeSetCookieInSnapshot.Should().BeFalse();
        options.Methods.Should().BeEquivalentTo(new[] { HttpMethod.Post.Method, HttpMethod.Patch.Method });
        options.AdditionalFingerprintHeaders.Should().BeEmpty();
    }

    [Fact]
    public void IdempotencyOptions_methods_and_extra_headers_are_mutable()
    {
        var options = new IdempotencyOptions();
        options.Methods.Add("PUT");
        options.AdditionalFingerprintHeaders.Add("Accept");

        options.Methods.Should().Contain("PUT");
        options.AdditionalFingerprintHeaders.Should().Contain("Accept");
    }

    [Fact]
    public void IdempotencyResponseSnapshot_record_equality_is_reference_based_for_collection_fields()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/json"]
        };
        var body = new byte[] { 1, 2, 3 };
        var a = new IdempotencyResponseSnapshot(StatusCode: 201, Headers: headers, Body: body, Fingerprint: "abc");
        var b = new IdempotencyResponseSnapshot(StatusCode: 201, Headers: headers, Body: body, Fingerprint: "abc");

        // Sharing the same Headers + Body references with equal scalar fields yields equal records
        // (default record equality on a reference-type field is EqualityComparer<T>.Default.Equals,
        //  which falls back to ReferenceEquals for IReadOnlyDictionary<,> and byte[]).
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());

        // Two snapshots with structurally-equal but DISTINCT header dictionaries / body arrays
        // are NOT considered equal — consumers must not assume deep structural equality.
        var distinctHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/json"]
        };
        var distinctBody = new byte[] { 1, 2, 3 };
        var c = new IdempotencyResponseSnapshot(StatusCode: 201, Headers: distinctHeaders, Body: distinctBody, Fingerprint: "abc");

        a.Should().NotBe(c);
    }

    [Fact]
    public void Reserved_outcome_carries_an_opaque_reservation_id()
    {
        var outcome = new IdempotencyReservationOutcome.Reserved(ReservationId: "tok-1");

        outcome.ReservationId.Should().Be("tok-1");
        outcome.Should().BeOfType<IdempotencyReservationOutcome.Reserved>();
    }

    [Fact]
    public void AlreadyInFlight_outcome_carries_retry_after_seconds()
    {
        var outcome = new IdempotencyReservationOutcome.AlreadyInFlight(RetryAfter: TimeSpan.FromSeconds(15));

        outcome.RetryAfter.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Replay_outcome_carries_a_snapshot()
    {
        var snap = new IdempotencyResponseSnapshot(
            StatusCode: 200,
            Headers: new Dictionary<string, string[]>(),
            Body: Array.Empty<byte>(),
            Fingerprint: "fp");

        var outcome = new IdempotencyReservationOutcome.Replay(snap);

        outcome.Snapshot.Should().BeSameAs(snap);
    }

    [Fact]
    public void BodyHashMismatch_outcome_carries_the_stored_fingerprint()
    {
        var outcome = new IdempotencyReservationOutcome.BodyHashMismatch(StoredFingerprint: "stored");

        outcome.StoredFingerprint.Should().Be("stored");
    }

    [Fact]
    public void Outcome_is_a_closed_hierarchy_of_four_cases()
    {
        // Use sealed concrete types as a structural pin. The base must be abstract.
        typeof(IdempotencyReservationOutcome).IsAbstract.Should().BeTrue();
        typeof(IdempotencyReservationOutcome.Reserved).IsSealed.Should().BeTrue();
        typeof(IdempotencyReservationOutcome.AlreadyInFlight).IsSealed.Should().BeTrue();
        typeof(IdempotencyReservationOutcome.Replay).IsSealed.Should().BeTrue();
        typeof(IdempotencyReservationOutcome.BodyHashMismatch).IsSealed.Should().BeTrue();
    }
}