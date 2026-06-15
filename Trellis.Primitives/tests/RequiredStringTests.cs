namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;
using Trellis.Testing;

public partial class TrackingId : RequiredString<TrackingId>
{
}
internal partial class InternalTrackingId : RequiredString<InternalTrackingId>
{
}

public class RequiredStringTests
{
    [Theory]
    [MemberData(nameof(GetBadStringWithMessages))]
    public void Cannot_create_invalid_RequiredString(string? input, string expectedDetail)
    {
        var trackingId1 = TrackingId.TryCreate(input, null);
        trackingId1.IsFailure.Should().BeTrue();
        trackingId1.UnwrapError().Should().BeOfType<Error.InvalidInput>();
        var validation = (Error.InvalidInput)trackingId1.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/trackingId");
        validation.Fields[0].Detail.Should().Be(expectedDetail);
        validation.Fields[0].ReasonCode.Should().Be("validation.error");
    }

    [Fact]
    public void Can_create_RequiredString() =>
        InternalTrackingId.TryCreate("32141sd")
            .Tap(trackingId =>
            {
                trackingId.Should().BeOfType<InternalTrackingId>();
                trackingId.ToString(CultureInfo.InvariantCulture).Should().Be("32141sd");
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Two_RequiredString_with_same_value_should_be_equal() =>
        TrackingId.TryCreate("SameValue")
            .Combine(TrackingId.TryCreate("SameValue"))
            .Tap((tr1, tr2) =>
            {
                (tr1 == tr2).Should().BeTrue();
                tr1.Equals(tr2).Should().BeTrue();
                tr1.GetHashCode().Should().Be(tr2.GetHashCode());
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Two_RequiredString_with_different_value_should_be_not_equal() =>
        TrackingId.TryCreate("Value1")
            .Combine(TrackingId.TryCreate("Value2"))
            .Tap((tr1, tr2) =>
             {
                 (tr1 != tr2).Should().BeTrue();
                 tr1.Equals(tr2).Should().BeFalse();
             })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Can_implicitly_cast_to_string()
    {
        // Arrange
        TrackingId trackingId1 = TrackingId.TryCreate("32141sd").Unwrap();

        // Act
        string strTracking = trackingId1;

        // Assert
        strTracking.Should().Be("32141sd");
    }

    [Fact]
    public void Can_explicitly_cast_to_RequiredString()
    {
        // Arrange

        // Act
        TrackingId trackingId1 = (TrackingId)"32141sd";

        // Assert
        trackingId1.Should().Be(TrackingId.TryCreate("32141sd").Unwrap());
    }

    [Fact]
    public void Can_use_ToString()
    {
        // Arrange
        TrackingId trackingId1 = TrackingId.TryCreate("32141sd").Unwrap();

        // Act
        var strTracking = trackingId1.ToString(CultureInfo.InvariantCulture);

        // Assert
        strTracking.Should().Be("32141sd");
    }

    [Fact]
    public void Can_cast_empty_to_RequiredString()
    {
        // Lenient default — casting "" succeeds and stores it verbatim.
        TrackingId trackingId = (TrackingId)string.Empty;

        trackingId.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void Can_create_RequiredString_from_try_parsing_valid_string()
    {
        // Arrange
        var strTrackingId = "32141sd";

        // Act
        TrackingId.TryParse(strTrackingId, null, out var trackingId)
            .Should().BeTrue();

        // Assert
        trackingId.Should().BeOfType<TrackingId>();
        trackingId!.ToString(CultureInfo.InvariantCulture).Should().Be(strTrackingId);
    }

    [Theory]
    [MemberData(nameof(GetBadStringWithMessages))]
    public void Cannot_create_RequiredString_from_try_parsing_invalid_string(string? input, string _)
    {
        // Arrange

        // Act
        TrackingId.TryParse(input, null, out var myId)
            .Should().BeFalse();

        // Assert
        myId.Should().BeNull();
    }

    [Fact]
    public void Can_create_RequiredString_from_parsing_valid_string()
    {
        // Arrange
        var strTrackingId = "32141sd";

        // Act
        var trackingId = TrackingId.Parse(strTrackingId, null);

        // Assert
        trackingId.Should().BeOfType<TrackingId>();
        trackingId.ToString(CultureInfo.InvariantCulture).Should().Be(strTrackingId);
    }

    [Theory]
    [MemberData(nameof(GetBadStringWithMessages))]
    public void Cannot_create_RequiredString_from_parsing_invalid_string(string? input, string expectedDetail)
    {
        // Arrange

        // Act
        Action act = () => TrackingId.Parse(input!, null);

        // Assert
        act.Should().Throw<FormatException>()
            .WithMessage(expectedDetail);
    }

    [Fact]
    public void Can_use_Contains()
    {
        // Arrange
        var id1 = TrackingId.TryCreate("id1").Unwrap();
        var id2 = TrackingId.TryCreate("id2").Unwrap();
        IReadOnlyList<TrackingId> ids = new List<TrackingId> { id1, id2 };

        // Act
        var actual = ids.Contains(id1);

        // Assert
        actual.Should().BeTrue();
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var strTrackingId = "MyTrackingId";
        TrackingId trackingId = TrackingId.TryCreate(strTrackingId).Unwrap();
        var expected = JsonSerializer.Serialize(strTrackingId);

        // Act
        var actual = JsonSerializer.Serialize(trackingId);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        string str = "MyTrackingId";
        var json = JsonSerializer.Serialize(str);

        // Act
        TrackingId actual = JsonSerializer.Deserialize<TrackingId>(json)!;

        // Assert
        actual.Value.Should().Be(str);
    }

    [Fact]
    public void Can_create_RequiredString_from_parsing_empty_string_in_json()
    {
        // Lenient default — empty JSON string deserializes successfully and stores "" verbatim.
        var json = JsonSerializer.Serialize(string.Empty);

        TrackingId actual = JsonSerializer.Deserialize<TrackingId>(json)!;

        actual.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void TryCreate_with_whitespace_padding_should_not_trim()
    {
        // Lenient default — no auto-trim. Input is stored verbatim.
        var result = TrackingId.TryCreate("  ABC123  ");

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("  ABC123  ",
            "RequiredString does not auto-trim under the lenient default; opt in with [Trim].");
    }

    public static TheoryData<string?, string> GetBadStringWithMessages() =>
      new TheoryData<string?, string>
      {
              { default(string), "Tracking Id cannot be null." }
      };

    #region StartsWith, Contains, EndsWith, Length

    [Fact]
    public void StartsWith_returns_true_when_value_starts_with_prefix()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.StartsWith("ABC").Should().BeTrue();
    }

    [Fact]
    public void StartsWith_returns_false_when_value_does_not_start_with_prefix()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.StartsWith("XYZ").Should().BeFalse();
    }

    [Fact]
    public void Contains_returns_true_when_value_contains_substring()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.Contains("-123").Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_when_value_does_not_contain_substring()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.Contains("XYZ").Should().BeFalse();
    }

    [Fact]
    public void EndsWith_returns_true_when_value_ends_with_suffix()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.EndsWith("345").Should().BeTrue();
    }

    [Fact]
    public void EndsWith_returns_false_when_value_does_not_end_with_suffix()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.EndsWith("XYZ").Should().BeFalse();
    }

    [Fact]
    public void Length_returns_string_length()
    {
        var tracking = TrackingId.Create("ABC-12345");
        tracking.Length.Should().Be(9);
    }

    [Fact]
    public void Length_of_single_char_string()
    {
        var tracking = TrackingId.Create("X");
        tracking.Length.Should().Be(1);
    }

    #endregion
}