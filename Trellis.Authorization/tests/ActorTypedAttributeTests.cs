namespace Trellis.Authorization.Tests;

using Trellis.Testing;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// Tests for the typed actor-attribute accessors that parse an attribute string into a
/// string-backed scalar value object through its <c>TryCreate</c> factory.
/// </summary>
public class ActorTypedAttributeTests
{
    private const string ScopeKey = "scope";

    private static Actor WithAttribute(string key, string value) =>
        new("user-1", new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string> { [key] = value });

    private static Actor WithNoAttributes() =>
        new("user-1", new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>());

    [Fact]
    public void GetRequiredAttribute_present_and_valid_returns_typed_value()
    {
        var actor = WithAttribute(ScopeKey, "tenant-7");

        var result = actor.GetRequiredAttribute<ActorId>(ScopeKey);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Value.Should().Be("tenant-7");
    }

    [Fact]
    public void GetRequiredAttribute_missing_returns_failure_referencing_the_key()
    {
        var actor = WithNoAttributes();

        var result = actor.GetRequiredAttribute<ActorId>(ScopeKey);

        result.IsFailure.Should().BeTrue();
        var invalid = result.Error.Should().BeOfType<Error.InvalidInput>().Subject;
        invalid.Fields[0].Field.Path.Should().Be("/scope", "the failed attribute key identifies the error field");
    }

    [Fact]
    public void GetRequiredAttribute_present_but_invalid_returns_failure()
    {
        // ActorId is [Trim, NotDefault]: whitespace trims to empty and is rejected.
        var actor = WithAttribute(ScopeKey, "   ");

        var result = actor.GetRequiredAttribute<ActorId>(ScopeKey);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<Error.InvalidInput>();
    }

    [Fact]
    public void GetRequiredAttribute_null_key_throws()
    {
        var actor = WithNoAttributes();

        var act = () => actor.GetRequiredAttribute<ActorId>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryGetAttribute_present_and_valid_returns_true_and_value()
    {
        var actor = WithAttribute(ScopeKey, "tenant-7");

        var ok = actor.TryGetAttribute<ActorId>(ScopeKey, out var scope);

        ok.Should().BeTrue();
        scope!.Value.Should().Be("tenant-7");
    }

    [Fact]
    public void TryGetAttribute_missing_returns_false_and_null()
    {
        var actor = WithNoAttributes();

        var ok = actor.TryGetAttribute<ActorId>(ScopeKey, out var scope);

        ok.Should().BeFalse();
        scope.Should().BeNull();
    }

    [Fact]
    public void TryGetAttribute_present_but_invalid_returns_false_and_null()
    {
        var actor = WithAttribute(ScopeKey, "   ");

        var ok = actor.TryGetAttribute<ActorId>(ScopeKey, out var scope);

        ok.Should().BeFalse();
        scope.Should().BeNull();
    }

    [Fact]
    public void TryGetAttribute_null_key_throws()
    {
        var actor = WithNoAttributes();

        var act = () => actor.TryGetAttribute<ActorId>(null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }
}

#pragma warning restore CA1707
