namespace Trellis.Core.Tests.Pagination;

using System;

/// <summary>
/// Unit tests for the <see cref="PageSize"/> request/applied pair carrying both the
/// limit the client asked for and the limit the server actually applied.
/// </summary>
public class PageSizeTests
{
    [Fact]
    public void Constants_have_sensible_defaults()
    {
        PageSize.Default.Should().Be(50);
        PageSize.Max.Should().Be(100);
    }

    [Fact]
    public void Constructor_round_trips_valid_values()
    {
        var size = new PageSize(requested: 25, applied: 10);

        size.Requested.Should().Be(25);
        size.Applied.Should().Be(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_requested(int requested)
    {
        var act = () => new PageSize(requested, 1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(requested));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_applied(int applied)
    {
        var act = () => new PageSize(10, applied);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(applied));
    }

    [Fact]
    public void Constructor_rejects_applied_greater_than_requested()
    {
        var act = () => new PageSize(requested: 5, applied: 10);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("applied");
    }

    [Fact]
    public void WasCapped_true_when_applied_less_than_requested()
    {
        var size = new PageSize(requested: 100, applied: 50);

        size.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void WasCapped_false_when_applied_equals_requested()
    {
        var size = new PageSize(requested: 50, applied: 50);

        size.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void FromRequested_returns_default_when_requested_is_null()
    {
        var size = PageSize.FromRequested(null);

        size.Requested.Should().Be(PageSize.Default);
        size.Applied.Should().Be(PageSize.Default);
        size.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void FromRequested_preserves_requested_verbatim_when_capped_by_max()
    {
        // Critical: WasCapped must remain observable. Clamping Requested would destroy it.
        var size = PageSize.FromRequested(1000);

        size.Requested.Should().Be(1000);
        size.Applied.Should().Be(PageSize.Max);
        size.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void FromRequested_under_max_returns_unchanged_applied()
    {
        var size = PageSize.FromRequested(25);

        size.Requested.Should().Be(25);
        size.Applied.Should().Be(25);
        size.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void FromRequested_respects_custom_max_parameter()
    {
        var size = PageSize.FromRequested(20, max: 5);

        size.Requested.Should().Be(20);
        size.Applied.Should().Be(5);
        size.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void FromRequested_with_custom_max_does_not_cap_when_under()
    {
        var size = PageSize.FromRequested(3, max: 5);

        size.Requested.Should().Be(3);
        size.Applied.Should().Be(3);
        size.WasCapped.Should().BeFalse();
    }

    [Fact]
    public void TryCreate_returns_success_for_valid_requested()
    {
        var result = PageSize.TryCreate(50);

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var size).Should().BeTrue();
        size!.Requested.Should().Be(50);
        size.Applied.Should().Be(50);
    }

    [Fact]
    public void TryCreate_returns_default_when_requested_is_null()
    {
        var result = PageSize.TryCreate(null);

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var size).Should().BeTrue();
        size!.Requested.Should().Be(PageSize.Default);
        size.Applied.Should().Be(PageSize.Default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryCreate_fails_when_requested_is_non_positive(int requested)
    {
        var result = PageSize.TryCreate(requested);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void TryCreate_fails_when_requested_exceeds_max()
    {
        var result = PageSize.TryCreate(101, policy: PageSizeLimitPolicy.Reject);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void TryCreate_strict_does_not_clamp()
    {
        var result = PageSize.TryCreate(1000, policy: PageSizeLimitPolicy.Reject);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_respects_custom_max()
    {
        PageSize.TryCreate(5, max: 5).IsSuccess.Should().BeTrue();
        PageSize.TryCreate(6, max: 5, policy: PageSizeLimitPolicy.Reject).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_propagates_field_name_into_error()
    {
        var result = PageSize.TryCreate(1000, fieldName: "limit", policy: PageSizeLimitPolicy.Reject);

        result.IsFailure.Should().BeTrue();
        var invalid = result.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        invalid.Fields.Items.Should().Contain(f => f.Field.ToString().Contains("limit"));
    }

    [Fact]
    public void Equality_treats_records_with_same_values_as_equal()
    {
        var a = new PageSize(50, 50);
        var b = new PageSize(50, 50);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Composes_into_Page_constructor_without_violation()
    {
        var size = PageSize.FromRequested(200);
        var page = new Page<int>([1, 2, 3], Next: null, Previous: null, RequestedLimit: size.Requested, AppliedLimit: size.Applied);

        page.RequestedLimit.Should().Be(200);
        page.AppliedLimit.Should().Be(PageSize.Max);
        page.WasCapped.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromRequested_throws_when_max_is_non_positive(int max)
    {
        var act = () => PageSize.FromRequested(10, max);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(max));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryCreate_throws_when_max_is_non_positive(int max)
    {
        var act = () => PageSize.TryCreate(10, max);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(max));
    }

    [Fact]
    public void FromRequested_with_null_clamps_default_to_smaller_max()
    {
        // When max < Default, FromRequested(null) returns Requested=Default but
        // Applied is clamped down to max so WasCapped is observable. Documented
        // shape that TryCreate(null, max) mirrors.
        var size = PageSize.FromRequested(null, max: 10);

        size.Requested.Should().Be(PageSize.Default);
        size.Applied.Should().Be(10);
        size.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void TryCreate_with_null_clamps_default_to_smaller_max()
    {
        // When max < Default, TryCreate(null) returns Requested=Default but Applied=max.
        // Documented behaviour: mirrors FromRequested(null, max).
        var result = PageSize.TryCreate(null, max: 10);

        result.IsSuccess.Should().BeTrue();
        result.TryGetValue(out var size).Should().BeTrue();
        size!.Requested.Should().Be(PageSize.Default);
        size.Applied.Should().Be(10);
        size.WasCapped.Should().BeTrue();
    }
}