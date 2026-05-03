namespace Trellis.Core.Tests.Errors;

using System.Collections.Immutable;

public class AuthChallengeTests
{
    [Fact]
    public void Equality_ignores_scheme_and_parameter_key_case()
    {
        var left = new AuthChallenge(
            "Bearer",
            ImmutableDictionary<string, string>.Empty
                .Add("realm", "api")
                .Add("scope", "read"));
        var right = new AuthChallenge(
            "bearer",
            ImmutableDictionary<string, string>.Empty
                .Add("Realm", "api")
                .Add("SCOPE", "read"));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Equality_keeps_parameter_values_case_sensitive()
    {
        var left = new AuthChallenge(
            "Bearer",
            ImmutableDictionary<string, string>.Empty.Add("realm", "api"));
        var right = new AuthChallenge(
            "Bearer",
            ImmutableDictionary<string, string>.Empty.Add("realm", "API"));

        left.Should().NotBe(right);
    }
}
