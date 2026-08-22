namespace Trellis.Core.Tests.Errors;

/// <summary>
/// A case that can only ever report <c>error.unspecified</c> publishes a member no producer can
/// use and no consumer can branch on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Error.NotFound"/>, <see cref="Error.Gone"/>, and <see cref="Error.RateLimited"/>
/// hard-coded their code to a compile-time constant, so the distinction the sentinel exists to
/// draw — "the producer named no reason" versus "the producer named this one" — could not be
/// expressed on those cases at all. Missing because the row was never looked up and missing
/// because the caller is not entitled to it are different answers, and a 404 that cannot say
/// which is a 404 a client cannot act on.
/// </para>
/// <para>
/// Every case now names a reason the same way, through the inherited <see cref="Error.Code"/>.
/// Silence remains the default: a producer that names nothing still reports the sentinel, so the
/// wire contract for existing callers is unchanged.
/// </para>
/// </remarks>
public class NameableReasonCodeTests
{
    private static readonly ResourceRef OrderRef = new("Order", "42");

    [Fact]
    public void NotFound_without_a_reason_stays_silent() =>
        new Error.NotFound(OrderRef).Code.Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void NotFound_publishes_the_reason_a_producer_named() =>
        new Error.NotFound(OrderRef) { Code = "order.not-found" }.Code
            .Should().Be("order.not-found");

    [Fact]
    public void NotFound_with_a_reason_keeps_its_resource_and_detail()
    {
        var error = new Error.NotFound(OrderRef)
        {
            Code = "order.not-found",
            Detail = "No order with that id.",
        };

        error.Resource.Should().Be(new ResourceRef("Order", "42"));
        error.Detail.Should().Be("No order with that id.");
        error.Kind.Should().Be("not-found", "a reason names why, not what class of failure it was");
    }

    [Fact]
    public void Gone_publishes_the_reason_a_producer_named() =>
        new Error.Gone(OrderRef) { Code = "order.purged" }.Code.Should().Be("order.purged");

    [Fact]
    public void Gone_without_a_reason_stays_silent() =>
        new Error.Gone(OrderRef).Code.Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void RateLimited_publishes_the_quota_that_was_exceeded() =>
        new Error.RateLimited { Code = "quota.daily-transfers" }.Code
            .Should().Be("quota.daily-transfers");

    [Fact]
    public void RateLimited_without_a_reason_stays_silent() =>
        new Error.RateLimited().Code.Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void RateLimited_with_a_reason_keeps_its_retry_advice()
    {
        var retry = new RetryAdvice(TimeSpan.FromSeconds(30));

        new Error.RateLimited(retry) { Code = "quota.daily-transfers" }.Retry.Should().Be(retry);
    }

    [Fact]
    public void Unavailable_publishes_the_reason_a_producer_named() =>
        new Error.Unavailable { Code = "db.unreachable" }.Code.Should().Be("db.unreachable");

    /// <remarks>
    /// A case whose reason is optional and unnamed reports the sentinel, never its kind — the
    /// distinction this whole surface exists to preserve.
    /// </remarks>
    [Fact]
    public void Code_is_the_sentinel_when_no_reason_was_named()
    {
        var silent = new Error.NotFound(OrderRef);

        silent.Code.Should().Be(ValidationCodes.Unspecified);
        silent.Code.Should().NotBe(silent.Kind);
    }
}
