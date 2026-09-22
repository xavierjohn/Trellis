namespace Trellis.Core.Tests.Errors;

using System.Text.Json;

/// <summary>
/// Tests for the resource-error convenience factories on <see cref="Error.NotFound"/>,
/// <see cref="Error.Gone"/>, <see cref="Error.Conflict"/>, <see cref="Error.Forbidden"/>, and
/// <see cref="Error.InvariantViolation"/>, which mirror the existing
/// <c>Error.InvalidInput.ForField</c> / <c>Error.InvalidInput.ForRule</c> style.
/// </summary>
public class ErrorFactoryTests
{
    private sealed class Team;

    // ── NotFound ───────────────────────────────────────────────────────────

    [Fact]
    public void NotFound_For_Generic_BuildsResourceAndDetail()
    {
        var error = Error.NotFound.For<Team>(id: 42, detail: "Team not found.");

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
        var error = Error.NotFound.For(resource: global::Trellis.ResourceRef.For("Season", 7), detail: "Season not found.");

        error.Resource.Type.Should().Be("Season");
        error.Resource.Id.Should().Be("7");
        error.Detail.Should().Be("Season not found.");
    }

    // ── Reason codes on the resource-only cases ────────────────────────────
    //
    // NotFound and Gone are the two cases whose Code is optional. These tests pin that the code
    // reaches Code when supplied, and the inherited sentinel survives when it is not.

    [Fact]
    public void NotFound_For_Generic_WithCode_SetsCode()
    {
        var error = Error.NotFound.For<Team>(id: 42, detail: "Team not found.", code: "team.archived");

        error.Code.Should().Be("team.archived");
        error.Detail.Should().Be("Team not found.");
    }

    [Fact]
    public void NotFound_For_StringType_WithCode_SetsCode()
    {
        var error = Error.NotFound.For(resource: global::Trellis.ResourceRef.For("Season", 7), code: "season.archived");

        error.Code.Should().Be("season.archived");
        error.Detail.Should().BeNull();
    }

    [Fact]
    public void Gone_For_Generic_WithCode_SetsCode()
    {
        var error = Error.Gone.For<Team>(id: 1, detail: "gone", code: "team.purged");

        error.Code.Should().Be("team.purged");
        error.Detail.Should().Be("gone");
    }

    [Fact]
    public void Gone_For_StringType_WithCode_SetsCode()
    {
        var error = Error.Gone.For(resource: global::Trellis.ResourceRef.For("Season", 7), code: "season.purged");

        error.Code.Should().Be("season.purged");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotFound_For_WithoutCode_KeepsUnspecifiedSentinel(string? code)
    {
        // An omitted code, an explicitly-empty one, and a whitespace-only one must all land the
        // same way. A blank code names nothing, and assigning it would put a meaningless string
        // on the wire where consumers branch on ValidationCodes.Unspecified — matching the
        // generator's own rule for optional runtime codes (RequiredPartialClassGenerator).
        Error.NotFound.For<Team>(id: 42, code: code).Code.Should().Be(ValidationCodes.Unspecified);
        Error.Gone.For<Team>(id: 42, code: code).Code.Should().Be(ValidationCodes.Unspecified);
        Error.NotFound.For(resource: global::Trellis.ResourceRef.For("Season", 7), code: code).Code.Should().Be(ValidationCodes.Unspecified);
        Error.Gone.For(resource: global::Trellis.ResourceRef.For("Season", 7), code: code).Code.Should().Be(ValidationCodes.Unspecified);
    }

    // ── Gone ───────────────────────────────────────────────────────────────
    [Fact]
    public void Gone_For_Generic_BuildsResourceAndDetail()
    {
        var error = Error.Gone.For<Team>(id: 1, detail: "gone");

        error.Resource.Type.Should().Be("Team");
        error.Resource.Id.Should().Be("1");
        error.Detail.Should().Be("gone");
    }

    [Fact]
    public void Gone_For_StringType_BuildsResource()
    {
        var error = Error.Gone.For(resource: global::Trellis.ResourceRef.For("Season", 7));

        error.Resource.Type.Should().Be("Season");
        error.Resource.Id.Should().Be("7");
        error.Detail.Should().BeNull();
    }

    // ── Conflict ───────────────────────────────────────────────────────────

    [Fact]
    public void Conflict_For_Generic_BuildsResourceReasonDetail()
    {
        var error = Error.Conflict.For<Team>(id: 5, code: "team.unresolved_penalties", detail: "has penalties");

        error.Resource.Should().NotBeNull();
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.Code.Should().Be("team.unresolved_penalties");
        error.Code.Should().Be("team.unresolved_penalties");
        error.Detail.Should().Be("has penalties");
    }

    [Fact]
    public void Conflict_For_StringType_BuildsResourceReason()
    {
        var error = Error.Conflict.For(resource: global::Trellis.ResourceRef.For("Team", 5), code: "x.y", detail: "d");

        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.Code.Should().Be("x.y");
        error.Detail.Should().Be("d");
    }

    [Fact]
    public void Conflict_ForReason_ResourcelessConflict()
    {
        var error = Error.Conflict.ForReason(code: "registration.pending_exists", detail: "pending");

        error.Resource.Should().BeNull();
        error.Code.Should().Be("registration.pending_exists");
        error.Detail.Should().Be("pending");
    }

    // ── Forbidden ──────────────────────────────────────────────────────────

    [Fact]
    public void Forbidden_For_Generic_BuildsPolicyResourceDetail()
    {
        var error = Error.Forbidden.For<Team>(code: "team.owner-only", id: 9, detail: "owner only");

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
        var error = Error.Forbidden.For<Team>(code: "team.owner-only");

        error.PolicyId.Should().Be("team.owner-only");
        error.Resource!.Value.Id.Should().BeNull();
        error.Detail.Should().BeNull();
    }

    [Fact]
    public void Forbidden_ForPolicy_ResourcelessForbidden()
    {
        var error = Error.Forbidden.ForPolicy(code: "team.owner-only", detail: "denied");

        error.PolicyId.Should().Be("team.owner-only");
        error.Resource.Should().BeNull();
        error.Detail.Should().Be("denied");
    }

    // ── InvariantViolation ─────────────────────────────────────────────────

    [Fact]
    public void InvariantViolation_For_Generic_BuildsResourceReasonDetail()
    {
        var error = Error.InvariantViolation.For<Team>(code: "team.roster_locked", id: 5, detail: "roster is locked");

        error.Resource.Should().NotBeNull();
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.Code.Should().Be("team.roster_locked");
        error.Code.Should().Be("team.roster_locked");
        error.Detail.Should().Be("roster is locked");
    }

    [Fact]
    public void InvariantViolation_For_Generic_NoIdNoDetail_DefaultsNull()
    {
        var error = Error.InvariantViolation.For<Team>(code: "team.roster_locked");

        error.Code.Should().Be("team.roster_locked");
        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().BeNull();
        error.Detail.Should().BeNull();
    }

    [Fact]
    public void InvariantViolation_For_StringType_BuildsResourceReason()
    {
        var error = Error.InvariantViolation.For(resource: global::Trellis.ResourceRef.For("Team", 5), code: "x.y", detail: "d");

        error.Resource!.Value.Type.Should().Be("Team");
        error.Resource.Value.Id.Should().Be("5");
        error.Code.Should().Be("x.y");
        error.Detail.Should().Be("d");
    }

    [Fact]
    public void InvariantViolation_ForReason_ResourcelessViolation()
    {
        var error = Error.InvariantViolation.ForReason(code: "order.must_have_items", detail: "empty order");

        error.Resource.Should().BeNull();
        error.Code.Should().Be("order.must_have_items");
        error.Code.Should().Be("order.must_have_items");
        error.Detail.Should().Be("empty order");
    }

    [Fact]
    public void For_CodeFirst_StringIds_PreservesEveryPayloadAndSerialization()
    {
        var resource = ResourceRef.For<Team>("team-42");
        (Error Actual, Error Expected)[] cases =
        [
            (Error.NotFound.For<Team>("team.missing", "team-42", "missing"),
                new Error.NotFound(resource) { Code = "team.missing", Detail = "missing" }),
            (Error.Gone.For<Team>("team.removed", "team-42", "removed"),
                new Error.Gone(resource) { Code = "team.removed", Detail = "removed" }),
            (Error.Conflict.For<Team>("team.locked", "team-42", "locked"),
                new Error.Conflict("team.locked", resource) { Detail = "locked" }),
            (Error.InvariantViolation.For<Team>("team.empty", "team-42", "empty"),
                new Error.InvariantViolation("team.empty", resource) { Detail = "empty" }),
            (Error.Forbidden.For<Team>("teams.manage", "team-42", "denied"),
                new Error.Forbidden("teams.manage", resource) { Detail = "denied" }),
        ];

        Assert.All(cases, pair =>
        {
            pair.Actual.Equals(pair.Expected).Should().BeTrue();
            JsonSerializer.Serialize(pair.Actual, pair.Actual.GetType())
                .Should().Be(JsonSerializer.Serialize(pair.Expected, pair.Expected.GetType()));
        });
    }

    [Fact]
    public void For_ExplicitResource_MatchesGenericFactory()
    {
        var resource = ResourceRef.For<Team>("team-42");
        (Error Actual, Error Expected)[] cases =
        [
            (Error.NotFound.For("team.missing", resource, "missing"),
                Error.NotFound.For<Team>("team.missing", "team-42", "missing")),
            (Error.Gone.For("team.removed", resource, "removed"),
                Error.Gone.For<Team>("team.removed", "team-42", "removed")),
            (Error.Conflict.For("team.locked", resource, "locked"),
                Error.Conflict.For<Team>("team.locked", "team-42", "locked")),
            (Error.InvariantViolation.For("team.empty", resource, "empty"),
                Error.InvariantViolation.For<Team>("team.empty", "team-42", "empty")),
            (Error.Forbidden.For("teams.manage", resource, "denied"),
                Error.Forbidden.For<Team>("teams.manage", "team-42", "denied")),
            (Error.NotFound.For(resource), Error.NotFound.For<Team>(id: "team-42")),
            (Error.Gone.For(resource), Error.Gone.For<Team>(id: "team-42")),
        ];

        Assert.All(cases, pair => pair.Actual.Equals(pair.Expected).Should().BeTrue());
    }

    [Fact]
    public void For_ExplicitResource_Default_Throws() =>
        Assert.All<Action>(
        [
            () => Error.NotFound.For(default(ResourceRef)),
            () => Error.Gone.For(default(ResourceRef)),
            () => Error.Conflict.For("team.locked", default(ResourceRef)),
            () => Error.InvariantViolation.For("team.empty", default(ResourceRef)),
            () => Error.Forbidden.For("teams.manage", default(ResourceRef)),
        ], create => create.Should().Throw<ArgumentException>());

    [Fact]
    public void Factories_WithCode_DeclareCodeFirst()
    {
        var factories = typeof(Error).GetNestedTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(method => method.Name is "For" or "ForField" or "ForRule" or "ForReason" or "ForPolicy")
            .ToArray();

        factories.Should().NotBeEmpty();
        Assert.All(factories, method =>
        {
            var parameters = method.GetParameters();
            if (method.Name == "For" && parameters[0].ParameterType == typeof(ResourceRef))
            {
                (method.DeclaringType == typeof(Error.NotFound) || method.DeclaringType == typeof(Error.Gone))
                    .Should().BeTrue();
                return;
            }

            parameters[0].Name.Should().Be("code");
        });
    }
}