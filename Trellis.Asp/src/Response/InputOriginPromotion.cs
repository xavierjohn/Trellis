namespace Trellis.Asp;

using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
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
    /// Locates pointers that reached the response boundary without a location, using what the
    /// endpoint is known to bind and, where that does not reach, what it declared via
    /// <see cref="InputOriginAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Only <see cref="Error.InvalidInput"/> and <see cref="Error.Aggregate"/> can carry a pointer,
    /// so anything else leaves before the endpoint's binding map is consulted — discovering it
    /// walks every API description the first time an endpoint is seen, which a 404 or a 409 should
    /// not pay for.
    /// </remarks>
    public static Error Apply(HttpContext httpContext, Error error)
    {
        if (error is not (Error.InvalidInput or Error.Aggregate)) return error;

        var endpoint = httpContext.GetEndpoint();
        if (endpoint is null) return error;

        var binding = EndpointBinding.For(httpContext, endpoint);
        var declared = endpoint.Metadata.GetMetadata<InputOriginAttribute>()?.Location;
        var residual = declared ?? (binding.BindsBody ? InputLocation.Body : InputLocation.Unspecified);

        if (residual is InputLocation.Unspecified
            && binding.QueryParameters.Count == 0
            && binding.HeaderParameters.Count == 0
            && (endpoint as RouteEndpoint)?.RoutePattern.Parameters.Count is null or 0)
            return error;

        var declaration = new Declaration(
            residual,
            (endpoint as RouteEndpoint)?.RoutePattern.Parameters,
            binding.QueryParameters,
            binding.HeaderParameters);

        return Promote(error, declaration);
    }

    /// <summary>
    /// The residual location, plus the parameters the endpoint is known to bind from the URL.
    /// </summary>
    /// <remarks>
    /// The route parameters come from the endpoint's own pattern and the query parameters from
    /// the API description built for it. Both are evidence about where a value arrived, so both
    /// outrank the residual, which only covers what the URL does not account for.
    /// </remarks>
    private readonly record struct Declaration(
        InputLocation Location,
        IReadOnlyList<RoutePatternParameterPart>? RouteParameters,
        IReadOnlyList<string> QueryParameters,
        IReadOnlyList<string> HeaderParameters);

    private static Error Promote(Error error, in Declaration declaration) => error switch
    {
        Error.InvalidInput invalid => Promote(invalid, declaration),
        Error.Aggregate aggregate => PromoteChildren(aggregate, declaration),
        _ => error,
    };

    private static Error.Aggregate PromoteChildren(Error.Aggregate aggregate, in Declaration declaration)
    {
        var children = aggregate.Errors.Items;
        Error[]? promoted = null;

        for (var i = 0; i < children.Length; i++)
        {
            var child = Promote(children[i], declaration);
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

    private static Error.InvalidInput Promote(Error.InvalidInput invalid, in Declaration declaration)
    {
        var promotedFields = PromoteFields(invalid.Fields.Items, declaration);
        var promotedRules = PromoteRules(invalid.Rules.Items, declaration);

        if (promotedFields is null && promotedRules is null)
            return invalid;

        return invalid with
        {
            Fields = promotedFields is null ? invalid.Fields : EquatableArray.Create(promotedFields),
            Rules = promotedRules is null ? invalid.Rules : EquatableArray.Create(promotedRules),
        };
    }

    private static FieldViolation[]? PromoteFields(ImmutableArray<FieldViolation> fields, in Declaration declaration)
    {
        FieldViolation[]? promoted = null;

        for (var i = 0; i < fields.Length; i++)
        {
            var located = Locate(fields[i].Field, declaration);
            if (located == fields[i].Field) continue;

            promoted ??= [.. fields];
            promoted[i] = fields[i] with { Field = located };
        }

        return promoted;
    }

    private static RuleViolation[]? PromoteRules(ImmutableArray<RuleViolation> rules, in Declaration declaration)
    {
        RuleViolation[]? promoted = null;

        for (var i = 0; i < rules.Length; i++)
        {
            var pointers = rules[i].Fields.Items;
            InputPointer[]? promotedPointers = null;

            for (var j = 0; j < pointers.Length; j++)
            {
                var located = Locate(pointers[j], declaration);
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
    /// Stamps an unlocated pointer, leaving an already-located pointer untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Evidence outranks the declaration. A violation naming one of the endpoint's own route
    /// parameters is stamped <see cref="InputLocation.Path"/>, and one naming a query or header
    /// parameter the endpoint binds is stamped <see cref="InputLocation.Query"/> or
    /// <see cref="InputLocation.Header"/>, whatever the endpoint declared — because
    /// <c>POST /employee/{employeeId}</c> says where an <c>employeeId</c> came from more reliably
    /// than a declaration covering the endpoint as a whole. Without this, a body residual would
    /// tell a caller their request body was at fault for a value they put in the URL.
    /// </para>
    /// <para>
    /// What is left is the residual: the names the request's own parameters do not account for.
    /// It is derived too — an endpoint that binds a body is where those values arrived, and one
    /// that binds none leaves <see cref="InputLocation.Unspecified"/> standing. A declaration only
    /// overrides that residual, for the cases derivation cannot reach.
    /// </para>
    /// <para>
    /// The residual locates but does not verify: a domain producer may raise a name matching no
    /// member of the body it was bound from, so the <c>pointer</c> may address nothing. That is a
    /// property of the producer's naming rather than of deriving, since an explicit declaration
    /// yields the same pointer.
    /// </para>
    /// <para>
    /// The one case this gets wrong is a body member that shares a name with a route, query or
    /// header parameter — <c>PUT /employee/{id}</c> carrying <c>{"id": …}</c> — where the request's
    /// own parameters are evidence for a different value of the same name. Confirming that would
    /// mean reflecting over the body type's members, which the AOT-friendly projection path
    /// deliberately avoids.
    /// </para>
    /// <para>
    /// Named evidence applies only to a pointer naming one top-level member, because a nested
    /// pointer such as <c>/lines/0/amount</c> addresses a document that no URL can carry. The body
    /// residual has no such restriction — a nested pointer already addresses a body document — and
    /// reuses the framework's single body-context rebase rule.
    /// </para>
    /// </remarks>
    private static InputPointer Locate(InputPointer pointer, in Declaration declaration)
    {
        if (pointer.In is not InputLocation.Unspecified) return pointer;

        var name = NamesOneMember(pointer) ? ViolationProjection.ToName(pointer.Path) : null;

        if (name is not null)
        {
            if (IsRouteParameter(declaration.RouteParameters, name))
                return pointer with { In = InputLocation.Path };

            if (IsNamedParameter(declaration.QueryParameters, name))
                return pointer with { In = InputLocation.Query };

            if (IsNamedParameter(declaration.HeaderParameters, name))
                return pointer with { In = InputLocation.Header };
        }

        return declaration.Location switch
        {
            InputLocation.Body => ValidationErrorsContext.RebaseToBody(pointer, string.Empty),
            InputLocation.Query when name is not null => pointer with { In = InputLocation.Query },
            _ => pointer,
        };
    }

    private static bool IsRouteParameter(IReadOnlyList<RoutePatternParameterPart>? parameters, string name)
    {
        if (parameters is null) return false;

        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsNamedParameter(IReadOnlyList<string> parameters, string name)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool NamesOneMember(InputPointer pointer) =>
        pointer.Path.Length > 1 && pointer.Path[0] == '/' && pointer.Path.IndexOf('/', 1) < 0;
}
