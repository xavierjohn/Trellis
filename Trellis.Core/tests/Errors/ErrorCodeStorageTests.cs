namespace Trellis.Core.Tests.Errors;

/// <summary>
/// <see cref="Error.Code"/> is storage on the base, not a per-case computed property.
/// </summary>
/// <remarks>
/// <para>
/// Every case reads and writes the same member, so a producer names a reason the same way no matter
/// which failure it is raising: <c>new Error.NotFound(resource) { Code = "account.closed" }</c>.
/// The alternative — a virtual <c>Code</c> that each case overrode from a private <c>ReasonCode</c>
/// payload — spelled one concept two ways and left three cases unable to carry a reason at all.
/// </para>
/// <para>
/// Storage on the base has one consequence worth pinning down: <see cref="Error.Equals(Error?)"/> is
/// hand-written and compares only the members it names. A code that lives on the base and is absent
/// from that override would make two errors with different reasons compare equal, which would in turn
/// make them interchangeable in a cache key or a deduplicated log.
/// </para>
/// </remarks>
public class ErrorCodeStorageTests
{
    private static readonly ResourceRef Order = new("Order", "42");

    [Fact]
    public void Code_is_named_through_an_object_initializer_on_any_case() =>
        new Error.NotFound(Order) { Code = "order.archived" }.Code
            .Should().Be("order.archived");

    [Fact]
    public void Code_defaults_to_the_sentinel_when_the_producer_names_nothing() =>
        new Error.NotFound(Order).Code
            .Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void Errors_differing_only_by_code_are_not_equal() =>
        (new Error.NotFound(Order) { Code = "order.archived" } as Error)
            .Should().NotBe(new Error.NotFound(Order) { Code = "order.purged" });

    [Fact]
    public void Errors_differing_only_by_code_do_not_share_a_hash_code() =>
        new Error.NotFound(Order) { Code = "order.archived" }.GetHashCode()
            .Should().NotBe(new Error.NotFound(Order) { Code = "order.purged" }.GetHashCode());

    [Fact]
    public void Errors_agreeing_on_code_remain_equal() =>
        (new Error.NotFound(Order) { Code = "order.archived" } as Error)
            .Should().Be(new Error.NotFound(Order) { Code = "order.archived" });

    /// <summary>
    /// A copy that changes an unrelated member keeps the reason its producer named; losing it here
    /// would silently downgrade a named failure to <c>error.unspecified</c> on the wire.
    /// </summary>
    [Fact]
    public void Code_survives_a_copy_that_changes_another_member() =>
        (new Error.NotFound(Order) { Code = "order.archived" } with { Detail = "gone since March" }).Code
            .Should().Be("order.archived");

    [Fact]
    public void Code_can_be_replaced_by_a_copy() =>
        (new Error.Conflict(Order, "order.already-shipped") with { Code = "order.locked" }).Code
            .Should().Be("order.locked");

    /// <summary>
    /// A mandatory reason stays mandatory: the case cannot be constructed without one, so the
    /// compiler still refuses a <see cref="Error.Conflict"/> that says nothing about the conflict.
    /// </summary>
    [Fact]
    public void A_mandatory_reason_is_the_code() =>
        new Error.Conflict(Order, "order.already-shipped").Code
            .Should().Be("order.already-shipped");

    /// <summary>
    /// <see cref="Error.Forbidden.PolicyId"/> is a reading alias over the same storage rather than a
    /// second field, so the policy that denied the request and the code a client sees cannot drift.
    /// </summary>
    [Fact]
    public void Forbidden_reads_its_policy_id_from_the_code() =>
        new Error.Forbidden("orders.write", Order).PolicyId
            .Should().Be("orders.write");

    [Fact]
    public void Forbidden_publishes_its_policy_id_as_the_code() =>
        new Error.Forbidden("orders.write", Order).Code
            .Should().Be("orders.write");

    [Fact]
    public void Forbidden_cannot_drift_between_its_policy_id_and_its_code()
    {
        var moved = new Error.Forbidden("orders.write", Order) with { Code = "orders.admin" };

        moved.PolicyId.Should().Be("orders.admin");
        moved.Code.Should().Be("orders.admin");
    }
}
