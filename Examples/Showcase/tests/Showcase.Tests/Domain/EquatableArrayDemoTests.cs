namespace Trellis.Showcase.Tests.Domain;

using Trellis;
using Trellis.Primitives;

/// <summary>
/// Demonstrates that Error cases compose with value-equality via EquatableArray, so
/// two Result.Fail values built from semantically identical inputs are themselves equal.
/// </summary>
public class EquatableArrayDemoTests
{
    [Fact]
    public void Two_UnprocessableContent_with_same_field_violations_are_equal()
    {
        var a = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), "validation.required") { Detail = "Email is required" }));

        var b = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), "validation.required") { Detail = "Email is required" }));

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Differing_field_violations_break_equality()
    {
        var a = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), "validation.required")));

        var b = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("name"), "validation.required")));

        a.Should().NotBe(b);
    }
}
