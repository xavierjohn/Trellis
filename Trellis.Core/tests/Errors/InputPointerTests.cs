namespace Trellis.Core.Tests.Errors;

/// <summary>
/// Tests for <see cref="InputPointer"/>'s RFC 6901 (JSON Pointer) compliance.
/// RFC 6901 §3 requires that the special characters '~' and '/' inside a property name
/// be escaped as "~0" and "~1" respectively (and the order matters: '~' must be escaped
/// FIRST, otherwise '/' → '~1' would be re-escaped as '~01').
/// </summary>
public class InputPointerTests
{
    [Theory]
    [InlineData("", "email", "/email")]
    [InlineData("/owner", "email", "/owner/email")]
    [InlineData("/owner~1name", "~/", "/owner~1name/~0~1")]
    [InlineData("/owner", "/child", "/owner/~1child")]
    [InlineData("/owner", "~1", "/owner/~01")]
    [InlineData("", "", "/")]
    [InlineData("/owner", "", "/owner/")]
    public void AppendProperty_LiteralName_EscapesOneSegment(string parent, string name, string expected)
    {
        var pointer = new InputPointer(parent, InputLocation.Body);

        var appended = pointer.AppendProperty(name);

        appended.Path.Should().Be(expected);
        appended.In.Should().Be(InputLocation.Body);
        pointer.Path.Should().Be(parent);
    }

    [Fact]
    public void AppendProperty_DefaultPointer_AppendsFromRoot()
    {
        var pointer = default(InputPointer);

        pointer.AppendProperty("name").Should().Be(new InputPointer("/name"));
    }

    [Fact]
    public void AppendProperty_NullName_Throws()
    {
        var act = () => InputPointer.Root.AppendProperty(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("propertyName");
    }

    [Theory]
    [InlineData(0, "/0")]
    [InlineData(1, "/1")]
    [InlineData(int.MaxValue, "/2147483647")]
    public void AppendIndex_Root_AppendsInvariantIndex(int index, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");

            InputPointer.Root.AppendIndex(index).Path.Should().Be(expected);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AppendIndex_NestedPointer_PreservesLocation()
    {
        var pointer = InputPointer.ForBody("/openingHours")
            .AppendProperty("periods").AppendIndex(1).AppendProperty("open");

        pointer.Should().Be(new InputPointer("/openingHours/periods/1/open", InputLocation.Body));
    }

    [Theory]
    [InlineData(InputLocation.Unspecified)]
    [InlineData(InputLocation.Body)]
    [InlineData(InputLocation.Query)]
    [InlineData(InputLocation.Path)]
    [InlineData(InputLocation.Header)]
    public void AppendPropertyAndIndex_AllLocations_PreserveLocation(InputLocation location)
    {
        var pointer = new InputPointer("/items", location).AppendIndex(2).AppendProperty("name");

        pointer.In.Should().Be(location);
    }

    [Fact]
    public void AppendIndex_NegativeIndex_Throws()
    {
        var act = () => InputPointer.Root.AppendIndex(-1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("index");
    }

    [Fact]
    public void ForProperty_with_simple_name_prepends_slash() =>
        InputPointer.ForProperty("Email").Path.Should().Be("/Email");

    [Fact]
    public void ForProperty_with_empty_returns_root() =>
        InputPointer.ForProperty("").Should().Be(InputPointer.Root);

    [Fact]
    public void Default_struct_represents_root()
    {
        var pointer = default(InputPointer);

        pointer.Path.Should().Be("");
        pointer.ToString().Should().Be("");
        pointer.Should().Be(InputPointer.Root);
    }

    [Fact]
    public void Object_initializer_validates_and_sets_path()
    {
        var pointer = new InputPointer { Path = "/email" };

        pointer.Path.Should().Be("/email");
    }

    [Fact]
    public void With_expression_validates_and_sets_path()
    {
        var pointer = new InputPointer("/email") with { Path = "/name" };

        pointer.Path.Should().Be("/name");
    }

    [Fact]
    public void Deconstruct_returns_path()
    {
        new InputPointer("/email").Deconstruct(out var path);

        path.Should().Be("/email");
    }

    [Fact]
    public void ForProperty_with_null_returns_root() =>
        InputPointer.ForProperty(null!).Should().Be(InputPointer.Root);

    [Fact]
    public void ForProperty_with_existing_pointer_does_not_double_escape() =>
        // Caller passing a fully-formed pointer (e.g. from JsonPointerNormalizer) must be preserved.
        InputPointer.ForProperty("/Lines/0/Memo").Path.Should().Be("/Lines/0/Memo");

    [Fact]
    public void ForProperty_escapes_tilde_per_rfc_6901() =>
        // RFC 6901 §3: '~' must be escaped as '~0' so the pointer can roundtrip.
        InputPointer.ForProperty("data~field").Path.Should().Be("/data~0field");

    [Fact]
    public void ForProperty_escapes_slash_in_property_name_per_rfc_6901() =>
        // RFC 6901 §3: '/' inside a property name must be escaped as '~1'.
        // Otherwise "email/work" would parse back as a nested pointer (/email then /work)
        // rather than a single property literally named "email/work".
        InputPointer.ForProperty("email/work").Path.Should().Be("/email~1work");

    [Fact]
    public void ForProperty_escapes_tilde_before_slash_per_rfc_6901_order() =>
        // RFC 6901 §3 mandates the escape order: '~' first, then '/'. Otherwise
        // '/' → '~1' would be re-escaped as '~01' on the second pass.
        // Input: "~/" should produce "/~0~1", NOT "/~01".
        InputPointer.ForProperty("~/").Path.Should().Be("/~0~1");

    [Theory]
    [InlineData("email")]
    [InlineData("items/0/quantity")]
    public void Constructor_rejects_non_pointer_paths(string path)
    {
        var act = () => new InputPointer(path);

        act.Should().Throw<ArgumentException>().WithParameterName("Path");
    }

    [Theory]
    [InlineData("/bad~")]
    [InlineData("/bad~2")]
    [InlineData("/bad~~")]
    public void Constructor_rejects_invalid_tilde_escapes(string path)
    {
        var act = () => new InputPointer(path);

        act.Should().Throw<ArgumentException>().WithParameterName("Path");
    }
}