namespace Trellis.Asp.Tests;

using Trellis;
using Trellis.Asp;

/// <summary>
/// Tests for <see cref="AggregateRepresentationValidator{T}"/>.
/// </summary>
public class RepresentationValidatorTests
{
    private readonly AggregateRepresentationValidator<FakeAggregate> _validator = new();

    [Fact]
    public void WithoutVariantKey_ReturnsAggregateETag()
    {
        var aggregate = new FakeAggregate("etag-abc");

        var result = _validator.GenerateETag(aggregate);

        result.OpaqueTag.Should().Be("etag-abc");
        result.IsWeak.Should().BeFalse();
    }

    [Fact]
    public void WithVariantKey_ReturnsDifferentETag()
    {
        var aggregate = new FakeAggregate("etag-abc");

        var withoutVariant = _validator.GenerateETag(aggregate);
        var withVariant = _validator.GenerateETag(aggregate, "gzip");

        withVariant.OpaqueTag.Should().NotBe(withoutVariant.OpaqueTag);
        withVariant.IsWeak.Should().BeFalse();
    }

    [Fact]
    public void DifferentVariantKeys_ProduceDifferentETags()
    {
        var aggregate = new FakeAggregate("etag-abc");

        var gzip = _validator.GenerateETag(aggregate, "gzip");
        var br = _validator.GenerateETag(aggregate, "br");

        gzip.OpaqueTag.Should().NotBe(br.OpaqueTag);
    }

    [Fact]
    public void SameVariantKey_ProducesSameETag()
    {
        var aggregate = new FakeAggregate("etag-abc");

        var first = _validator.GenerateETag(aggregate, "gzip");
        var second = _validator.GenerateETag(aggregate, "gzip");

        first.OpaqueTag.Should().Be(second.OpaqueTag);
    }

    #region Test double

    private sealed class FakeAggregate : IAggregate
    {
        public FakeAggregate(string eTag) => ETag = eTag;

        public string ETag { get; }
        public bool IsChanged => false;
        public IReadOnlyList<IDomainEvent> UncommittedEvents() => [];
        public void AcceptChanges() { }
    }

    #endregion
}