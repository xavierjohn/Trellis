namespace Trellis.Asp.Validation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;

/// <summary>
/// A request-scoped side-channel carrying complete failures from scalar parameter binding.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelStateDictionary</c> holds strings, and <c>ModelStateExtensions.AddResultErrors</c> is
/// handed no <see cref="HttpContext"/>, so a binder-produced <see cref="FieldViolation"/> has
/// nowhere to survive between the binder and the action filter that renders the response. Without
/// this channel such violations reach the wire as message text only, degrading exactly the
/// failures a client is most likely to want to act on.
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

    /// <summary>
    /// Records the complete failure, relocating field and rule pointers onto
    /// <paramref name="location"/> without dropping root metadata.
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
        var relocated = invalid with
        {
            Fields = EquatableArray.From(invalid.Fields.Items.Select(violation => violation with
            {
                Field = Relocate(violation.Field, parameterName, location),
            }).Distinct()),
            Rules = EquatableArray.From(invalid.Rules.Items.Select(rule => rule with
            {
                Fields = EquatableArray.From(rule.Fields.Items.Select(field => Relocate(field, parameterName, location))),
            })),
        };

        GetOrCreate(httpContext).Add(new BoundError(parameterName, invalid, relocated));
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
    public static Error.InvalidInput? GetError(HttpContext? httpContext, Error.InvalidInput? additionalError = null)
    {
        var boundErrors = GetBoundErrors(httpContext);
        if (boundErrors is not { Count: > 0 })
            return additionalError;
        if (boundErrors.Count == 1 && additionalError is null)
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
    /// True when a recorded failure supplies the MVC error at <paramref name="modelStateKey"/>.
    /// </summary>
    public static bool HasModelStateErrorFor(HttpContext? httpContext, string modelStateKey)
    {
        var boundErrors = GetBoundErrors(httpContext);
        if (boundErrors is null)
            return false;

        foreach (var bound in boundErrors)
        {
            if (string.Equals(bound.ParameterName, modelStateKey, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var field in bound.OriginalError.Fields)
                if (string.Equals(JsonPointerToMvc.Translate(field.Field.Path), modelStateKey, StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        return false;
    }

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

    private sealed record BoundError(string ParameterName, Error.InvalidInput OriginalError, Error.InvalidInput Error);
}