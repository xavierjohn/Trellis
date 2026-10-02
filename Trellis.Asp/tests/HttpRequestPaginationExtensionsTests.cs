namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;

public sealed class HttpRequestPaginationExtensionsTests
{
    [Fact]
    public void TryCreatePageRequest_Defaults_MissingParameters_CreatesFirstPage()
    {
        var pageRequest = Parse(string.Empty).GetValueOrThrow();

        pageRequest.Cursor.Should().BeNull();
        pageRequest.Size.Requested.Should().Be(PageSize.Default);
        pageRequest.Size.Applied.Should().Be(PageSize.Default);
    }

    [Fact]
    public void TryCreatePageRequest_Defaults_ValidValues_PreservesCursorAndLimit()
    {
        var pageRequest = Parse("?cursor=opaque-token&limit=25").GetValueOrThrow();

        pageRequest.Cursor.Should().Be(new Cursor("opaque-token"));
        pageRequest.Size.Requested.Should().Be(25);
        pageRequest.Size.Applied.Should().Be(25);
    }

    [Theory]
    [InlineData("?cursor=")]
    [InlineData("?cursor=%20%20")]
    public void TryCreatePageRequest_CursorPresentButBlank_ReturnsQueryLocatedMalformedFailure(string query)
    {
        var violation = Failure(Parse(query));

        violation.ReasonCode.Should().Be(ValidationCodes.CursorMalformed);
        violation.Field.Should().Be(InputPointer.ForQuery("cursor"));
    }

    [Theory]
    [InlineData("?cursor=first&cursor=second")]
    [InlineData("?cursor=same&cursor=same")]
    public void TryCreatePageRequest_CursorRepeated_ReturnsQueryLocatedMalformedFailure(string query)
    {
        var violation = Failure(Parse(query));

        violation.ReasonCode.Should().Be(ValidationCodes.CursorMalformed);
        violation.Field.Should().Be(InputPointer.ForQuery("cursor"));
        violation.Detail.Should().Contain("at most once");
    }

    [Theory]
    [InlineData("?limit=")]
    [InlineData("?limit=%20")]
    [InlineData("?limit=not-an-integer")]
    [InlineData("?limit=2147483648")]
    public void TryCreatePageRequest_LimitNotParseable_ReturnsQueryLocatedIntegerFailure(string query)
    {
        var violation = Failure(Parse(query));

        violation.ReasonCode.Should().Be(ValidationCodes.FormatInteger);
        violation.Field.Should().Be(InputPointer.ForQuery("limit"));
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=-1")]
    public void TryCreatePageRequest_LimitNotPositive_ReturnsQueryLocatedRangeFailure(string query)
    {
        var violation = Failure(Parse(query));

        violation.ReasonCode.Should().Be(ValidationCodes.PageSizeOutOfRange);
        violation.Field.Should().Be(InputPointer.ForQuery("limit"));
    }

    [Theory]
    [InlineData("?limit=10&limit=20")]
    [InlineData("?limit=10&limit=10")]
    public void TryCreatePageRequest_LimitRepeated_ReturnsQueryLocatedIntegerFailure(string query)
    {
        var violation = Failure(Parse(query));

        violation.ReasonCode.Should().Be(ValidationCodes.FormatInteger);
        violation.Field.Should().Be(InputPointer.ForQuery("limit"));
        violation.Detail.Should().Contain("at most once");
    }

    [Fact]
    public void TryCreatePageRequest_ClampPolicy_AboveMaximumCapsAppliedLimit()
    {
        var pageRequest = Parse("?limit=125", max: 100).GetValueOrThrow();

        pageRequest.Size.Requested.Should().Be(125);
        pageRequest.Size.Applied.Should().Be(100);
        pageRequest.Size.WasCapped.Should().BeTrue();
    }

    [Fact]
    public void TryCreatePageRequest_RejectPolicy_AboveMaximumReturnsQueryLocatedRangeFailure()
    {
        var violation = Failure(Parse("?limit=125", max: 100, policy: PageSizeLimitPolicy.Reject));

        violation.ReasonCode.Should().Be(ValidationCodes.PageSizeOutOfRange);
        violation.Field.Should().Be(InputPointer.ForQuery("limit"));
    }

    [Theory]
    [InlineData("?pageSize=0", PageSizeLimitPolicy.Clamp, "pageSize must be positive.")]
    [InlineData("?pageSize=125", PageSizeLimitPolicy.Reject, "pageSize must be at most 100.")]
    public void TryCreatePageRequest_CustomLimitName_RangeFailureUsesConfiguredNameInDetail(
        string query,
        PageSizeLimitPolicy policy,
        string expectedDetail)
    {
        var violation = Failure(Parse(
            query,
            cursorParameter: "after",
            limitParameter: "pageSize",
            max: 100,
            policy: policy));

        violation.ReasonCode.Should().Be(ValidationCodes.PageSizeOutOfRange);
        violation.Field.Should().Be(InputPointer.ForQuery("pageSize"));
        violation.Detail.Should().Be(expectedDetail);
    }

    [Fact]
    public void TryCreatePageRequest_CustomNames_ValidValuesUseConfiguredParameters()
    {
        var pageRequest = Parse(
            "?after=opaque-token&pageSize=30",
            cursorParameter: "after",
            limitParameter: "pageSize").GetValueOrThrow();

        pageRequest.Cursor.Should().Be(new Cursor("opaque-token"));
        pageRequest.Size.Requested.Should().Be(30);
    }

    [Theory]
    [InlineData("?after%2Fvalue=", "after/value", "/after~1value")]
    [InlineData("?after~value=", "after~value", "/after~0value")]
    public void TryCreatePageRequest_CustomEscapedName_FailurePreservesSingleQueryToken(
        string query,
        string cursorParameter,
        string expectedPath)
    {
        var violation = Failure(Parse(
            query,
            cursorParameter,
            limitParameter: "pageSize"));

        violation.ReasonCode.Should().Be(ValidationCodes.CursorMalformed);
        violation.Field.Should().Be(InputPointer.ForQuery(cursorParameter));
        violation.Field.Path.Should().Be(expectedPath);
    }

    [Fact]
    public void TryCreatePageRequest_InvalidSizeAndRepeatedCursor_ValidatesSizeFirst()
    {
        var violation = Failure(Parse("?cursor=first&cursor=second&limit=0"));

        violation.ReasonCode.Should().Be(ValidationCodes.PageSizeOutOfRange);
        violation.Field.Should().Be(InputPointer.ForQuery("limit"));
    }

    [Fact]
    public void TryCreatePageRequest_ParameterNamesNotDistinct_ThrowsArgumentException()
    {
        var request = NewRequest(string.Empty);

        FluentActions.Invoking(() => request.TryCreatePageRequest("page", "PAGE"))
            .Should().Throw<ArgumentException>()
            .WithParameterName("limitParameter");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreatePageRequest_InvalidCursorParameterName_ThrowsArgumentException(string? cursorParameter)
    {
        var request = NewRequest(string.Empty);

        FluentActions.Invoking(() => request.TryCreatePageRequest(cursorParameter!, "limit"))
            .Should().Throw<ArgumentException>()
            .WithParameterName(nameof(cursorParameter));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreatePageRequest_InvalidLimitParameterName_ThrowsArgumentException(string? limitParameter)
    {
        var request = NewRequest(string.Empty);

        FluentActions.Invoking(() => request.TryCreatePageRequest("cursor", limitParameter!))
            .Should().Throw<ArgumentException>()
            .WithParameterName(nameof(limitParameter));
    }

    [Fact]
    public void TryCreatePageRequest_NullRequest_ThrowsArgumentNullException()
    {
        HttpRequest request = null!;

        FluentActions.Invoking(() => request.TryCreatePageRequest())
            .Should().Throw<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void TryCreatePageRequest_InvalidPageConfiguration_PropagatesPageRequestExceptions()
    {
        var request = NewRequest(string.Empty);

        FluentActions.Invoking(() => request.TryCreatePageRequest(max: 0))
            .Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("max");
        FluentActions.Invoking(() => request.TryCreatePageRequest(defaultSize: 0))
            .Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("defaultSize");
        FluentActions.Invoking(() => request.TryCreatePageRequest(policy: (PageSizeLimitPolicy)42))
            .Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("policy");
    }

    [Fact]
    public void TryCreatePageRequest_InvalidPageConfigurationAndMalformedLimit_ThrowsConfigurationException()
    {
        var request = NewRequest("?limit=not-an-integer");

        FluentActions.Invoking(() => request.TryCreatePageRequest(max: 0))
            .Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("max");
    }

    private static Result<PageRequest> Parse(
        string query,
        string cursorParameter = "cursor",
        string limitParameter = "limit",
        int max = PageSize.Max,
        int defaultSize = PageSize.Default,
        PageSizeLimitPolicy policy = PageSizeLimitPolicy.Clamp) =>
        NewRequest(query).TryCreatePageRequest(
            cursorParameter,
            limitParameter,
            max,
            defaultSize,
            policy);

    private static HttpRequest NewRequest(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        return context.Request;
    }

    private static FieldViolation Failure(Result<PageRequest> result)
    {
        result.IsFailure.Should().BeTrue();
        var invalid = result.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        return invalid.Fields.Items.Should().ContainSingle().Which;
    }
}
