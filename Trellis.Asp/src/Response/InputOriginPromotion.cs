namespace Trellis.Asp;

using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Trellis;
using Trellis.Asp.Validation;

/// <summary>
/// Fills in the location an endpoint has declared for violations that reached it without one.
/// </summary>
/// <remarks>
/// Applied once, at the response boundary, so every downstream projection — the field
/// violations, the rule violations, the MVC-shaped <c>errors</c> map, and each aggregate child —
/// sees the same located pointer. Doing it per projection is how four spellings of one rule get
/// created.
/// </remarks>
internal static class InputOriginPromotion
{
    /// <summary>
    /// Promotes unlocated pointers to the location the endpoint declared via
    /// <see cref="InputOriginAttribute"/>, and returns <paramref name="error"/> unchanged when the
    /// endpoint declared nothing or opted out.
    /// </summary>
    public static Error Apply(HttpContext httpContext, Error error)
    {
        var declared = httpContext.GetEndpoint()?.Metadata.GetMetadata<InputOriginAttribute>()?.Location;

        return declared is null or InputLocation.Unspecified ? error : Promote(error, declared.Value);
    }

    private static Error Promote(Error error, InputLocation declared) => error switch
    {
        Error.InvalidInput invalid => Promote(invalid, declared),
        Error.Aggregate aggregate => PromoteChildren(aggregate, declared),
        _ => error,
    };

    private static Error.Aggregate PromoteChildren(Error.Aggregate aggregate, InputLocation declared)
    {
        var children = aggregate.Errors.Items;
        Error[]? promoted = null;

        for (var i = 0; i < children.Length; i++)
        {
            var child = Promote(children[i], declared);
            if (ReferenceEquals(child, children[i])) continue;

            promoted ??= [.. children];
            promoted[i] = child;
        }

        return promoted is null
            ? aggregate
            : new Error.Aggregate(EquatableArray.Create(promoted))
            {
                Detail = aggregate.Detail,
                Cause = aggregate.Cause,
            };
    }

    private static Error.InvalidInput Promote(Error.InvalidInput invalid, InputLocation declared)
    {
        var promotedFields = PromoteFields(invalid.Fields.Items, declared);
        var promotedRules = PromoteRules(invalid.Rules.Items, declared);

        if (promotedFields is null && promotedRules is null)
            return invalid;

        return invalid with
        {
            Fields = promotedFields is null ? invalid.Fields : EquatableArray.Create(promotedFields),
            Rules = promotedRules is null ? invalid.Rules : EquatableArray.Create(promotedRules),
        };
    }

    private static FieldViolation[]? PromoteFields(ImmutableArray<FieldViolation> fields, InputLocation declared)
    {
        FieldViolation[]? promoted = null;

        for (var i = 0; i < fields.Length; i++)
        {
            var located = Locate(fields[i].Field, declared);
            if (located == fields[i].Field) continue;

            promoted ??= [.. fields];
            promoted[i] = fields[i] with { Field = located };
        }

        return promoted;
    }

    private static RuleViolation[]? PromoteRules(ImmutableArray<RuleViolation> rules, InputLocation declared)
    {
        RuleViolation[]? promoted = null;

        for (var i = 0; i < rules.Length; i++)
        {
            var pointers = rules[i].Fields.Items;
            InputPointer[]? promotedPointers = null;

            for (var j = 0; j < pointers.Length; j++)
            {
                var located = Locate(pointers[j], declared);
                if (located == pointers[j]) continue;

                promotedPointers ??= [.. pointers];
                promotedPointers[j] = located;
            }

            if (promotedPointers is null) continue;

            promoted ??= [.. rules];
            promoted[i] = rules[i] with { Fields = EquatableArray.Create(promotedPointers) };
        }

        return promoted;
    }

    /// <summary>
    /// Stamps an unlocated pointer with the declared location, leaving an already-located pointer
    /// untouched.
    /// </summary>
    /// <remarks>
    /// A body declaration reuses the framework's single body-context rebase rule. A query
    /// declaration applies only to a pointer that names one top-level member, because a nested
    /// pointer such as <c>/lines/0/amount</c> addresses a document that no query string can carry;
    /// promoting it would emit a shape the model binder never produces, so it is left unlocated.
    /// </remarks>
    private static InputPointer Locate(InputPointer pointer, InputLocation declared) => declared switch
    {
        InputLocation.Body => ValidationErrorsContext.RebaseToBody(pointer, string.Empty),
        InputLocation.Query when pointer.In is InputLocation.Unspecified && NamesOneMember(pointer) =>
            pointer with { In = InputLocation.Query },
        _ => pointer,
    };

    private static bool NamesOneMember(InputPointer pointer) =>
        pointer.Path.Length > 1 && pointer.Path[0] == '/' && pointer.Path.IndexOf('/', 1) < 0;
}
