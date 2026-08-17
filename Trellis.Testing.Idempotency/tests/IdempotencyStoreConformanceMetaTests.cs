namespace Trellis.Testing.Idempotency.Tests;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit.Sdk;

/// <summary>
/// Proves the conformance suite is worth inheriting.
/// </summary>
/// <remarks>
/// A suite of tests that passes for every store is worse than no suite at all, because it grants
/// false confidence. Each test here runs one rule against a store built to violate exactly that
/// rule and asserts the rule fails. <see cref="A_correct_store_passes_every_rule"/> closes the
/// loop from the other side, running the whole suite against a correct implementation that is not
/// the one the rules were extracted from.
/// </remarks>
public class IdempotencyStoreConformanceMetaTests
{
    private static async Task ShouldFail(
        StoreDefects defects, Func<IdempotencyStoreConformance, Task> rule)
    {
        var act = async () => await rule(new DefectiveStoreSuite(defects));

        await act.Should().ThrowAsync<XunitException>(
            "the conformance suite must reject a store with this defect");
    }

    [Fact]
    public async Task Suite_rejects_a_store_that_accepts_a_displaced_reservation_id_on_complete() =>
        await ShouldFail(
            StoreDefects.IgnoreReservationId,
            suite => suite.Complete_with_a_reservation_id_that_lost_the_slot_is_ignored());

    [Fact]
    public async Task Suite_rejects_a_store_that_accepts_a_displaced_reservation_id_on_abandon() =>
        await ShouldFail(
            StoreDefects.IgnoreReservationId,
            suite => suite.Abandon_with_a_reservation_id_that_lost_the_slot_is_ignored());

    [Fact]
    public async Task Suite_rejects_a_store_whose_abandon_deletes_a_persisted_snapshot() =>
        await ShouldFail(
            StoreDefects.DeleteSnapshotOnAbandon,
            suite => suite.Abandon_after_Complete_must_not_delete_the_persisted_snapshot());

    [Fact]
    public async Task Suite_rejects_a_store_that_ignores_scope() =>
        await ShouldFail(
            StoreDefects.IgnoreScope,
            suite => suite.Reserve_under_a_different_scope_does_not_collide());

    [Fact]
    public async Task Suite_rejects_a_store_whose_reserve_is_not_atomic() =>
        await ShouldFail(
            StoreDefects.NonAtomicReserve,
            suite => suite.Concurrent_reservations_of_one_key_grant_exactly_one_winner());

    [Fact]
    public async Task Suite_rejects_a_store_that_serves_a_snapshot_past_its_ttl() =>
        await ShouldFail(
            StoreDefects.IgnoreTtl,
            suite => suite.Reserve_after_the_ttl_elapses_treats_a_completed_entry_as_absent());

    [Fact]
    public async Task Suite_rejects_a_store_that_reuses_the_reservation_id_on_takeover() =>
        await ShouldFail(
            StoreDefects.ReuseReservationIdOnTakeover,
            suite => suite.Reserve_after_the_reservation_timeout_takes_over_with_a_new_reservation_id());

    /// <summary>
    /// The mirror of the rejection tests: the suite must not reject a store for behaviour the
    /// contract explicitly permits. `IdempotencyResponseSnapshot` documents header names as
    /// case-insensitive, so a store that normalizes their casing is conforming.
    /// </summary>
    [Fact]
    public async Task Suite_accepts_a_store_that_normalizes_header_name_casing()
    {
        var suite = new DefectiveStoreSuite(StoreDefects.NormalizeHeaderCasing);

        await suite.Reserve_after_Complete_replays_the_snapshot_for_a_matching_fingerprint();
    }

    /// <summary>
    /// Runs every rule in the suite against a correct store, mirroring how xUnit runs them: a
    /// fresh suite instance, and therefore a fresh store, per rule.
    /// </summary>
    /// <remarks>
    /// Discovered reflectively rather than listed, so a rule added to the suite is covered here
    /// automatically instead of silently escaping this check.
    /// </remarks>
    [Fact]
    public async Task A_correct_store_passes_every_rule()
    {
        var rules = Rules();
        rules.Should().HaveCountGreaterThanOrEqualTo(17,
            "the suite is not expected to shrink; update this floor deliberately if a rule is removed");

        foreach (var rule in rules)
        {
            var suite = new DefectiveStoreSuite(StoreDefects.None);
            await (Task)rule.Invoke(suite, [])!;
        }
    }

    /// <summary>
    /// Guards the shape the meta-tests depend on: rules must be public so a store author can
    /// invoke one in isolation while diagnosing a failure.
    /// </summary>
    [Fact]
    public void Every_rule_is_public_and_returns_a_Task() =>
        Rules().Should().OnlyContain(m => m.IsPublic && m.ReturnType == typeof(Task));

    private static MethodInfo[] Rules() =>
        [.. typeof(IdempotencyStoreConformance)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)];
}
