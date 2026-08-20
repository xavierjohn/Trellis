namespace Trellis.Core.Tests.Errors;

/// <summary>
/// Tests for the <see cref="InputLocation"/> dimension of <see cref="InputPointer"/>.
///
/// A location is what a producer knows about *where* an offending value came from. The
/// three name factories (<c>ForQuery</c>, <c>ForPath</c>, <c>ForHeader</c>) store the
/// parameter name as a single RFC 6901-escaped token, so a name containing '/' or '~'
/// survives the round trip as one name rather than decomposing into path segments.
/// <c>ForBody</c> is deliberately shaped unlike the other three: it takes a pointer, not
/// a name, and is exempt from single-token escaping.
/// </summary>
public class InputPointerLocationTests
{
    [Fact]
    public void ForProperty_is_unspecified_so_existing_behaviour_is_unchanged() =>
        InputPointer.ForProperty("Email").In.Should().Be(InputLocation.Unspecified);

    [Fact]
    public void Root_is_unspecified() =>
        InputPointer.Root.In.Should().Be(InputLocation.Unspecified);

    [Fact]
    public void Default_struct_is_unspecified() =>
        default(InputPointer).In.Should().Be(InputLocation.Unspecified);

    // --- ForBody: pointer semantics, identical to ForProperty except for the stamp ---

    [Fact]
    public void ForBody_stamps_body()
    {
        var pointer = InputPointer.ForBody("displayName");

        pointer.Path.Should().Be("/displayName");
        pointer.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void ForBody_passes_through_an_already_formed_pointer()
    {
        var pointer = InputPointer.ForBody("/items/0/quantity");

        pointer.Path.Should().Be("/items/0/quantity");
        pointer.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void ForBody_escapes_a_simple_name_like_ForProperty() =>
        InputPointer.ForBody("a~b").Path.Should().Be("/a~0b");

    [Fact]
    public void ForBody_empty_is_root_path_stamped_body()
    {
        var pointer = InputPointer.ForBody("");

        pointer.Path.Should().Be("");
        pointer.In.Should().Be(InputLocation.Body);
    }

    /// <summary>
    /// <c>InputPointer.Root</c> is the documented idiom for a whole-body violation, so
    /// <c>ForBody("")</c> must remain distinguishable from it — otherwise an application
    /// reporting "this body is internally inconsistent" emits <c>in: "unknown"</c>, which
    /// is the exact defect <c>ForBody</c> exists to remove.
    /// </summary>
    [Fact]
    public void ForBody_empty_is_not_Root() =>
        InputPointer.ForBody("").Should().NotBe(InputPointer.Root);

    [Fact]
    public void ForBody_and_ForProperty_differ_despite_an_identical_path()
    {
        var body = InputPointer.ForBody("/a");
        var property = InputPointer.ForProperty("/a");

        body.Path.Should().Be(property.Path);
        body.Should().NotBe(property);
    }

    // --- the three name factories: single-token escaping ---

    [Theory]
    [InlineData("id", "/id")]
    [InlineData("a/b", "/a~1b")]
    [InlineData("a~b", "/a~0b")]
    [InlineData("/id", "/~1id")]
    public void ForQuery_stores_the_name_as_one_escaped_token(string name, string expectedPath)
    {
        var pointer = InputPointer.ForQuery(name);

        pointer.Path.Should().Be(expectedPath);
        pointer.In.Should().Be(InputLocation.Query);
    }

    /// <summary>
    /// The row that separates a name factory from <c>ForProperty</c>/<c>ForBody</c>: a
    /// parameter literally named "/id" is a name, not a pointer, and is escaped as one.
    /// </summary>
    [Fact]
    public void ForQuery_escapes_a_leading_slash_rather_than_treating_it_as_a_pointer()
    {
        InputPointer.ForQuery("/id").Path.Should().Be("/~1id");
        InputPointer.ForProperty("/id").Path.Should().Be("/id");
    }

    [Fact]
    public void ForPath_stamps_path()
    {
        var pointer = InputPointer.ForPath("a/b");

        pointer.Path.Should().Be("/a~1b");
        pointer.In.Should().Be(InputLocation.Path);
    }

    [Fact]
    public void ForHeader_stamps_header()
    {
        var pointer = InputPointer.ForHeader("If-Match");

        pointer.Path.Should().Be("/If-Match");
        pointer.In.Should().Be(InputLocation.Header);
    }

    /// <summary>
    /// An empty name is rejected rather than collapsing to root: root is meaningful for a
    /// body pointer and meaningless for a named parameter.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Name_factories_reject_an_empty_name(string? name)
    {
        var forQuery = () => InputPointer.ForQuery(name!);
        var forPath = () => InputPointer.ForPath(name!);
        var forHeader = () => InputPointer.ForHeader(name!);

        forQuery.Should().Throw<ArgumentException>();
        forPath.Should().Throw<ArgumentException>();
        forHeader.Should().Throw<ArgumentException>();
    }

    // --- equality incorporates In, not just Path ---

    [Fact]
    public void Same_path_and_same_location_are_equal()
    {
        InputPointer.ForQuery("id").Should().Be(InputPointer.ForQuery("id"));
        InputPointer.ForQuery("id").GetHashCode().Should().Be(InputPointer.ForQuery("id").GetHashCode());
    }

    /// <summary>
    /// Without this, four factories producing four different wire locations would all be
    /// mutually equal, and <c>ValidationErrorsContext</c>'s de-duplication would silently
    /// collapse distinct locations into one.
    /// </summary>
    [Fact]
    public void Same_path_and_different_location_are_not_equal()
    {
        InputPointer.ForQuery("id").Should().NotBe(InputPointer.ForPath("id"));
        InputPointer.ForPath("id").Should().NotBe(InputPointer.ForHeader("id"));
        InputPointer.ForHeader("id").Should().NotBe(InputPointer.ForProperty("id"));
        InputPointer.ForProperty("id").Should().NotBe(InputPointer.ForQuery("id"));
    }

    [Fact]
    public void Default_still_equals_Root()
    {
        default(InputPointer).Should().Be(InputPointer.Root);
        default(InputPointer).GetHashCode().Should().Be(InputPointer.Root.GetHashCode());
    }

    // --- construction and deconstruction ---

    [Fact]
    public void Two_argument_constructor_carries_the_location()
    {
        var pointer = new InputPointer("/a", InputLocation.Header);

        pointer.Path.Should().Be("/a");
        pointer.In.Should().Be(InputLocation.Header);
    }

    [Fact]
    public void Single_value_Deconstruct_is_preserved_for_source_compatibility()
    {
        InputPointer.ForBody("/a").Deconstruct(out var path);

        path.Should().Be("/a");
    }

    [Fact]
    public void Two_value_Deconstruct_yields_path_and_location()
    {
        var (path, @in) = InputPointer.ForQuery("id");

        path.Should().Be("/id");
        @in.Should().Be(InputLocation.Query);
    }

    [Fact]
    public void With_expression_can_set_the_location()
    {
        var pointer = InputPointer.ForProperty("Email") with { In = InputLocation.Body };

        pointer.Path.Should().Be("/Email");
        pointer.In.Should().Be(InputLocation.Body);
    }
}
