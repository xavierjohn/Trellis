namespace Trellis.Core.Tests.Pagination;

using System.Globalization;

public class PaginationRegressionTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Scalar_DateTime_RoundTrip_PreservesTicksAndKind(DateTimeKind kind)
    {
        var value = new DateTime(2026, 9, 12, 12, 0, 0, kind).AddTicks(1234567);
        var result = CursorCodec.TryDecode<DateTime>(CursorCodec.Encode(value));

        result.TryGetValue(out var decoded).Should().BeTrue();
        decoded.Ticks.Should().Be(value.Ticks);
        decoded.Kind.Should().Be(kind);
    }

    [Fact]
    public void Scalar_DateTimeOffset_RoundTrip_PreservesTicksAndOffset()
    {
        var value = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.FromHours(5.5)).AddTicks(1234567);
        var result = CursorCodec.TryDecode<DateTimeOffset>(CursorCodec.Encode(value));

        result.TryGetValue(out var decoded).Should().BeTrue();
        decoded.Ticks.Should().Be(value.Ticks);
        decoded.Offset.Should().Be(value.Offset);
    }

    [Fact]
    public void Encode_OversizedKey_RejectsUnusableContinuation()
    {
        var act = () => CursorCodec.Encode(new string('x', 1024));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PageSize_MaximumApplied_RejectsOverFetchOverflow()
    {
        var act = () => new PageSize(int.MaxValue, int.MaxValue);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Encode_NonFiniteKey_RejectsInvalidBoundary(double value)
    {
        var act = () => CursorCodec.Encode(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Scalar_Double_RoundTrip_PreservesBitsAcrossCultures()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var value = Math.BitIncrement(1.0);
            var token = CursorCodec.Encode(value);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            CursorCodec.TryDecode<double>(token).TryGetValue(out var decoded).Should().BeTrue();
            BitConverter.DoubleToInt64Bits(decoded).Should().Be(BitConverter.DoubleToInt64Bits(value));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
