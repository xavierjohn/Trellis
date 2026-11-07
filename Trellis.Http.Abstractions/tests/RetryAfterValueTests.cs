namespace Trellis.Http.Abstractions.Tests;

using System.Globalization;
using Trellis.Testing;

/// <summary>
/// <see cref="RetryAfterValue"/> is a two-case union (delay seconds or absolute date). Each factory must produce
/// exactly one case, the accessors must refuse the other case, and the header text must be the RFC 9110 form.
/// </summary>
public class RetryAfterValueTests
{
    private static readonly DateTimeOffset s_instant = new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(int.MaxValue)]
    public void FromSeconds_creates_only_the_delay_case_and_formats_as_decimal_seconds(int seconds)
    {
        var value = RetryAfterValue.FromSeconds(seconds);

        value.IsDelaySeconds.Should().BeTrue();
        value.IsDate.Should().BeFalse();
        value.DelaySeconds.Should().Be(seconds);
        value.ToHeaderValue().Should().Be(seconds.ToString(CultureInfo.InvariantCulture));
        value.ToString().Should().Be(value.ToHeaderValue());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void FromSeconds_rejects_a_negative_delay_naming_the_seconds_parameter(int seconds)
    {
        var act = () => RetryAfterValue.FromSeconds(seconds);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("seconds");
    }

    [Fact]
    public void FromDate_creates_only_the_date_case_and_keeps_the_instant()
    {
        var value = RetryAfterValue.FromDate(s_instant);

        value.IsDate.Should().BeTrue();
        value.IsDelaySeconds.Should().BeFalse();
        value.Date.Should().Be(s_instant);
    }

    [Fact]
    public void The_accessor_of_the_other_case_throws_InvalidOperationException()
    {
        var delay = RetryAfterValue.FromSeconds(30);
        var date = RetryAfterValue.FromDate(s_instant);

        Func<DateTimeOffset> readDateOfDelay = () => delay.Date;
        Func<int> readDelayOfDate = () => date.DelaySeconds;

        readDateOfDelay.Should().Throw<InvalidOperationException>();
        readDelayOfDate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_date_is_formatted_as_an_IMF_fixdate_in_GMT() =>
        RetryAfterValue.FromDate(s_instant).ToHeaderValue().Should().Be("Fri, 01 Jan 2027 12:00:00 GMT");

    [Fact]
    public void A_date_with_a_nonzero_offset_is_formatted_as_the_same_instant_in_GMT()
    {
        var plusFive = new DateTimeOffset(2027, 1, 1, 17, 0, 0, TimeSpan.FromHours(5));

        RetryAfterValue.FromDate(plusFive).ToHeaderValue().Should().Be("Fri, 01 Jan 2027 12:00:00 GMT");
    }

    [Fact]
    public void Values_of_the_same_case_and_payload_are_equal_with_equal_hash_codes()
    {
        var sameInstantOtherOffset = s_instant.ToOffset(TimeSpan.FromHours(-8));

        RetryAfterValue.FromSeconds(30).Should().Be(RetryAfterValue.FromSeconds(30));
        RetryAfterValue.FromSeconds(30).GetHashCode().Should().Be(RetryAfterValue.FromSeconds(30).GetHashCode());
        RetryAfterValue.FromDate(s_instant).Should().Be(RetryAfterValue.FromDate(sameInstantOtherOffset));
        RetryAfterValue.FromDate(s_instant).GetHashCode()
            .Should().Be(RetryAfterValue.FromDate(sameInstantOtherOffset).GetHashCode());
    }

    [Fact]
    public void Values_with_a_different_payload_or_case_are_not_equal()
    {
        RetryAfterValue.FromSeconds(30).Should().NotBe(RetryAfterValue.FromSeconds(31));
        RetryAfterValue.FromDate(s_instant).Should().NotBe(RetryAfterValue.FromDate(s_instant.AddSeconds(1)));
        RetryAfterValue.FromSeconds(0).Should().NotBe(RetryAfterValue.FromDate(default));
        RetryAfterValue.FromDate(default).Should().NotBe(RetryAfterValue.FromSeconds(0));
    }

    [Fact]
    public void A_value_is_not_equal_to_null_or_to_an_unrelated_object()
    {
        var value = RetryAfterValue.FromSeconds(30);

        RetryAfterValue? nothing = null;
        object? nothingAsObject = null;
        object unrelated = "30";
        object sameValue = RetryAfterValue.FromSeconds(30);

        value.Equals(nothing).Should().BeFalse();
        value.Equals(nothingAsObject).Should().BeFalse();
        value.Equals(unrelated).Should().BeFalse();
        value.Equals(sameValue).Should().BeTrue();
    }
}
