namespace Trellis.Asp.Validation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;

/// <summary>
/// A request-scoped side-channel carrying complete failures from scalar parameter binding.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelStateDictionary</c> carries MVC error entries rather than complete Trellis failures.
/// This channel retains the structured payload between binding and response rendering and
/// identifies the exact <see cref="ModelError"/> entries produced by scalar binding, without
/// claiming unrelated errors at the same key.
/// </para>
/// <para>
/// The binder keeps writing to <c>ModelState</c> as well — MVC's own invalid-model short-circuit
/// depends on it — so this collector adds a parallel structured record rather than replacing one.
/// </para>
/// <para>
/// <see cref="HttpContext.Items"/> rather than an <c>AsyncLocal</c>: the existing
/// <see cref="ValidationErrorsContext"/> scope is opened by the JSON pipeline, which never runs
/// for a route, query or header parameter.
/// </para>
/// <para>
/// The collector carries the <em>binding source</em>, because the binder is the only point in the
/// pipeline that knows it. A violation that loses it forces the projection to guess at <c>in</c>,
/// and a guessed location is a checkable claim that may be false.
/// </para>
/// </remarks>
internal static class BoundViolationCollector
{
    private const string ItemsKey = "Trellis.Asp.BoundViolations";
    private const string ModelStateErrorsKey = "Trellis.Asp.BoundModelStateErrors";

    /// <summary>
    /// Records the complete failure, relocating field and rule pointers onto
    /// <paramref name="location"/> without dropping root metadata. Equivalent reports for the
    /// same parameter and location are recorded once, including their diagnostic causes.
    /// Query, path and header binding-name comparisons are case-insensitive.
    /// </summary>
    /// <remarks>
    /// For a named location the pointer is replaced by the parameter name rather than merely
    /// re-stamped: a path into the interior of a scalar value object is not addressable in a
    /// query string, so the name is the only part the client can act on.
    /// </remarks>
    public static void AddFrom(HttpContext? httpContext, Error error, string parameterName, InputLocation location)
    {
        if (httpContext is null)
            return;

        var invalid = error as Error.InvalidInput
            ?? new Error.InvalidInput(EquatableArray.Create(new FieldViolation(
                InputPointer.ForProperty(parameterName), error.Code, Detail: error.Detail)))
            {
                Code = error.Code,
                Detail = error.Detail,
                Cause = error,
            };
        var boundErrors = GetOrCreate(httpContext);
        if (boundErrors.Any(bound => IsEquivalentBinding(bound, invalid, parameterName, location)))
            return;

        var relocated = invalid with
        {
            Fields = EquatableArray.From(invalid.Fields.Items.Select(violation => violation with
            {
                Field = Relocate(violation.Field, parameterName, location),
            }).Distinct()),
            Rules = EquatableArray.From(invalid.Rules.Items.Select(rule => rule with
            {
                Fields = EquatableArray.From(rule.Fields.Items.Select(field => Relocate(field, parameterName, location)).Distinct()),
            })),
        };

        boundErrors.Add(new BoundError(parameterName, location, invalid, relocated));
    }

    /// <summary>
    /// Gets the violations recorded for the current request, or an empty list.
    /// </summary>
    public static IReadOnlyList<FieldViolation> Get(HttpContext? httpContext) =>
        GetBoundErrors(httpContext)?.SelectMany(bound => bound.Error.Fields.Items).Distinct().ToArray() ?? [];

    /// <summary>
    /// Retains a single failure's metadata, or merges independent failures under a neutral root
    /// with their complete errors retained in an aggregate cause.
    /// </summary>
    /// <remarks>
    /// <paramref name="hasOtherFailures"/> says unrelated failures exist that have no
    /// <see cref="Error"/> to contribute, so a lone bound failure must not claim the root. It is a
    /// flag rather than a placeholder error so nothing fabricated reaches the aggregate cause.
    /// </remarks>
    public static Error.InvalidInput? GetError(
        HttpContext? httpContext,
        Error.InvalidInput? additionalError = null,
        bool hasOtherFailures = false)
    {
        var boundErrors = GetBoundErrors(httpContext);
        if (boundErrors is not { Count: > 0 })
            return additionalError;
        if (boundErrors.Count == 1 && additionalError is null && !hasOtherFailures)
            return boundErrors[0].Error;

        var fields = additionalError?.Fields.Items.ToList() ?? [];
        var rules = additionalError?.Rules.Items.ToList() ?? [];
        var errors = new List<Error>();
        if (additionalError is not null)
            errors.Add(additionalError);

        foreach (var bound in boundErrors)
        {
            errors.Add(bound.OriginalError);
            foreach (var field in bound.Error.Fields)
                if (!fields.Contains(field))
                    fields.Add(field);

            foreach (var rule in bound.Error.Rules)
                if (!rules.Contains(rule))
                    rules.Add(rule);
        }

        return new Error.InvalidInput(EquatableArray.From(fields), EquatableArray.From(rules))
        {
            Cause = new Error.Aggregate(errors),
        };
    }

    /// <summary>
    /// True when the collector already holds a failure for <paramref name="parameterName"/>,
    /// which is how the filter's early-out knows a binder has already reported the failure
    /// structurally and must not re-derive it from <c>ModelState</c>.
    /// </summary>
    public static bool HasViolationFor(HttpContext? httpContext, string parameterName)
    {
        var boundErrors = GetBoundErrors(httpContext);
        if (boundErrors is null)
            return false;

        foreach (var bound in boundErrors)
        {
            if (string.Equals(bound.ParameterName, parameterName, StringComparison.Ordinal))
                return true;

            foreach (var violation in bound.Error.Fields)
                if (string.Equals(NameOf(violation.Field), parameterName, StringComparison.Ordinal))
                    return true;
        }

        return false;
    }

    /// <summary>
    /// Records the exact MVC error produced by a scalar binding failure.
    /// </summary>
    public static void RecordModelStateError(HttpContext? httpContext, ModelError error)
    {
        if (httpContext is null)
            return;

        if (!httpContext.Items.TryGetValue(ModelStateErrorsKey, out var existing)
            || existing is not HashSet<ModelError> errors)
        {
            errors = new HashSet<ModelError>(ReferenceEqualityComparer.Instance);
            httpContext.Items[ModelStateErrorsKey] = errors;
        }

        errors.Add(error);
    }

    /// <summary>
    /// True when a scalar binding failure produced this specific MVC error.
    /// </summary>
    public static bool HasModelStateErrorFor(HttpContext? httpContext, ModelError error) =>
        httpContext?.Items.TryGetValue(ModelStateErrorsKey, out var existing) == true
        && existing is HashSet<ModelError> errors
        && errors.Contains(error);

    /// <summary>
    /// Maps an MVC binding source onto the wire location. An unrecognized source yields
    /// <see cref="InputLocation.Unspecified"/>, which projects as <c>"unknown"</c> — the
    /// producer declining to assert a location it does not know.
    /// </summary>
    public static InputLocation ToInputLocation(BindingSource? source)
    {
        if (source is null)
            return InputLocation.Unspecified;
        if (source.CanAcceptDataFrom(BindingSource.Path))
            return InputLocation.Path;
        if (source.CanAcceptDataFrom(BindingSource.Query))
            return InputLocation.Query;
        if (source.CanAcceptDataFrom(BindingSource.Header))
            return InputLocation.Header;
        if (source.CanAcceptDataFrom(BindingSource.Body))
            return InputLocation.Body;

        return InputLocation.Unspecified;
    }

    private static bool IsEquivalentBinding(
        BoundError bound,
        Error.InvalidInput error,
        string parameterName,
        InputLocation location)
    {
        if (bound.Location != location)
            return false;
        if (bound.ParameterName == parameterName)
            return EqualsIncludingCauses(bound.OriginalError, error);
        if (location is not (InputLocation.Query or InputLocation.Path or InputLocation.Header)
            || !string.Equals(bound.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (EqualsIncludingCauses(bound.OriginalError, error))
            return true;

        var parameterPath = InputPointer.ForProperty(parameterName).Path;
        var canonicalPath = InputPointer.ForProperty(bound.ParameterName).Path;
        InputPointer Normalize(InputPointer pointer) =>
            pointer.Path == parameterPath ? pointer with { Path = canonicalPath } : pointer;

        var normalized = error with
        {
            Fields = EquatableArray.From(error.Fields.Items.Select(field => field with { Field = Normalize(field.Field) })),
            Rules = EquatableArray.From(error.Rules.Items.Select(rule => rule with
            {
                Fields = EquatableArray.From(rule.Fields.Items.Select(Normalize)),
            })),
        };
        return EqualsIncludingCauses(bound.OriginalError, normalized);
    }

    private static bool EqualsIncludingCauses(Error? left, Error? right)
    {
        while (!ReferenceEquals(left, right))
        {
            if (left is null || right is null || left != right)
                return false;

            if (left is Error.Aggregate leftAggregate && right is Error.Aggregate rightAggregate)
                for (var index = 0; index < leftAggregate.Errors.Length; index++)
                    if (!EqualsIncludingCauses(leftAggregate.Errors[index], rightAggregate.Errors[index]))
                        return false;

            left = left.Cause;
            right = right.Cause;
        }

        return true;
    }

    private static InputPointer Relocate(InputPointer pointer, string parameterName, InputLocation location) =>
        location switch
        {
            InputLocation.Query => InputPointer.ForQuery(parameterName),
            InputLocation.Path => InputPointer.ForPath(parameterName),
            InputLocation.Header => InputPointer.ForHeader(parameterName),
            InputLocation.Body => pointer with { In = InputLocation.Body },
            _ => pointer,
        };

    private static string NameOf(InputPointer pointer)
    {
        var token = pointer.Path.Length > 0 && pointer.Path[0] == '/' ? pointer.Path[1..] : pointer.Path;
        return token.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);
    }

    private static List<BoundError>? GetBoundErrors(HttpContext? httpContext) =>
        httpContext?.Items.TryGetValue(ItemsKey, out var existing) == true && existing is List<BoundError> errors
            ? errors
            : null;

    private static List<BoundError> GetOrCreate(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(ItemsKey, out var existing) && existing is List<BoundError> errors)
            return errors;

        var created = new List<BoundError>();
        httpContext.Items[ItemsKey] = created;
        return created;
    }

    private sealed record BoundError(string ParameterName, InputLocation Location, Error.InvalidInput OriginalError, Error.InvalidInput Error);
}