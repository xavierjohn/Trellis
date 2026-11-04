namespace Trellis.Asp.Tests;

using Trellis;
using Trellis.Asp.ModelBinding;

/// <summary>
/// Pins that every <see cref="PrimitiveConverter"/> rejection carries a field violation.
/// </summary>
/// <remarks>
/// The boundary's <c>ValidationProblem</c> gate keys on there being at least one field violation,
/// so a rejection that carried only a <c>Detail</c> rendered as an untyped problem no client could
/// dispatch on. The pointer is the root because this converter is field-agnostic by construction —
/// it is handed a raw string and a target type and nothing else — and the caller that does know
/// the field re-roots it.
/// </remarks>
public sealed class PrimitiveConverterViolationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void A_rejected_conversion_carries_a_root_relative_field_violation(string? raw)
    {
        var result = PrimitiveConverter.ConvertToPrimitive<int>(raw);

        result.TryGetError(out var error).Should().BeTrue();
        var invalid = error.Should().BeOfType<Error.InvalidInput>().Subject;

        var violation = invalid.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().BeEmpty("a field-agnostic converter can only honestly point at the root");
        violation.Field.In.Should().Be(InputLocation.Unspecified);
        violation.Detail.Should().NotBeNullOrEmpty("the existing English is preserved verbatim");
    }

    [Fact]
    public void The_violation_detail_matches_the_error_detail()
    {
        var result = PrimitiveConverter.ConvertToPrimitive<Guid>("nope");

        result.TryGetError(out var error).Should().BeTrue();
        var invalid = error.Should().BeOfType<Error.InvalidInput>().Subject;

        invalid.Fields.Items[0].Detail.Should().Be(invalid.Detail,
            "the two must not drift; they describe one failure");
    }

    [Fact]
    public void A_successful_conversion_still_succeeds()
    {
        PrimitiveConverter.ConvertToPrimitive<int>("42").TryGetValue(out var value).Should().BeTrue();
        value.Should().Be(42);
    }
}