namespace Trellis.Results.Tests;

using System.Collections.ObjectModel;
using Xunit;

public class AuthenticationChallengeTests
{
    [Fact]
    public void Bearer_with_all_params_sets_properties()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.Bearer(
            realm: "api",
            scope: "read write",
            error: "invalid_token",
            errorDescription: "The token has expired");

        // Assert
        challenge.Scheme.Should().Be("Bearer");
        challenge.Token68.Should().BeNull();
        challenge.Parameters.Should().NotBeNull();
        challenge.Parameters!["realm"].Should().Be("api");
        challenge.Parameters["scope"].Should().Be("read write");
        challenge.Parameters["error"].Should().Be("invalid_token");
        challenge.Parameters["error_description"].Should().Be("The token has expired");
    }

    [Fact]
    public void Bearer_with_no_params_has_null_parameters()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.Bearer();

        // Assert
        challenge.Scheme.Should().Be("Bearer");
        challenge.Token68.Should().BeNull();
        challenge.Parameters.Should().BeNull();
    }

    [Fact]
    public void Basic_with_realm_sets_parameters()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.Basic(realm: "My API");

        // Assert
        challenge.Scheme.Should().Be("Basic");
        challenge.Token68.Should().BeNull();
        challenge.Parameters.Should().NotBeNull();
        challenge.Parameters!["realm"].Should().Be("My API");
        challenge.Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void Basic_with_no_realm_has_null_parameters()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.Basic();

        // Assert
        challenge.Scheme.Should().Be("Basic");
        challenge.Token68.Should().BeNull();
        challenge.Parameters.Should().BeNull();
    }

    [Fact]
    public void Create_with_custom_scheme_and_no_parameters()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.Create("CustomScheme");

        // Assert
        challenge.Scheme.Should().Be("CustomScheme");
        challenge.Token68.Should().BeNull();
        challenge.Parameters.Should().BeNull();
    }

    [Fact]
    public void Create_with_custom_scheme_and_parameters()
    {
        // Arrange
        var parameters = new Dictionary<string, string>
        {
            ["realm"] = "example",
            ["charset"] = "UTF-8"
        };

        // Act
        var challenge = AuthenticationChallenge.Create("Negotiate", new ReadOnlyDictionary<string, string>(parameters));

        // Assert
        challenge.Scheme.Should().Be("Negotiate");
        challenge.Parameters.Should().NotBeNull();
        challenge.Parameters!["realm"].Should().Be("example");
        challenge.Parameters["charset"].Should().Be("UTF-8");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_invalid_scheme_throws(string? scheme)
    {
        // Arrange & Act
        var act = () => AuthenticationChallenge.Create(scheme!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateWithToken68_sets_token68()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.CreateWithToken68("Bearer", "mF_9.B5f-4.1JqM");

        // Assert
        challenge.Scheme.Should().Be("Bearer");
        challenge.Token68.Should().Be("mF_9.B5f-4.1JqM");
        challenge.Parameters.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithToken68_with_invalid_scheme_throws(string? scheme)
    {
        // Arrange & Act
        var act = () => AuthenticationChallenge.CreateWithToken68(scheme!, "token");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithToken68_with_invalid_token68_throws(string? token68)
    {
        // Arrange & Act
        var act = () => AuthenticationChallenge.CreateWithToken68("Bearer", token68!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToHeaderValue_formats_bearer_with_all_params()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Bearer(
            realm: "api",
            scope: "read write",
            error: "invalid_token");

        // Act
        var header = challenge.ToHeaderValue();

        // Assert
        header.Should().Be("Bearer realm=\"api\", scope=\"read write\", error=\"invalid_token\"");
    }

    [Fact]
    public void ToHeaderValue_formats_bearer_with_no_params()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Bearer();

        // Act
        var header = challenge.ToHeaderValue();

        // Assert
        header.Should().Be("Bearer");
    }

    [Fact]
    public void ToHeaderValue_formats_basic_with_realm()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Basic(realm: "My API");

        // Act
        var header = challenge.ToHeaderValue();

        // Assert
        header.Should().Be("Basic realm=\"My API\"");
    }

    [Fact]
    public void ToHeaderValue_formats_custom_scheme_with_no_params()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Create("Negotiate");

        // Act
        var header = challenge.ToHeaderValue();

        // Assert
        header.Should().Be("Negotiate");
    }

    [Fact]
    public void ToHeaderValue_formats_token68()
    {
        // Arrange
        var challenge = AuthenticationChallenge.CreateWithToken68("Bearer", "mF_9.B5f-4.1JqM");

        // Act
        var header = challenge.ToHeaderValue();

        // Assert
        header.Should().Be("Bearer mF_9.B5f-4.1JqM");
    }

    [Fact]
    public void ToString_returns_header_value()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Basic(realm: "test");

        // Act & Assert
        challenge.ToString().Should().Be(challenge.ToHeaderValue());
    }

    [Fact]
    public void Bearer_parameters_are_case_insensitive()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Bearer(realm: "api");

        // Act & Assert
        challenge.Parameters!["Realm"].Should().Be("api");
        challenge.Parameters["REALM"].Should().Be("api");
    }

    [Fact]
    public void UnauthorizedError_with_challenges_preserves_challenges()
    {
        // Arrange
        var challenges = new[]
        {
            AuthenticationChallenge.Bearer(realm: "api", scope: "read"),
            AuthenticationChallenge.Basic(realm: "My API")
        };

        // Act
        var error = new UnauthorizedError("Token expired", "unauthorized.error", challenges);

        // Assert
        error.Detail.Should().Be("Token expired");
        error.Code.Should().Be("unauthorized.error");
        error.Challenges.Should().NotBeNull();
        error.Challenges.Should().HaveCount(2);
        error.Challenges![0].Scheme.Should().Be("Bearer");
        error.Challenges[1].Scheme.Should().Be("Basic");
    }

    [Fact]
    public void UnauthorizedError_without_challenges_has_null_challenges()
    {
        // Arrange & Act
        var error = new UnauthorizedError("Auth required", "unauthorized.error");

        // Assert
        error.Challenges.Should().BeNull();
    }

    [Fact]
    public void Error_Unauthorized_factory_with_challenges()
    {
        // Arrange
        var challenges = new[] { AuthenticationChallenge.Bearer(realm: "api") };

        // Act
        var error = Error.Unauthorized("Token expired", challenges);

        // Assert
        error.Should().BeOfType<UnauthorizedError>();
        error.Detail.Should().Be("Token expired");
        error.Code.Should().Be("unauthorized.error");
        error.Challenges.Should().NotBeNull();
        error.Challenges.Should().HaveCount(1);
        error.Challenges![0].Scheme.Should().Be("Bearer");
    }

    [Fact]
    public void Error_Unauthorized_factory_with_challenges_and_instance()
    {
        // Arrange
        var challenges = new[] { AuthenticationChallenge.Basic(realm: "test") };

        // Act
        var error = Error.Unauthorized("Auth required", challenges, instance: "req-123");

        // Assert
        error.Instance.Should().Be("req-123");
        error.Challenges.Should().HaveCount(1);
    }

    [Fact]
    public void Bearer_with_partial_params_only_includes_provided()
    {
        // Arrange & Act
        var challenge = AuthenticationChallenge.Bearer(realm: "api", error: "invalid_token");

        // Assert
        challenge.Parameters.Should().HaveCount(2);
        challenge.Parameters!.ContainsKey("scope").Should().BeFalse();
        challenge.Parameters.ContainsKey("error_description").Should().BeFalse();
    }

    [Fact]
    public void ToHeaderValue_formats_bearer_with_error_description()
    {
        // Arrange
        var challenge = AuthenticationChallenge.Bearer(
            realm: "api",
            scope: "read write",
            error: "invalid_token",
            errorDescription: "The token has expired");

        // Act
        var header = challenge.ToHeaderValue();

        // Assert
        header.Should().Be(
            "Bearer realm=\"api\", scope=\"read write\", error=\"invalid_token\", error_description=\"The token has expired\"");
    }
}
