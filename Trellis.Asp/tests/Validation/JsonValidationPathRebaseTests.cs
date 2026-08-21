namespace Trellis.Asp.Tests.Validation;

using System;
using System.Collections.Immutable;
using Trellis.Asp.Validation;

/// <summary>
/// Guard tests for §8.2(c) — the "prefix exactly once, marked explicitly, never inferred" rule.
///
/// <para>
/// <c>CompositeValueObjectJsonConverter</c> lives in <c>Trellis.Primitives</c>, which references only
/// <c>Trellis.Core</c>. It therefore cannot see <see cref="ValidationErrorsContext"/> and emits
/// <em>composite-relative</em> pointers with no knowledge of its position in the document. Re-rooting
/// is the ASP layer's job, and it must happen exactly once no matter how deeply wrappers nest.
/// </para>
///
/// <para>
/// The discriminator is an explicit marker in <see cref="Exception.Data"/>, <em>not</em> a
/// string-prefix heuristic. A legitimate relative path can coincidentally share a leading segment
/// sequence with its own ancestor — a composite value object with a property literally named
/// <c>members</c>, nested under a collection named <c>members</c> — so "skip if it already starts
/// with the ancestor" silently drops a real prefix.
/// </para>
/// </summary>
public class JsonValidationPathRebaseTests
{
    private const string UnspecifiedCode = ValidationCodes.Unspecified;

    private static TrellisJsonValidationException CompositeRelativeFailure() =>
        new("Postal code is not valid for the country.")
        {
            InvalidInput = new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("postalCode"), UnspecifiedCode)
                {
                    Detail = "Postal code is not valid for the country.",
                })),
        };

    [Fact]
    public void Rebase_prefixes_the_live_ancestor_and_promotes_to_body()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var members = ValidationErrorsContext.PushPathSegment("members");
        using var index = ValidationErrorsContext.PushPathSegment("0");
        using var address = ValidationErrorsContext.PushPathSegment("address");

        var rebased = JsonValidationPathRebase.Rebase(CompositeRelativeFailure());

        var violation = rebased.InvalidInput!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/members/0/address/postalCode");
        violation.Field.In.Should().Be(InputLocation.Body);
    }

    [Fact]
    public void Rebase_marks_the_result_so_an_outer_wrapper_leaves_it_alone()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var address = ValidationErrorsContext.PushPathSegment("address");

        var rebased = JsonValidationPathRebase.Rebase(CompositeRelativeFailure());

        JsonValidationPathRebase.IsMarked(rebased).Should().BeTrue();
    }

    [Fact]
    public void An_unmarked_exception_is_not_reported_as_marked() =>
        JsonValidationPathRebase.IsMarked(CompositeRelativeFailure()).Should().BeFalse();

    /// <summary>
    /// The whole point of the marker. <c>AncestorPointer()</c> returns the <em>entire</em> live stack,
    /// not the segment owned by the current wrapper, and wrappers nest — so prefixing at every level
    /// yields <c>/members/0/members/0/address/postalCode</c>.
    /// </summary>
    [Fact]
    public void Rebasing_an_already_marked_exception_is_a_no_op()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var members = ValidationErrorsContext.PushPathSegment("members");
        using var index = ValidationErrorsContext.PushPathSegment("0");

        var once = JsonValidationPathRebase.Rebase(CompositeRelativeFailure());
        var twice = JsonValidationPathRebase.RebaseIfUnmarked(once);

        twice.Should().BeSameAs(once);
        twice.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/members/0/postalCode");
    }

    /// <summary>
    /// The case a string-prefix heuristic gets wrong: the relative leaf legitimately begins with the
    /// same segment as the ancestor, so "already starts with the ancestor" would refuse to prefix it
    /// and the violation would point at the wrong node.
    /// </summary>
    [Fact]
    public void A_relative_path_that_coincides_with_its_ancestor_is_still_prefixed()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var members = ValidationErrorsContext.PushPathSegment("members");

        var failure = new TrellisJsonValidationException("Nested members are not valid.")
        {
            InvalidInput = new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("members"), UnspecifiedCode))),
        };

        var rebased = JsonValidationPathRebase.Rebase(failure);

        rebased.InvalidInput!.Fields.Items.Should().ContainSingle()
            .Which.Field.Path.Should().Be("/members/members");
    }

    [Fact]
    public void Rebase_at_depth_0_promotes_without_prefixing()
    {
        using var scope = ValidationErrorsContext.BeginScope();

        var rebased = JsonValidationPathRebase.Rebase(CompositeRelativeFailure());

        var violation = rebased.InvalidInput!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/postalCode");
        violation.Field.In.Should().Be(InputLocation.Body);
    }

    /// <summary>
    /// An explicitly located violation is exempt from prefixing — see the rebase rule. A composite
    /// converter has no reason to emit one today, but the rule is shared and must not fork.
    /// </summary>
    [Fact]
    public void An_explicitly_located_violation_is_neither_prefixed_nor_promoted()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var address = ValidationErrorsContext.PushPathSegment("address");

        var failure = new TrellisJsonValidationException("Bad query parameter.")
        {
            InvalidInput = new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForQuery("page"), UnspecifiedCode))),
        };

        var rebased = JsonValidationPathRebase.Rebase(failure);

        var violation = rebased.InvalidInput!.Fields.Items.Should().ContainSingle().Subject;
        violation.Field.Path.Should().Be("/page");
        violation.Field.In.Should().Be(InputLocation.Query);
    }

    [Fact]
    public void Rule_violation_locations_are_rebased_too()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var address = ValidationErrorsContext.PushPathSegment("address");

        var failure = new TrellisJsonValidationException("Provide exactly one of them.")
        {
            InvalidInput = new Error.InvalidInput(
                EquatableArray<FieldViolation>.Empty,
                EquatableArray.Create(new RuleViolation(
                    UnspecifiedCode,
                    ImmutableArray.Create(
                        InputPointer.ForProperty("postalCode"),
                        InputPointer.ForProperty("region"))))),
        };

        var rebased = JsonValidationPathRebase.Rebase(failure);

        var rule = rebased.InvalidInput!.Rules.Items.Should().ContainSingle().Subject;
        rule.Fields.Items.Should().SatisfyRespectively(
            first => first.Path.Should().Be("/address/postalCode"),
            second => second.Path.Should().Be("/address/region"));
        rule.Fields.Items.Should().OnlyContain(p => p.In == InputLocation.Body);
    }

    /// <summary>
    /// An unstructured throw carries no pointers to re-root, but it is still rewrapped so the live
    /// ancestor is recorded. Passing it through would have preserved
    /// <see cref="System.Text.Json.JsonException.Path"/> — except that once a path-tracking wrapper is
    /// installed that property is no longer the document position: the wrapper deserializes through a
    /// nested <c>JsonSerializer</c> call, so the path is stamped relative to that nested root and the
    /// outer frames leave the now-non-null value alone. The recorded ancestor is what arm 3 consumes.
    /// </summary>
    [Fact]
    public void An_exception_without_structured_input_records_the_ancestor_for_the_unstructured_arm()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var address = ValidationErrorsContext.PushPathSegment("address");

        var failure = new TrellisJsonValidationException("Required property 'postalCode' is missing.");

        var rebased = JsonValidationPathRebase.RebaseIfUnmarked(failure);

        rebased.Should().NotBeSameAs(failure);
        rebased.InvalidInput.Should().BeNull("there were no pointers to re-root");
        rebased.Message.Should().Be(failure.Message, "the curated message is the whole payload here");
        rebased.InnerException.Should().BeSameAs(failure);
        JsonValidationPathRebase.RecordedAbsolutePath(rebased).Should().Be("/address");
    }

    [Fact]
    public void The_original_exception_is_chained_as_inner_so_nothing_is_lost()
    {
        using var scope = ValidationErrorsContext.BeginScope();
        using var address = ValidationErrorsContext.PushPathSegment("address");

        var original = CompositeRelativeFailure();
        var rebased = JsonValidationPathRebase.Rebase(original);

        rebased.Should().NotBeSameAs(original);
        rebased.InnerException.Should().BeSameAs(original);
        rebased.Message.Should().Be(original.Message);
    }
}
