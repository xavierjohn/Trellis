using Trellis.Testing;
namespace Trellis.Core.Tests.DomainDrivenDesign.Aggregates;

using Trellis;

/// <summary>
/// Focused tests for the failure-mode differentiation in
/// <see cref="AggregateETagExtensions"/> — empty array vs. all-weak filter.
/// End-to-end behaviour of <c>OptionalETag</c> / <c>RequireETag</c> is covered by
/// <see cref="AggregateTests"/>.
/// </summary>
public class AggregateETagExtensionsTests
{
    [Fact]
    public void OptionalETag_EmptyArray_FailsWithEmptyHeaderMessage()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag(Array.Empty<EntityTagValue>());

        ensured.IsFailure.Should().BeTrue();
        var error = ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>().Subject;
        error.Detail.Should().Be("If-Match header is empty.");
        error.Condition.Should().Be(PreconditionKind.IfMatch);
    }

    [Fact]
    public void OptionalETag_AllWeakTags_FailsWithWeakOnlyMessage()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag([EntityTagValue.Weak("abc123"), EntityTagValue.Weak("def456")]);

        ensured.IsFailure.Should().BeTrue();
        var error = ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>().Subject;
        error.Detail.Should().Be("If-Match contains only weak ETags. Strong comparison is required.");
    }

    [Fact]
    public void OptionalETag_MixedWeakAndStrongMatching_StrongTagWins()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("current");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag([EntityTagValue.Weak("ignored"), EntityTagValue.Strong("current")]);

        ensured.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void OptionalETag_MixedWeakAndStrongNonMatching_FailsWithModifiedMessage()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("current");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag([EntityTagValue.Weak("current"), EntityTagValue.Strong("stale")]);

        ensured.IsFailure.Should().BeTrue();
        var error = ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>().Subject;
        error.Detail.Should().Be("Resource has been modified. Please reload and retry.");
    }

    [Fact]
    public void OptionalETag_WildcardMixedWithWeak_Succeeds()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("any");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag([EntityTagValue.Weak("ignored"), EntityTagValue.Wildcard()]);

        ensured.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RequireETag_EmptyArray_FailsWithEmptyHeaderMessage()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        var ensured = result.RequireETag(Array.Empty<EntityTagValue>());

        ensured.IsFailure.Should().BeTrue();
        ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>()
            .Which.Detail.Should().Be("If-Match header is empty.");
    }
}
