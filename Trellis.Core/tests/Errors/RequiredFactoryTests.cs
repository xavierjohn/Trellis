namespace Trellis.Core.Tests.Errors;

using System.Text.Json;

public class RequiredFactoryTests
{
    [Theory]
    [InlineData("name", "/name", null)]
    [InlineData("name", "/name", "Name is required.")]
    [InlineData("a/~b", "/a~1~0b", "Required.")]
    [InlineData("/items/0/name", "/items/0/name", "Required.")]
    [InlineData("", "", null)]
    [InlineData(null, "", null)]
    public void Required_String_FieldNormalization_MatchesCanonicalForField(string? field, string path, string? detail)
    {
        var expected = Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, field, detail: detail);

        var error = Error.InvalidInput.Required(field, detail);
        var violation = FieldViolation.Required(field, detail);

        error.Equals(expected).Should().BeTrue();
        error.GetHashCode().Should().Be(expected.GetHashCode());
        JsonSerializer.Serialize(error).Should().Be(JsonSerializer.Serialize(expected));
        error.Code.Should().Be(ValidationCodes.Unspecified);
        error.Detail.Should().BeNull();
        error.Rules.Items.Should().BeEmpty();
        error.Fields.Items.Should().ContainSingle().Which.Should().Be(violation);
        violation.Field.Path.Should().Be(path);
        violation.Field.In.Should().Be(InputLocation.Unspecified);
        violation.ReasonCode.Should().Be(ValidationCodes.ValueNotNull);
        violation.Detail.Should().Be(detail);
        violation.Args.Should().BeNull();
    }

    [Theory]
    [InlineData(InputLocation.Unspecified)]
    [InlineData(InputLocation.Body)]
    [InlineData(InputLocation.Query)]
    [InlineData(InputLocation.Path)]
    [InlineData(InputLocation.Header)]
    public void Required_Pointer_InputLocation_IsPreserved(InputLocation location)
    {
        var field = new InputPointer("/items/0/name", location);
        var expected = Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, field, detail: "Required.");

        var error = Error.InvalidInput.Required(field, "Required.");
        var violation = FieldViolation.Required(field, "Required.");

        error.Equals(expected).Should().BeTrue();
        error.GetHashCode().Should().Be(expected.GetHashCode());
        JsonSerializer.Serialize(error).Should().Be(JsonSerializer.Serialize(expected));
        error.Code.Should().Be(ValidationCodes.Unspecified);
        error.Detail.Should().BeNull();
        error.Rules.Items.Should().BeEmpty();
        error.Fields.Items.Should().ContainSingle().Which.Should().Be(violation);
        violation.Should().Be(new FieldViolation(field, ValidationCodes.ValueNotNull, Detail: "Required."));
        violation.Field.Should().Be(field);
        violation.Args.Should().BeNull();
    }

    [Fact]
    public void Required_Pointer_DefaultPointer_TargetsRoot()
    {
        var error = Error.InvalidInput.Required(default(InputPointer));
        var violation = FieldViolation.Required(default(InputPointer));

        error.Should().Be(Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, InputPointer.Root));
        error.Fields.Items.Should().ContainSingle().Which.Should().Be(violation);
        violation.Field.Should().Be(InputPointer.Root);
        violation.Detail.Should().BeNull();
    }

    [Fact]
    public void Required_String_MalformedPointer_Throws()
    {
        var error = () => Error.InvalidInput.Required("/invalid~2");
        var violation = () => FieldViolation.Required("/invalid~2");

        error.Should().Throw<ArgumentException>();
        violation.Should().Throw<ArgumentException>();
    }
}
