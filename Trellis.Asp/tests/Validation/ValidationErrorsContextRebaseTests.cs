namespace Trellis.Asp.Tests.Validation;

using System.Collections.Immutable;

/// <summary>
/// Guard tests for the single body-context rebase rule and the <c>AddBodyError</c> overloads.
///
/// There is exactly one rebase rule, and it must be applied identically everywhere it
/// appears:
///
/// | incoming <c>In</c>          | ancestor prefix | resulting <c>In</c> |
/// |-----------------------------|-----------------|---------------------|
/// | <c>Unspecified</c>          | applied         | <c>Body</c> (promotion — <c>AddBodyError</c> only) |
/// | <c>Body</c>                 | applied         | <c>Body</c>         |
/// | <c>Query</c>/<c>Path</c>/<c>Header</c> | **not applied** | unchanged |
///
/// The exemption is not a nicety: under the single-token name encoding,
/// <c>ForQuery("a/b")</c> stores <c>/a~1b</c>, so prefixing a body ancestor would corrupt
/// the very location the rule exists to preserve.
/// </summary>
public class ValidationErrorsContextRebaseTests
{
    private const string UnspecifiedCode = ValidationCodes.Unspecified;

    // --- AddBodyError promotes Unspecified to Body, at every depth ---

    [Fact]
    public void AddBodyError_promotes_an_unspecified_field_violation_to_body_at_depth_0()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField("displayName", UnspecifiedCode, "too short"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        error.Should().NotBeNull();
        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/displayName");
        violation.Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void AddBodyError_promotes_and_prefixes_at_depth_1()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField("displayName", UnspecifiedCode, "too short"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/customer/displayName");
        violation.Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void AddBodyError_string_overload_stamps_body()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        ValidationErrorsContext.AddBodyError("displayName", "too short");

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/displayName");
        violation.Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void AddBodyError_code_overload_carries_code_and_args()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        ValidationErrorsContext.AddBodyError(
            "displayName",
            UnspecifiedCode,
            "too short",
            new Dictionary<string, string> { ["min"] = "3" });

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.In.Should().Be(InputLocation.Body);
        violation.ReasonCode.Should().Be(UnspecifiedCode);
        violation.Detail.Should().Be("too short");
        violation.Args.Should().Contain(new KeyValuePair<string, string>("min", "3"));
    }

    // --- explicit non-body locations survive unprefixed and unpromoted ---

    [Fact]
    public void AddBodyError_leaves_an_explicit_query_location_unprefixed_and_unpromoted()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField(InputPointer.ForQuery("page"), UnspecifiedCode, "out of range"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/page");
        violation.Field.In.Should().Be(InputLocation.Query);
    }

    /// <summary>
    /// The corruption case, pinned directly: a query parameter named <c>a/b</c> is one
    /// token. Prefixing a body ancestor onto it would make the name unrecoverable.
    /// </summary>
    [Fact]
    public void A_slash_bearing_query_name_survives_an_ancestor_rebase_intact()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField(InputPointer.ForQuery("a/b"), UnspecifiedCode, "bad"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/a~1b");
        violation.Field.In.Should().Be(InputLocation.Query);
    }

    [Fact]
    public void AddBodyError_leaves_explicit_path_and_header_locations_untouched()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField(InputPointer.ForPath("id"), UnspecifiedCode, "bad"));
        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField(InputPointer.ForHeader("If-Match"), UnspecifiedCode, "bad"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        error!.Fields.Items.Should().SatisfyRespectively(
            first =>
            {
                first.Field.Path.Should().Be("/id");
                first.Field.In.Should().Be(InputLocation.Path);
            },
            second =>
            {
                second.Field.Path.Should().Be("/If-Match");
                second.Field.In.Should().Be(InputLocation.Header);
            });
    }

    [Fact]
    public void AddBodyError_prefixes_an_explicit_body_location_and_keeps_it_body()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        ValidationErrorsContext.AddBodyError(
            Error.InvalidInput.ForField(InputPointer.ForBody("street"), UnspecifiedCode, "bad"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/customer/street");
        violation.Field.In.Should().Be(InputLocation.Body);
    }

    // --- rule violations follow the same rule ---

    [Fact]
    public void Rule_violation_pointers_follow_the_same_rebase_rule()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        var rule = new RuleViolation(UnspecifiedCode)
        {
            Fields = ImmutableArray.Create(InputPointer.ForProperty("start"), InputPointer.ForQuery("page")),
        };

        ValidationErrorsContext.AddBodyError(
            new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Rules = ImmutableArray.Create(rule) });

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var projected = error!.Rules.Items.Should().ContainSingle().Subject;
        projected.Fields.Items.Should().SatisfyRespectively(
            body =>
            {
                body.Path.Should().Be("/customer/start");
                body.In.Should().Be(InputLocation.Body);
            },
            query =>
            {
                query.Path.Should().Be("/page");
                query.In.Should().Be(InputLocation.Query);
            });
    }

    // --- AddError preserves In but never promotes ---

    /// <summary>
    /// <c>AddError</c> is public and callable from an application filter that may well be
    /// reporting a query parameter, so it must not assert body origin. Only
    /// <c>AddBodyError</c>, which says so in its name, promotes.
    /// </summary>
    [Fact]
    public void AddError_does_not_promote_an_unspecified_location()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        ValidationErrorsContext.AddError(
            Error.InvalidInput.ForField("page", UnspecifiedCode, "bad"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.In.Should().Be(InputLocation.Unspecified);
    }

    [Fact]
    public void AddError_still_exempts_an_explicit_query_location_from_prefixing()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var segment = ValidationErrorsContext.PushPathSegment("customer");

        ValidationErrorsContext.AddError(
            Error.InvalidInput.ForField(InputPointer.ForQuery("page"), UnspecifiedCode, "bad"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        var violation = error!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/page");
        violation.Field.In.Should().Be(InputLocation.Query);
    }

    // --- de-duplication still works once In is part of identity ---

    /// <summary>
    /// The reason the equality change and this rebase fix must ship together: if a rebase
    /// dropped <c>In</c>, the same violation seen at depth 0 and depth 1 would no longer be
    /// equal and the failure would be emitted twice.
    /// </summary>
    [Fact]
    public void Identical_body_violations_are_de_duplicated()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        ValidationErrorsContext.AddBodyError("displayName", "too short");
        ValidationErrorsContext.AddBodyError("displayName", "too short");

        var error = ValidationErrorsContext.GetUnprocessableContent();

        error!.Fields.Items.Should().ContainSingle();
    }

    [Fact]
    public void Violations_differing_only_by_location_are_not_de_duplicated()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        ValidationErrorsContext.AddError(
            Error.InvalidInput.ForField(InputPointer.ForQuery("page"), UnspecifiedCode, "bad"));
        ValidationErrorsContext.AddError(
            Error.InvalidInput.ForField(InputPointer.ForPath("page"), UnspecifiedCode, "bad"));

        var error = ValidationErrorsContext.GetUnprocessableContent();

        error!.Fields.Items.Should().HaveCount(2);
    }
}
