namespace Trellis.Results.Tests.Errors;

public class ValidationErrorTests
{
    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
    public void For_creates_single_field_error_with_expected_field_and_message()
    {
        var ve = ValidationError.For("Email", "Must be valid");

        ve.FieldErrors.Should().ContainSingle();
        var fe = ve.FieldErrors[0];
        fe.FieldName.Should().Be("Email");
        fe.Details.Should().BeEquivalentTo(new[] { "Must be valid" });
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
    public void And_returns_new_instance_and_preserves_original()
    {
        var first = ValidationError.For("Email", "Must be valid");
        var second = first.And("Password", "Too short");

        // Immutability expectation
        first.FieldErrors.Should().ContainSingle();
        second.FieldErrors.Should().HaveCount(2);

        second.FieldErrors.Select(f => f.FieldName)
            .Should().BeEquivalentTo(new[] { "Email", "Password" });
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
    public void And_same_field_deduplicates_messages()
    {
        var ve = ValidationError.For("Email", "Must be valid")
            .And("Email", "Must be valid")              // duplicate
            .And("Email", "Must contain @");

        var email = ve.FieldErrors.Single(f => f.FieldName == "Email");
        email.Details.Should().BeEquivalentTo(new[] { "Must be valid", "Must contain @" });
    }

    [Fact]
    public void Merge_different_codes_concatenates_code_and_detail()
    {
        var a = new ValidationError("Invalid", "Email", "codeA", "Detail A");
        var b = new ValidationError("Too short", "Password", "codeB", "Detail B");

        var merged = a.Merge(b);

        merged.Code.Should().Be("codeA+codeB");
        merged.Detail.Should().Be("Detail A | Detail B");
        merged.FieldErrors.Should().HaveCount(2);
    }

    [Fact]
    public void For_field_and_message_not_swapped()
    {
        var ve = ValidationError.For("FieldX", "MessageY");
        ve.FieldErrors[0].FieldName.Should().Be("FieldX");
        ve.FieldErrors[0].Details[0].Should().Be("MessageY");
    }

    [Fact]
    public void ToDictionary_DuplicateFieldEntries_MergesMessagesInsteadOfOverwriting()
    {
        var error = new ValidationError(
        [
            new ValidationError.FieldError("email", ["Required"]),
            new ValidationError.FieldError("email", ["Invalid format"]),
            new ValidationError.FieldError("password", ["Too short"])
        ],
        "validation.error");

        var dictionary = error.ToDictionary();

        dictionary.Should().ContainKey("email");
        dictionary["email"].Should().Equal(["Required", "Invalid format"]);
        dictionary["password"].Should().Equal(["Too short"]);
    }
}