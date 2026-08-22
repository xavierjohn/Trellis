namespace Trellis.Asp.Tests;

using System.Collections.Immutable;

/// <summary>
/// Tests for the single projection every pipeline shares.
///
/// The load-bearing case is name recovery: a query parameter's path is one RFC 6901-escaped
/// token, so <c>ForQuery("a/b")</c> stores <c>/a~1b</c> and must project back to the name
/// <c>a/b</c> — not to two path segments, and not to the raw escaped form.
/// </summary>
public class ViolationProjectionTests
{
    [Fact]
    public void A_body_pointer_projects_as_body_with_a_pointer()
    {
        var location = ViolationProjection.ToLocation(InputPointer.ForBody("/customer/email"));

        location.In.Should().Be("body");
        location.Pointer.Should().Be("/customer/email");
        location.Name.Should().BeNull();
    }

    [Fact]
    public void An_unlocated_pointer_projects_as_unknown_with_a_pointer()
    {
        var location = ViolationProjection.ToLocation(InputPointer.ForProperty("/email"));

        location.In.Should().Be("unknown");
        location.Pointer.Should().Be("/email");
        location.Name.Should().BeNull();
    }

    [Fact]
    public void The_root_pointer_projects_as_unknown_with_an_empty_pointer()
    {
        var location = ViolationProjection.ToLocation(InputPointer.Root);

        location.In.Should().Be("unknown");
        location.Pointer.Should().Be("");
    }

    [Fact]
    public void The_body_root_pointer_projects_as_body_with_an_empty_pointer()
    {
        var location = ViolationProjection.ToLocation(InputPointer.ForBody(""));

        location.In.Should().Be("body");
        location.Pointer.Should().Be("");
    }

    [Theory]
    [InlineData("page", "page")]
    [InlineData("a/b", "a/b")]
    [InlineData("a~b", "a~b")]
    [InlineData("/id", "/id")]
    [InlineData("a~1b", "a~1b")]
    public void A_query_name_round_trips_through_the_projection(string name, string expected)
    {
        var location = ViolationProjection.ToLocation(InputPointer.ForQuery(name));

        location.In.Should().Be("query");
        location.Name.Should().Be(expected);
        location.Pointer.Should().BeNull();
    }

    [Fact]
    public void Path_and_header_names_round_trip_too()
    {
        ViolationProjection.ToLocation(InputPointer.ForPath("id")).Name.Should().Be("id");
        ViolationProjection.ToLocation(InputPointer.ForPath("id")).In.Should().Be("path");
        ViolationProjection.ToLocation(InputPointer.ForHeader("If-Match")).Name.Should().Be("If-Match");
        ViolationProjection.ToLocation(InputPointer.ForHeader("If-Match")).In.Should().Be("header");
    }

    [Fact]
    public void Field_violations_project_in_order_with_their_locations()
    {
        var fields = new EquatableArray<FieldViolation>(
        [
            new FieldViolation(InputPointer.ForBody("/email"), ValidationCodes.Unspecified) { Detail = "bad email" },
            new FieldViolation(InputPointer.ForQuery("page"), ValidationCodes.Unspecified) { Detail = "bad page" },
        ]);

        var projected = ViolationProjection.ToFieldViolations(fields);

        projected.Should().SatisfyRespectively(
            first =>
            {
                first.Location.In.Should().Be("body");
                first.Location.Pointer.Should().Be("/email");
                first.Detail.Should().Be("bad email");
            },
            second =>
            {
                second.Location.In.Should().Be("query");
                second.Location.Name.Should().Be("page");
                second.Detail.Should().Be("bad page");
            });
    }

    [Fact]
    public void Rule_violations_project_every_location()
    {
        var rules = new EquatableArray<RuleViolation>(
        [
            new RuleViolation(ValidationCodes.Unspecified)
            {
                Detail = "End date must follow start date.",
                Fields = ImmutableArray.Create(
                    InputPointer.ForBody("/startDate"),
                    InputPointer.ForBody("/endDate")),
            },
        ]);

        var projected = ViolationProjection.ToRuleViolations(rules);

        var rule = projected.Should().ContainSingle().Subject;
        rule.Locations.Should().HaveCount(2);
        rule.Locations.Select(l => l.Pointer).Should().Equal("/startDate", "/endDate");
    }

    [Fact]
    public void A_rule_with_no_pointers_projects_an_empty_locations_list()
    {
        var rules = new EquatableArray<RuleViolation>(
            [new RuleViolation(ValidationCodes.Unspecified) { Detail = "form-level" }]);

        var projected = ViolationProjection.ToRuleViolations(rules);

        projected.Should().ContainSingle().Subject.Locations.Should().BeEmpty();
    }

    [Fact]
    public void An_application_supplied_code_is_projected_verbatim()
    {
        var fields = new EquatableArray<FieldViolation>(
            [new FieldViolation(InputPointer.ForBody("/email"), "validation.error")]);

        var projected = ViolationProjection.ToFieldViolations(fields);

        projected.Should().ContainSingle().Subject.Code.Should().Be("validation.error");
    }

    [Fact]
    public void A_non_legacy_code_is_left_untouched()
    {
        var fields = new EquatableArray<FieldViolation>(
            [new FieldViolation(InputPointer.ForBody("/email"), "string.email")]);

        var projected = ViolationProjection.ToFieldViolations(fields);

        projected.Should().ContainSingle().Subject.Code.Should().Be("string.email");
    }
}
