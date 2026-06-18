namespace Trellis.Primitives.Tests;

using System;
using Trellis.Testing;

public partial class DefaultGuid : RequiredGuid<DefaultGuid> { }
[NotDefault] public partial class NotDefaultRequiredGuid : RequiredGuid<NotDefaultRequiredGuid> { }

public partial class DefaultDateTime : RequiredDateTime<DefaultDateTime> { }
[NotDefault] public partial class NotDefaultRequiredDateTime : RequiredDateTime<NotDefaultRequiredDateTime> { }

public partial class DefaultDateTimeOffset : RequiredDateTimeOffset<DefaultDateTimeOffset> { }
[NotDefault] public partial class NotDefaultRequiredDateTimeOffset : RequiredDateTimeOffset<NotDefaultRequiredDateTimeOffset> { }

public partial class DefaultString : RequiredString<DefaultString> { }
[NotDefault] public partial class NotDefaultRequiredString : RequiredString<NotDefaultRequiredString> { }
[Trim] public partial class TrimRequiredString : RequiredString<TrimRequiredString> { }
[Trim, NotDefault] public partial class TrimNotDefaultRequiredString : RequiredString<TrimNotDefaultRequiredString> { }

public partial class DefaultInt : RequiredInt<DefaultInt> { }
[NotDefault] public partial class NotDefaultRequiredInt : RequiredInt<NotDefaultRequiredInt> { }

public partial class DefaultLong : RequiredLong<DefaultLong> { }
[NotDefault] public partial class NotDefaultRequiredLong : RequiredLong<NotDefaultRequiredLong> { }

public partial class DefaultDecimal : RequiredDecimal<DefaultDecimal> { }
[NotDefault] public partial class NotDefaultRequiredDecimal : RequiredDecimal<NotDefaultRequiredDecimal> { }

[Range(1, 100)] public partial class DefaultRangedInt : RequiredInt<DefaultRangedInt> { }
[Range(1L, 100L)] public partial class DefaultRangedLong : RequiredLong<DefaultRangedLong> { }
[Range(1, 100)] public partial class DefaultRangedDecimal : RequiredDecimal<DefaultRangedDecimal> { }

public class RequiredDefaultsTests
{
    [Fact]
    public void RequiredGuid_Default_AcceptsGuidEmpty()
    {
        var result = DefaultGuid.TryCreate(Guid.Empty);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void RequiredGuid_Default_AcceptsParsedAllZeroString()
    {
        var result = DefaultGuid.TryCreate("00000000-0000-0000-0000-000000000000");

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void RequiredGuid_NotDefault_RejectsGuidEmpty()
    {
        var result = NotDefaultRequiredGuid.TryCreate(Guid.Empty);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Not Default Required Guid cannot be Guid.Empty.");
    }

    [Fact]
    public void RequiredDateTime_Default_AcceptsMinValue()
    {
        var result = DefaultDateTime.TryCreate(DateTime.MinValue);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void RequiredDateTime_NotDefault_RejectsMinValue()
    {
        var result = NotDefaultRequiredDateTime.TryCreate(DateTime.MinValue);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Not Default Required Date Time cannot be DateTime.MinValue.");
    }

    [Fact]
    public void RequiredDateTimeOffset_Default_AcceptsMinValue()
    {
        var result = DefaultDateTimeOffset.TryCreate(DateTimeOffset.MinValue);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public void RequiredDateTimeOffset_NotDefault_RejectsMinValue()
    {
        var result = NotDefaultRequiredDateTimeOffset.TryCreate(DateTimeOffset.MinValue);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Not Default Required Date Time Offset cannot be DateTimeOffset.MinValue.");
    }

    [Fact]
    public void RequiredString_Default_AcceptsAllConcreteValuesVerbatim()
    {
        AssertStringFailure(DefaultString.TryCreate((string?)null), "Default String cannot be null.");
        DefaultString.TryCreate("").Unwrap().Value.Should().Be("");
        DefaultString.TryCreate("   ").Unwrap().Value.Should().Be("   ");
        DefaultString.TryCreate(" a ").Unwrap().Value.Should().Be(" a ");
        DefaultString.TryCreate("a").Unwrap().Value.Should().Be("a");
    }

    [Fact]
    public void RequiredString_NotDefault_RejectsEmptyString()
    {
        AssertStringFailure(NotDefaultRequiredString.TryCreate(""), "Not Default Required String cannot be empty.");
        NotDefaultRequiredString.TryCreate("a").Unwrap().Value.Should().Be("a");
        NotDefaultRequiredString.TryCreate("   ").Unwrap().Value.Should().Be("   ");
    }

    [Fact]
    public void RequiredString_Trim_TrimsValue()
    {
        TrimRequiredString.TryCreate(" a ").Unwrap().Value.Should().Be("a");
        TrimRequiredString.TryCreate("   ").Unwrap().Value.Should().Be("");
        TrimRequiredString.TryCreate("").Unwrap().Value.Should().Be("");
    }

    [Fact]
    public void RequiredString_TrimNotDefault_RejectsWhitespaceOnlyAfterTrim()
    {
        AssertStringFailure(TrimNotDefaultRequiredString.TryCreate(""), "Trim Not Default Required String cannot be empty.");
        AssertStringFailure(TrimNotDefaultRequiredString.TryCreate("   "), "Trim Not Default Required String cannot be empty.");
        TrimNotDefaultRequiredString.TryCreate(" a ").Unwrap().Value.Should().Be("a");
    }

    [Fact]
    public void RequiredInt_Default_AcceptsZero()
    {
        var result = DefaultInt.TryCreate(0);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(0);
    }

    [Fact]
    public void RequiredInt_NotDefault_RejectsZero()
    {
        var result = NotDefaultRequiredInt.TryCreate(0);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Not Default Required Int cannot be zero.");
    }

    [Fact]
    public void RequiredLong_Default_AcceptsZero()
    {
        var result = DefaultLong.TryCreate(0L);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(0L);
    }

    [Fact]
    public void RequiredLong_NotDefault_RejectsZero()
    {
        var result = NotDefaultRequiredLong.TryCreate(0L);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Not Default Required Long cannot be zero.");
    }

    [Fact]
    public void RequiredDecimal_Default_AcceptsZero()
    {
        var result = DefaultDecimal.TryCreate(0m);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be(0m);
    }

    [Fact]
    public void RequiredDecimal_NotDefault_RejectsZero()
    {
        var result = NotDefaultRequiredDecimal.TryCreate(0m);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Not Default Required Decimal cannot be zero.");
    }

    [Fact]
    public void RequiredInt_WithRange_ZeroSurfacesRangeMessage()
    {
        var result = DefaultRangedInt.TryCreate(0);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Default Ranged Int must be at least 1.");
    }

    [Fact]
    public void RequiredInt_WithRange_NullableZeroSurfacesRangeMessage()
    {
        var result = DefaultRangedInt.TryCreate((int?)0);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Default Ranged Int must be at least 1.");
    }

    [Fact]
    public void RequiredInt_WithRange_StringZeroSurfacesRangeMessage()
    {
        var result = DefaultRangedInt.TryCreate("0");

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Default Ranged Int must be at least 1.");
    }

    [Fact]
    public void RequiredLong_WithRange_ZeroSurfacesRangeMessage()
    {
        var result = DefaultRangedLong.TryCreate(0L);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Default Ranged Long must be at least 1.");
    }

    [Fact]
    public void RequiredDecimal_WithRange_ZeroSurfacesRangeMessage()
    {
        var result = DefaultRangedDecimal.TryCreate(0m);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Default Ranged Decimal must be at least 1.");
    }

    [Fact]
    public void RequiredInt_WithRange_NonZeroBelowMinimumStillSurfacesRangeMessage()
    {
        var result = DefaultRangedInt.TryCreate(-5);

        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Default Ranged Int must be at least 1.");
    }

    private static void AssertStringFailure<T>(Result<T> result, string expectedDetail)
    {
        result.IsFailure.Should().BeTrue();
        var validation = (Error.InvalidInput)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be(expectedDetail);
    }
}