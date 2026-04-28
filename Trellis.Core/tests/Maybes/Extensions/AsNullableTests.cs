namespace Trellis.Core.Tests.Maybes.Extensions;

using Trellis;

public class AsNullableTests
{
    [Fact]
    public void Struct_nullable_conversion_equality_none()
    {
        Maybe<double> none = default;
        double? noneNullable = none.AsNullable();
        noneNullable.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Struct_nullable_conversion_equality_some()
    {
        Maybe<double> some = 123;
        double? someNullable = some.AsNullable();
        someNullable.HasValue.Should().BeTrue();
        someNullable.Should().Be(123);
    }
}