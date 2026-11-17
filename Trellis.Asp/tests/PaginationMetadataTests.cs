namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Builder;

public sealed class PaginationMetadataTests
{
    [Theory]
    [InlineData(null, "limit")]
    [InlineData(" ", "limit")]
    [InlineData("cursor", "")]
    [InlineData("cursor", "CURSOR")]
    public void PaginationAttribute_Constructor_InvalidNames_ThrowsArgumentException(
        string? cursorParameter, string? limitParameter) =>
        FluentActions.Invoking(() => new PaginationAttribute(cursorParameter!, limitParameter!))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(0, 10, PageSizeLimitPolicy.Clamp, "max")]
    [InlineData(int.MaxValue, 10, PageSizeLimitPolicy.Clamp, "max")]
    [InlineData(10, 0, PageSizeLimitPolicy.Clamp, "defaultSize")]
    [InlineData(10, 10, (PageSizeLimitPolicy)42, "policy")]
    public void PaginationAttribute_Constructor_InvalidConfiguration_ThrowsArgumentOutOfRangeException(
        int max, int defaultSize, PageSizeLimitPolicy policy, string parameter) =>
        FluentActions.Invoking(() => new PaginationAttribute(max: max, defaultSize: defaultSize, policy: policy))
            .Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);

    [Fact]
    public void WithPagination_Builder_Null_ThrowsArgumentNullException()
    {
        RouteHandlerBuilder builder = null!;

        FluentActions.Invoking(() => builder.WithPagination())
            .Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }
}
