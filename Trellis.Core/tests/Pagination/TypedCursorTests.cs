namespace Trellis.Core.Tests.Pagination;

using System.Globalization;
using System.Text;

public class TypedCursorTests
{
    [Fact]
    public void Composite_SuccessfulNullComponent_ThrowsContractViolation()
    {
        var token = CursorCodec.Composite<string, int>().Encode(("value", 1));
        var codec = CursorCodec.Composite(new NullStateCodec(), CursorCodec.Scalar<int>());
        var act = () => codec.TryDecode(token);
        act.Should().Throw<InvalidOperationException>().WithMessage("*successful null*");
    }

    [Fact]
    public void PageRequest_SuccessfulNullCodecState_ThrowsInsteadOfRestarting()
    {
        PageRequest.TryCreate("supplied-token", 1).TryGetValue(out var request).Should().BeTrue();
        var act = () => request!.Decode(new NullStateCodec());
        act.Should().Throw<InvalidOperationException>().WithMessage("*successful null*");
    }

    [Fact]
    public void Create_SuccessfulNullParserState_ThrowsContractViolation()
    {
        var codec = CursorCodec.Create<string>("nullable", state => state, (_, _) => Result.Ok<string>(null!));
        var act = () => codec.TryDecode(Raw("1:x-nullable:payload"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*successful null*");
    }

    [Fact]
    public void Composite_StringsWithDelimiters_RoundTrips()
    {
        var codec = CursorCodec.Composite<string, string>();
        var state = ("a:|/雪", "b:|");
        codec.TryDecode(codec.Encode(state)).TryGetValue(out var decoded).Should().BeTrue();
        decoded.Should().Be(state);
    }

    [Fact]
    public void Composite_NestedState_RoundTrips()
    {
        var codec = CursorCodec.Composite(CursorCodec.Composite<double, int>(), CursorCodec.Scalar<string>());
        var state = ((1.23456789012345, 42), "geo:v2:lat=10&lon=20");
        codec.TryDecode(codec.Encode(state)).TryGetValue(out var decoded).Should().BeTrue();
        decoded.Should().Be(state);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e9999")]
    public void Scalar_UntrustedNonFinitePayload_Fails(string raw)
    {
        var codec = CursorCodec.Scalar<double>();
        var token = Raw("1:s:" + raw);
        var result = codec.TryDecode(token, "after");
        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        error.Fields[0].Field.Path.Should().Be("/after");
        error.Fields[0].ReasonCode.Should().Be("cursor.malformed");
    }

    [Theory]
    [InlineData("1:c:999999999999999999999:abc")]
    [InlineData("1:c:-1:abc")]
    [InlineData("1:c:0:abc")]
    [InlineData("1:c:3:abc")]
    [InlineData("2:c:1:ab")]
    public void Composite_MalformedFraming_ReturnsFailure(string payload) =>
        CursorCodec.Composite<string, string>().TryDecode(Raw(payload)).IsFailure.Should().BeTrue();

    [Fact]
    public void Map_DistanceAndQueryContext_ValidatesBothPaths()
    {
        var wire = CursorCodec.Composite(CursorCodec.Composite<double, int>(), CursorCodec.Scalar<string>());
        var codec = CursorCodec.Map<((double Primary, int Secondary) Primary, string Secondary), NearbyState>(
            wire,
            state => ((state.Distance, state.Id), state.Query),
            (state, field) => state.Primary.Primary >= 0 && state.Secondary == "query-a"
                ? Result.Ok(new NearbyState(state.Primary.Primary, state.Primary.Secondary, state.Secondary))
                : Result.Fail<NearbyState>(Error.InvalidInput.ForField(field ?? "cursor", "cursor.malformed", "Invalid distance or query context.")));

        var expected = new NearbyState(0.123, 5, "query-a");
        codec.TryDecode(codec.Encode(expected)).TryGetValue(out var decoded).Should().BeTrue();
        decoded.Should().Be(expected);
        codec.TryDecode(wire.Encode(((-1, 5), "query-a"))).IsFailure.Should().BeTrue();
        codec.TryDecode(wire.Encode(((1, 5), "query-b"))).IsFailure.Should().BeTrue();
        var encode = () => codec.Encode(new NearbyState(-1, 5, "query-a"));
        encode.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ExplicitFormatter_SupportsCustomState()
    {
        var codec = CursorCodec.Create<CustomKey>(
            "custom-key",
            key => key.Number.ToString(CultureInfo.InvariantCulture),
            (text, field) => int.TryParse(text, CultureInfo.InvariantCulture, out var value)
                ? Result.Ok(new CustomKey(value))
                : Result.Fail<CustomKey>(Error.InvalidInput.ForField(field ?? "cursor", "cursor.malformed", "Invalid key.")));
        codec.TryDecode(codec.Encode(new CustomKey(7))).TryGetValue(out var value).Should().BeTrue();
        value.Should().Be(new CustomKey(7));
    }

    [Fact]
    public void PageRequest_AbsentCursor_ReturnsSuccessfulAbsenceAndCappedDefault()
    {
        PageRequest.TryCreate(null, null, max: 5, defaultSize: 10).TryGetValue(out var request).Should().BeTrue();
        request!.Size.Should().Be(new PageSize(10, 5));
        request.Decode(CursorCodec.Scalar<int>()).TryGetValue(out var state).Should().BeTrue();
        state.HasValue.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void PageRequest_EmptyCursor_FailsWithoutRestarting(string cursor) =>
        PageRequest.TryCreate(cursor, 10).IsFailure.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PageRequest_InvalidLimit_ReturnsInputFailure(int limit) =>
        PageRequest.TryCreate(null, limit).IsFailure.Should().BeTrue();

    [Fact]
    public void PageRequest_AboveCap_ClampsOrExplicitlyRejects()
    {
        PageRequest.TryCreate(null, 200).TryGetValue(out var request).Should().BeTrue();
        request!.Size.Should().Be(new PageSize(200, 100));
        PageRequest.TryCreate(null, 200, policy: PageSizeLimitPolicy.Reject).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PageBuilder_CustomCursor_InvokesOnlyForLastRetainedRow()
    {
        var seen = new List<int>();
        Cursor Select(int row)
        {
            seen.Add(row);
            return new Cursor("provider-" + row.ToString(CultureInfo.InvariantCulture));
        }

        var size = new PageSize(2, 2);
        PageBuilder.FromOverFetch<int>([], size, Select).Next.Should().BeNull();
        PageBuilder.FromOverFetch<int>([1, 2], size, Select).Next.Should().BeNull();
        seen.Should().BeEmpty();
        PageBuilder.FromOverFetch<int>([1, 2, 3], size, Select).Next.Should().Be(new Cursor("provider-2"));
        seen.Should().Equal([2]);
    }

    [Fact]
    public void PageBuilder_NullContinuation_ThrowsInsteadOfClosingTraversal()
    {
        var act = () => PageBuilder.FromOverFetch<int>([1, 2], new PageSize(1, 1), _ => null!);
        act.Should().Throw<InvalidOperationException>();
    }

    private static Cursor Raw(string payload) =>
        new(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).Replace('+', '-').Replace('/', '_').TrimEnd('='));

    private sealed record NearbyState(double Distance, int Id, string Query);
    private sealed record CustomKey(int Number);

    private sealed class NullStateCodec : ICursorCodec<string>
    {
        public Cursor Encode(string state) => new(state);
        public Result<string> TryDecode(Cursor? cursor, string? fieldName = null) => Result.Ok<string>(null!);
    }
}
