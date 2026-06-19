namespace Trellis.Core.Tests.Errors;

/// <summary>
/// Tests for the resource-error convenience factories on <see cref="Error.NotFound"/>,
/// <see cref="Error.Gone"/>, <see cref="Error.Conflict"/>, <see cref="Error.Forbidden"/>, and
/// <see cref="Error.InvariantViolation"/>, which mirror the existing
/// <see cref="Error.InvalidInput.ForField(string, string, string?)"/> /
/// <see cref="Error.InvalidInput.ForRule(string, string?)"/> style (trailing optional detail).
/// </summary>
public class ErrorFactoryTests
{
    private sealed class Team;

    // ── NotFound ───────────────────────────────────────────────────────────

    [Fact]
    public void NotFound_For_Generic_BuildsResourceAndDetail()
    {
        var error = Error.NotFound.For<Team>(42, "Team not found.");

        error.Resource.Type.Should().Be("Team");
        error.Resource.Id.Should().Be("42");
        error.Detail.Should().Be("Team not found.");
    }

    [Fact]
    public void NotFound_For_Generic_NoArguments_DefaultsNull()
    {
        var error = Error.NotFound.For<Team>();

        error.Resource.Type.Should().Be("Team");
        error.Resource.Id.Should().BeNull();
        error.Detail.Should().BeNull();
    }

    [Fact]
    public void NotFound_For_StringType_BuildsResource()
    {
        var error = Error.NotFound.For("Season", 7, "Season not found.");

        error.Resource.Type.Should().Be("Season");
        error.Resource.Id.Should().Be("7");
        error.Detail.Should().Be("Season not found.");
    }

    // ── Gone ───────────────────────────────────────────────────────────────

    [Fact]
    public void Gone_For_Generic_BuildsResourceAndDetail()
    {
        var error = Error.Gone.For<Team>(1, "gone");

        error.Resource.Type.Should().Be("Team");
        error.Resource.Id.Should().Be("1");
        error.Detail.Should().Be("gone");
    }

    [Fact]
    public void Gone_For_StringType_BuildsResource()
    {
        var error = Error.Gone.For("Season", 7);

        error.Resource.Type.Should().Be("Season");
        error.Resource.Id.Should().Be("7");
        error.Detail.Should().BeNull();
    }

    // ── Conflict ───────────────────────────────────────────────────────────

    [Fact]
    public void Conflict_For_Generic_BuildsResourceReasonDetail()
    {
        var error = Error.Conflict.For<Team>(5, "team.unresolved_penalties", "has penalties");

        error.Resource.Should().NotBeNull();
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.ReasonCode.Should().Be("team.unresolved_penalties");
        error.Code.Should().Be("team.unresolved_penalties");
        error.Detail.Should().Be("has penalties");
    }

    [Fact]
    public void Conflict_For_StringType_BuildsResourceReason()
    {
        var error = Error.Conflict.For("Team", 5, "x.y", "d");

        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.ReasonCode.Should().Be("x.y");
        error.Detail.Should().Be("d");
    }

    [Fact]
    public void Conflict_ForReason_ResourcelessConflict()
    {
        var error = Error.Conflict.ForReason("registration.pending_exists", "pending");

        error.Resource.Should().BeNull();
        error.ReasonCode.Should().Be("registration.pending_exists");
        error.Detail.Should().Be("pending");
    }

    // ── Forbidden ──────────────────────────────────────────────────────────

    [Fact]
    public void Forbidden_For_Generic_BuildsPolicyResourceDetail()
    {
        var error = Error.Forbidden.For<Team>("team.owner-only", 9, "owner only");

        error.PolicyId.Should().Be("team.owner-only");
        error.Code.Should().Be("team.owner-only");
        error.Resource.Should().NotBeNull();
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("9");
        error.Detail.Should().Be("owner only");
    }

    [Fact]
    public void Forbidden_For_Generic_NoIdNoDetail_DefaultsNull()
    {
        var error = Error.Forbidden.For<Team>("team.owner-only");

        error.PolicyId.Should().Be("team.owner-only");
        error.Resource!.Value.Id.Should().BeNull();
        error.Detail.Should().BeNull();
    }

    [Fact]
    public void Forbidden_ForPolicy_ResourcelessForbidden()
    {
        var error = Error.Forbidden.ForPolicy("team.owner-only", "denied");

        error.PolicyId.Should().Be("team.owner-only");
        error.Resource.Should().BeNull();
        error.Detail.Should().Be("denied");
    }

    // ── InvariantViolation ─────────────────────────────────────────────────

    [Fact]
    public void InvariantViolation_For_Generic_BuildsResourceReasonDetail()
    {
        var error = Error.InvariantViolation.For<Team>("team.roster_locked", 5, "roster is locked");

        error.Resource.Should().NotBeNull();
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.ReasonCode.Should().Be("team.roster_locked");
        error.Code.Should().Be("team.roster_locked");
        error.Detail.Should().Be("roster is locked");
    }

    [Fact]
    public void InvariantViolation_For_Generic_NoIdNoDetail_DefaultsNull()
    {
        var error = Error.InvariantViolation.For<Team>("team.roster_locked");

        error.ReasonCode.Should().Be("team.roster_locked");
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().BeNull();
        error.Detail.Should().BeNull();
    }

    [Fact]
    public void InvariantViolation_For_StringType_BuildsResourceReason()
    {
        var error = Error.InvariantViolation.For("Team", "x.y", 5, "d");

        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.ReasonCode.Should().Be("x.y");
        error.Detail.Should().Be("d");
    }

    [Fact]
    public void InvariantViolation_ForReason_ResourcelessViolation()
    {
        var error = Error.InvariantViolation.ForReason("order.must_have_items", "empty order");

        error.Resource.Should().BeNull();
        error.ReasonCode.Should().Be("order.must_have_items");
        error.Code.Should().Be("order.must_have_items");
        error.Detail.Should().Be("empty order");
    }
}
