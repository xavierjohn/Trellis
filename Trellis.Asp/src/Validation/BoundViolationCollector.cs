namespace Trellis.Asp.Validation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Trellis;

/// <summary>
/// A request-scoped side-channel carrying structured violations produced by MVC model binders.
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
    /// Records a violation for the current request, ignoring one already present for the same
    /// pointer and reason so a re-derivation cannot double-count it.
    /// </summary>
    public static void Add(HttpContext? httpContext, FieldViolation violation)
    {
        if (httpContext is null)
            return;

        var violations = GetOrCreate(httpContext);
        if (!violations.Contains(violation))
            violations.Add(violation);
    }

    /// <summary>
    /// Records every field violation carried by <paramref name="error"/>, re-stamping each
    /// pointer onto <paramref name="location"/>.
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

        if (error is Error.InvalidInput invalid && invalid.Fields.Items.Length > 0)
        {
            foreach (var violation in invalid.Fields)
                Add(httpContext, violation with { Field = Relocate(violation.Field, parameterName, location) });

            return;
        }

        Add(httpContext, new FieldViolation(
            Relocate(InputPointer.ForProperty(parameterName), parameterName, location),
            error.WireCode,
            Detail: error.Detail));
    }

    /// <summary>
    /// Gets the violations recorded for the current request, or an empty list.
    /// </summary>
    public static IReadOnlyList<FieldViolation> Get(HttpContext? httpContext) =>
        httpContext?.Items.TryGetValue(ItemsKey, out var existing) == true && existing is List<FieldViolation> violations
            ? violations
            : [];

    /// <summary>
    /// True when the collector already holds a violation for <paramref name="parameterName"/>,
    /// which is how the filter's early-out knows a binder has already reported the failure
    /// structurally and must not re-derive it from <c>ModelState</c>.
    /// </summary>
    public static bool HasViolationFor(HttpContext? httpContext, string parameterName)
    {
        foreach (var violation in Get(httpContext))
        {
            if (string.Equals(NameOf(violation.Field), parameterName, StringComparison.Ordinal))
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

    private static List<FieldViolation> GetOrCreate(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(ItemsKey, out var existing) && existing is List<FieldViolation> violations)
            return violations;

        var created = new List<FieldViolation>();
        httpContext.Items[ItemsKey] = created;
        return created;
    }
}
