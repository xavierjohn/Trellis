namespace Trellis.Core.Tests.Errors;

using Trellis.Testing;

/// <summary>
/// Tests for <see cref="Error.AuthenticationRequired"/>, the closed-ADT case used when an operation
/// requires authentication that was not supplied or could not be validated.
/// <see cref="Error.AuthenticationRequired.Scheme"/> is a <c>WWW-Authenticate</c> hint;
/// <see cref="Error.Code"/> is the optional machine-readable code that lets consumers (telemetry,
/// dashboards, client branching) distinguish causes that share the 401 surface — for example
/// <c>"Authentication.InvalidCredentials"</c> vs <c>"Authentication.MissingCredentials"</c> —
/// without parsing <see cref="Error.Detail"/>.
/// </summary>
public class AuthenticationRequiredErrorTests
{
    [Fact]
    public void Kind_is_authentication_required() =>
        new Error.AuthenticationRequired().Kind.Should().Be("authentication-required");

    [Fact]
    public void Code_is_the_sentinel_when_no_reason_is_named() =>
        new Error.AuthenticationRequired().Code.Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void Code_is_the_sentinel_when_only_Scheme_supplied() =>
        new Error.AuthenticationRequired(Scheme: "Bearer").Code.Should().Be(ValidationCodes.Unspecified);

    [Fact]
    public void Code_is_the_reason_the_producer_named() =>
        new Error.AuthenticationRequired { Code = "Authentication.InvalidCredentials" }
            .Code.Should().Be("Authentication.InvalidCredentials");

    [Fact]
    public void Code_and_Scheme_are_independent() =>
        new Error.AuthenticationRequired(Scheme: "Bearer") { Code = "Authentication.TokenExpired" }
            .Code.Should().Be("Authentication.TokenExpired");

    [Fact]
    public void Construct_with_no_args_leaves_Scheme_null_and_Code_unspecified()
    {
        var error = new Error.AuthenticationRequired();

        error.Scheme.Should().BeNull();
        error.Code.Should().Be(ValidationCodes.Unspecified);
    }

    [Fact]
    public void Construct_with_Scheme_positional_leaves_Code_unspecified()
    {
        var error = new Error.AuthenticationRequired("Bearer");

        error.Scheme.Should().Be("Bearer");
        error.Code.Should().Be(ValidationCodes.Unspecified);
    }

    [Fact]
    public void Construct_with_Code_only_leaves_Scheme_null()
    {
        var error = new Error.AuthenticationRequired { Code = "Authentication.MissingCredentials" };

        error.Scheme.Should().BeNull();
        error.Code.Should().Be("Authentication.MissingCredentials");
    }

    [Fact]
    public void Construct_with_Scheme_and_Code_preserves_both()
    {
        var error = new Error.AuthenticationRequired(Scheme: "Bearer") { Code = "Authentication.InvalidCredentials" };

        error.Scheme.Should().Be("Bearer");
        error.Code.Should().Be("Authentication.InvalidCredentials");
    }

    [Fact]
    public void Detail_init_property_inherited_from_base()
    {
        var error = new Error.AuthenticationRequired(Scheme: "Bearer")
        {
            Code = "Authentication.InvalidCredentials",
            Detail = "The supplied credentials were not recognised.",
        };

        error.Detail.Should().Be("The supplied credentials were not recognised.");
    }

    [Fact]
    public void GetDisplayMessage_prefers_Detail_when_set()
    {
        var error = new Error.AuthenticationRequired
        {
            Code = "Authentication.InvalidCredentials",
            Detail = "human-readable detail",
        };

        error.GetDisplayMessage().Should().Be("human-readable detail");
    }

    [Fact]
    public void GetDisplayMessage_falls_back_to_Code_when_Detail_null_and_a_reason_is_named() =>
        new Error.AuthenticationRequired { Code = "Authentication.TokenExpired" }
            .GetDisplayMessage().Should().Be("Authentication.TokenExpired");

    [Fact]
    public void GetDisplayMessage_falls_back_to_Kind_when_neither_Detail_nor_a_reason_is_set() =>
        new Error.AuthenticationRequired().GetDisplayMessage().Should().Be("authentication-required");

    [Fact]
    public void Two_AuthenticationRequired_with_same_payload_are_equal()
    {
        var a = new Error.AuthenticationRequired(Scheme: "Bearer") { Code = "Authentication.InvalidCredentials" };
        var b = new Error.AuthenticationRequired(Scheme: "Bearer") { Code = "Authentication.InvalidCredentials" };

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_with_different_Code_are_not_equal()
    {
        var a = new Error.AuthenticationRequired { Code = "Authentication.InvalidCredentials" };
        var b = new Error.AuthenticationRequired { Code = "Authentication.TokenExpired" };

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Two_with_different_Scheme_are_not_equal()
    {
        var a = new Error.AuthenticationRequired(Scheme: "Bearer");
        var b = new Error.AuthenticationRequired(Scheme: "Basic");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Unspecified_vs_named_Code_are_not_equal()
    {
        var a = new Error.AuthenticationRequired(Scheme: "Bearer");
        var b = new Error.AuthenticationRequired(Scheme: "Bearer") { Code = "Authentication.InvalidCredentials" };

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Switch_pattern_matches_as_distinct_case_and_exposes_Code()
    {
        Error error = new Error.AuthenticationRequired { Code = "Authentication.InvalidCredentials" };

        var matched = error switch
        {
            Error.AuthenticationRequired ar => $"auth-required:{ar.Code}",
            _ => "other",
        };

        matched.Should().Be("auth-required:Authentication.InvalidCredentials");
    }

    [Fact]
    public void ToString_includes_Kind_and_Code()
    {
        var error = new Error.AuthenticationRequired { Code = "Authentication.InvalidCredentials" };

        error.ToString().Should().Contain("authentication-required");
        error.ToString().Should().Contain("Authentication.InvalidCredentials");
    }
}
