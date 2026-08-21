namespace Trellis.Core.Tests.Errors;

using System.Linq;
using System.Reflection;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="ErrorRetryExtensions"/> and <see cref="RetryClassification"/>.
/// Covers the default classification of every nested case of <see cref="Error"/>,
/// aggregate max-severity semantics, retry-advice extraction, the predicate convenience
/// helpers, null-argument handling, and a reflection-based exhaustiveness guard that
/// fails when a new nested case of <see cref="Error"/> ships without an explicit
/// classification.
/// </summary>
public class ErrorRetryExtensionsTests
{
    private static readonly ResourceRef SampleResource = ResourceRef.For<Sample>("id-1");

    private sealed record Sample(string Id);

    private sealed record SampleTransportFault(string Name) : ITransportFault;

    // ─────────────────────────────────────────────────────────────────────────
    // Direct classification per Error case
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<Error, RetryClassification> ClassificationCases() => new()
    {
        { new Error.Unavailable(), RetryClassification.Transient },
        { new Error.RateLimited(), RetryClassification.Transient },
        { new Error.Unexpected("unhandled-exception"), RetryClassification.Transient },
        { new Error.TransportFault(new SampleTransportFault("http-timeout")), RetryClassification.Permanent },
        { new Error.AuthenticationRequired(), RetryClassification.FailFast },
        { new Error.Forbidden("policy.deny"), RetryClassification.Permanent },
        { Error.InvalidInput.ForField("name", "required"), RetryClassification.Permanent },
        { new Error.InvariantViolation("order_must_have_items"), RetryClassification.Permanent },
        { new Error.NotFound(SampleResource), RetryClassification.Permanent },
        { new Error.Gone(SampleResource), RetryClassification.Permanent },
        { new Error.Conflict(SampleResource, "duplicate_key"), RetryClassification.Permanent },
    };

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void Classify_returns_expected_classification_for_each_error_case(
        Error error, RetryClassification expected) =>
        error.Classify().Should().Be(expected);

    // ─────────────────────────────────────────────────────────────────────────
    // Aggregate classification: max-severity semantics
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_aggregate_with_only_transient_inners_returns_transient()
    {
        var agg = new Error.Aggregate(
            new Error.Unavailable(),
            new Error.RateLimited(),
            new Error.Unexpected("io"));

        agg.Classify().Should().Be(RetryClassification.Transient);
    }

    [Fact]
    public void Classify_aggregate_with_mixed_transient_and_permanent_returns_permanent()
    {
        var agg = new Error.Aggregate(
            new Error.Unavailable(),
            new Error.NotFound(SampleResource));

        agg.Classify().Should().Be(RetryClassification.Permanent);
    }

    [Fact]
    public void Classify_aggregate_with_any_failfast_inner_returns_failfast()
    {
        var agg = new Error.Aggregate(
            new Error.Unavailable(),
            new Error.AuthenticationRequired("Bearer"));

        agg.Classify().Should().Be(RetryClassification.FailFast);
    }

    [Fact]
    public void Classify_aggregate_with_failfast_and_permanent_inners_returns_failfast()
    {
        var agg = new Error.Aggregate(
            new Error.NotFound(SampleResource),
            new Error.AuthenticationRequired());

        agg.Classify().Should().Be(RetryClassification.FailFast);
    }

    [Fact]
    public void Classify_aggregate_with_only_permanent_inners_returns_permanent()
    {
        var agg = new Error.Aggregate(
            new Error.NotFound(SampleResource),
            new Error.Forbidden("policy.deny"));

        agg.Classify().Should().Be(RetryClassification.Permanent);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Predicate helpers
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsTransient_true_when_classification_is_transient()
    {
        new Error.Unavailable().IsTransient().Should().BeTrue();
        new Error.NotFound(SampleResource).IsTransient().Should().BeFalse();
        new Error.AuthenticationRequired().IsTransient().Should().BeFalse();
    }

    [Fact]
    public void IsPermanent_true_when_classification_is_permanent()
    {
        new Error.NotFound(SampleResource).IsPermanent().Should().BeTrue();
        new Error.Unavailable().IsPermanent().Should().BeFalse();
        new Error.AuthenticationRequired().IsPermanent().Should().BeFalse();
    }

    [Fact]
    public void IsFailFast_true_when_classification_is_failfast()
    {
        new Error.AuthenticationRequired().IsFailFast().Should().BeTrue();
        new Error.Unavailable().IsFailFast().Should().BeFalse();
        new Error.NotFound(SampleResource).IsFailFast().Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetRetryAdvice
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetRetryAdvice_returns_advice_supplied_by_RateLimited()
    {
        var advice = new RetryAdvice(After: TimeSpan.FromSeconds(30));

        var error = new Error.RateLimited(advice);

        error.GetRetryAdvice().Should().Be(advice);
    }

    [Fact]
    public void GetRetryAdvice_returns_advice_supplied_by_Unavailable()
    {
        var at = DateTimeOffset.UtcNow.AddSeconds(15);
        var advice = new RetryAdvice(At: at);

        var error = new Error.Unavailable(ReasonCode: "db_unreachable", Retry: advice);

        error.GetRetryAdvice().Should().Be(advice);
    }

    [Fact]
    public void GetRetryAdvice_returns_null_when_RateLimited_has_no_advice() =>
        new Error.RateLimited().GetRetryAdvice().Should().BeNull();

    [Fact]
    public void GetRetryAdvice_returns_null_when_Unavailable_has_no_advice() =>
        new Error.Unavailable().GetRetryAdvice().Should().BeNull();

    public static TheoryData<Error> NonAdviceCarryingCases() => new()
    {
        new Error.Unexpected("io"),
        new Error.TransportFault(new SampleTransportFault("timeout")),
        new Error.AuthenticationRequired(),
        new Error.Forbidden("policy.deny"),
        Error.InvalidInput.ForField("name", "required"),
        new Error.InvariantViolation("rule"),
        new Error.NotFound(SampleResource),
        new Error.Gone(SampleResource),
        new Error.Conflict(SampleResource, "duplicate_key"),
    };

    [Theory]
    [MemberData(nameof(NonAdviceCarryingCases))]
    public void GetRetryAdvice_returns_null_for_cases_that_do_not_carry_advice(Error error) =>
        error.GetRetryAdvice().Should().BeNull();

    [Fact]
    public void GetRetryAdvice_returns_null_for_aggregate_even_when_inner_carries_advice()
    {
        // Aggregate intentionally returns null so callers cannot accidentally retry a
        // Permanent or FailFast aggregate using an inner Transient's advice.
        var advice = new RetryAdvice(After: TimeSpan.FromSeconds(30));
        var agg = new Error.Aggregate(
            new Error.NotFound(SampleResource),
            new Error.RateLimited(advice));

        agg.GetRetryAdvice().Should().BeNull();
    }

    [Fact]
    public void GetRetryAdvice_returns_null_for_aggregate_of_only_transient_inners()
    {
        // Even when every inner is Transient (so the aggregate classifies Transient),
        // the helper deliberately returns null to force consumers to apply their own
        // merge rule over potentially-conflicting inner hints.
        var agg = new Error.Aggregate(
            new Error.Unavailable(Retry: new RetryAdvice(After: TimeSpan.FromSeconds(1))),
            new Error.RateLimited(new RetryAdvice(After: TimeSpan.FromMinutes(1))));

        agg.GetRetryAdvice().Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Null-argument handling
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_throws_on_null()
    {
        var act = () => ErrorRetryExtensions.Classify(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("error");
    }

    [Fact]
    public void IsTransient_throws_on_null()
    {
        var act = () => ErrorRetryExtensions.IsTransient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsPermanent_throws_on_null()
    {
        var act = () => ErrorRetryExtensions.IsPermanent(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsFailFast_throws_on_null()
    {
        var act = () => ErrorRetryExtensions.IsFailFast(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetRetryAdvice_throws_on_null()
    {
        var act = () => ErrorRetryExtensions.GetRetryAdvice(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("error");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Enum severity ordering (relied on by aggregate max-semantics)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RetryClassification_numeric_ordering_encodes_severity_low_to_high()
    {
        // The ClassifyAggregate implementation relies on the ordering
        // Transient (0) < Permanent (1) < FailFast (2). Pin it so a future re-ordering
        // surfaces as a test failure rather than a silent semantic change.
        ((int)RetryClassification.Transient).Should().Be(0);
        ((int)RetryClassification.Permanent).Should().Be(1);
        ((int)RetryClassification.FailFast).Should().Be(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Exhaustiveness guard — fails loudly when a new Error nested case is added
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Discovers every nested <c>sealed record</c> derived from <see cref="Error"/> via
    /// reflection. If a new case is added to the closed catalog without updating
    /// <see cref="ErrorRetryExtensions.Classify(Error)"/>, this test fails with the name
    /// of the unhandled case so the author is forced to make an explicit decision.
    /// Aggregate is excluded because it is covered by dedicated aggregate-semantics tests.
    /// </summary>
    [Fact]
    public void Classify_handles_every_nested_Error_case_explicitly()
    {
        var nestedCases = typeof(Error)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsClass && !t.IsAbstract && typeof(Error).IsAssignableFrom(t))
            .Where(t => t != typeof(Error.Aggregate))
            .OrderBy(t => t.Name)
            .ToArray();

        nestedCases.Should().NotBeEmpty("the closed Error catalog must contain at least one case");

        var expectedHandledCases = new[]
        {
            nameof(Error.AuthenticationRequired),
            nameof(Error.Conflict),
            nameof(Error.Forbidden),
            nameof(Error.Gone),
            nameof(Error.InvalidInput),
            nameof(Error.InvariantViolation),
            nameof(Error.NotFound),
            nameof(Error.RateLimited),
            nameof(Error.TransportFault),
            nameof(Error.Unavailable),
            nameof(Error.Unexpected),
        };

        nestedCases.Select(t => t.Name).Should().BeEquivalentTo(
            expectedHandledCases,
            "every non-Aggregate nested Error case must have an explicit classification in " +
            "ErrorRetryExtensions.Classify (the fallback `_ => Permanent` is a safety net, not " +
            "a substitute for an intentional mapping). Add the new case to the switch in " +
            "ErrorRetryExtensions and to the expectedHandledCases list in this test.");
    }
}