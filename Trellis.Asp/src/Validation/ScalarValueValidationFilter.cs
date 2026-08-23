namespace Trellis.Asp;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp.Validation;

/// <summary>
/// An action filter that checks for validation errors collected during JSON deserialization
/// and validates <see cref="IScalarValue{TSelf, TPrimitive}"/> route/query parameters.
/// Returns a <c>ValidationProblemDetails</c> response — 422 Unprocessable Content for
/// Trellis-driven semantic validation failures (composite VO converter, scalar VO TryCreate,
/// <see cref="ValidationErrorsContext"/>-collected errors) per RFC 9110 §15.5.21, and
/// 400 Bad Request for plain <see cref="System.Text.Json.JsonException"/>s where the bytes
/// aren't valid JSON per RFC 9110 §15.5.1. When both occur on the same request, 400 wins.
/// </summary>
/// <remarks>
/// <para>
/// This filter works in conjunction with ValidatingJsonConverterFactory to provide
/// automatic validation of scalar values in request DTOs. The converter collects validation errors
/// during deserialization, and this filter checks for errors before the action executes.
/// </para>
/// <para>
/// Additionally, this filter validates route and query string parameters that are
/// <see cref="IScalarValue{TSelf, TPrimitive}"/> types. When model binding fails for these types
/// (resulting in a null parameter), the filter returns a validation error.
/// </para>
/// <para>
/// If validation errors are detected:
/// <list type="bullet">
/// <item>The action is short-circuited (not executed)</item>
/// <item>A <c>ValidationProblemDetails</c> response is returned — 422 for Trellis semantic
///   validation failures, 400 for plain JSON syntax errors (with 400 winning on mixed requests)</item>
/// <item>The response format matches ASP.NET Core's standard validation error format</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// The filter is typically registered globally in Program.cs:
/// <code>
/// builder.Services.AddControllers(options =>
/// {
///     options.Filters.Add&lt;ScalarValueValidationFilter&gt;();
/// });
/// </code>
/// </example>
public sealed class ScalarValueValidationFilter : IActionFilter, IOrderedFilter
{
    /// <summary>
    /// Gets the order value for filter execution. This filter runs early to catch validation errors
    /// before other filters or the action execute.
    /// </summary>
    public int Order => -2000; // Run early, before most other filters

    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        // First, check for validation errors from JSON deserialization that landed in
        // the per-request collection scope (Trellis scalar VO converters that fail
        // gracefully — e.g., MaybeScalarValueJsonConverter / ValidatingJsonConverter).
        // Precedence guard: if the same request ALSO failed with malformed JSON (a plain
        // JsonException in ModelState), the 400 path is authoritative — malformed bytes are a
        // more fundamental client error (RFC 9110 §15.5.1) than a semantic VO failure, and we
        // must not short-circuit to the semantic status. This mirrors the guard in
        // TryHandleStructuredModelStateErrors so both VO-failure entry points agree.
        var validationError = ValidationErrorsContext.GetUnprocessableContent();
        if (validationError is not null && !HasPlainJsonException(context.ModelState))
        {
            HandleJsonValidationErrors(context, validationError);
            return;
        }

        // Second, check for structured errors that propagated as exceptions through MVC's
        // input formatter and landed in ModelState. Composite VO converters throw
        // TrellisJsonValidationException with UnprocessableContent attached; the JSON input
        // formatter catches it (as JsonException), records the message + JsonException.Path
        // verbatim in ModelState, and the body parameter ends up null — which then prompts
        // the model binder to add a parameter-name "request": ["The request field is required."]
        // entry. Both shapes are wrong: the first collapses per-leaf violations into a joined
        // string, and the second is binding-pipeline noise that duplicates the same logical
        // condition under a key the client cannot act on. Replace both with per-leaf entries
        // built from the structured payload.
        if (TryHandleStructuredModelStateErrors(context))
            return;

        // Third, check for null IScalarValue route/query parameters (binding failures)
        ValidateScalarValueParameters(context);
    }

    // True when ModelState carries a plain System.Text.Json JsonException (malformed request
    // bytes), excluding the TrellisJsonValidationException subclass (a semantic value failure).
    // Malformed bytes are RFC 9110 §15.5.1 (400) and take precedence over semantic validation.
    private static bool HasPlainJsonException(ModelStateDictionary modelState)
    {
        foreach (var (_, entry) in modelState)
        {
            foreach (var error in entry.Errors)
            {
                if (error.Exception is System.Text.Json.JsonException and not TrellisJsonValidationException)
                    return true;
            }
        }

        return false;
    }

    private static bool TryHandleStructuredModelStateErrors(ActionExecutingContext context)
    {
        // The MVC key for an unstructured failure. The recorded ancestor wins over JsonException.Path
        // whenever a path-tracking wrapper handled the throw, because the wrapper's nested
        // JsonSerializer call has already displaced that property with a nested-root-relative value.
        static string UnstructuredParentKey(TrellisJsonValidationException exception) =>
            JsonValidationPathRebase.RecordedAbsolutePath(exception) is { } absolute
                ? JsonPointerToMvc.Translate(absolute)
                : ScalarValueValidationMiddleware.JsonPathToMvcKey(exception.Path);

        // Find any ModelState entry whose error carries a TrellisJsonValidationException.
        // Both shapes are handled:
        //   - Structured: UnprocessableContent has at least one FieldViolation -> emit one
        //     wire entry per violation under <parent>.<leaf> keys.
        //   - Unstructured: no UnprocessableContent (e.g. missing required property,
        //     unsupported primitive type, JSON shape mismatch) -> emit a single entry at the
        //     translated JSON path with the exception's curated message. Without this branch
        //     the message would be lost: ModelStateDictionary.TryAddModelError stores an empty
        //     ErrorMessage when the recorded exception isn't an InputFormatterException, and
        //     ValidationProblemDetails would render a generic placeholder.
        TrellisJsonValidationException? trellisEx = null;
        string? entryParentPath = null;
        var trellisEntryKeys = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        // Precedence guard: a plain JsonException in ModelState means the request body is
        // not valid JSON — a more fundamental client error than any semantic failure that
        // happened on the same request. The 400 path stays authoritative in that case so
        // we don't promote a request with malformed bytes to 422 just because one segment
        // of the input also failed Trellis validation.
        var hasPlainJsonException = false;

        foreach (var (key, entry) in context.ModelState)
        {
            foreach (var error in entry.Errors)
            {
                if (error.Exception is TrellisJsonValidationException tjx)
                {
                    // First match wins — additional Trellis exceptions in the same request
                    // are treated as duplicates (the converter throws on first failure).
                    trellisEx ??= tjx;
                    entryParentPath ??= UnstructuredParentKey(tjx);
                    trellisEntryKeys.Add(key);
                    break;
                }
                else if (error.Exception is System.Text.Json.JsonException)
                {
                    hasPlainJsonException = true;
                }
            }
        }

        if (trellisEx is null || hasPlainJsonException)
            return false;

        var freshModelState = new ModelStateDictionary();
        Error.InvalidInput? structuredError = null;
        if (trellisEx.InvalidInput is { Fields.Length: > 0 })
        {
            // Arms 1 and 2 — see ScalarValueValidationMiddleware for the rule. `entryParentPath`
            // stays in use for the unstructured branch below, where there is no pointer to rebase.
            var structured = JsonValidationPathRebase.IsMarked(trellisEx)
                ? trellisEx.InvalidInput!
                : JsonValidationPathRebase.RebaseTo(
                    trellisEx.InvalidInput!,
                    ScalarValueValidationMiddleware.JsonPathToPointer(trellisEx.Path));

            structuredError = structured;

            foreach (var fv in structured.Fields)
            {
                var combined = JsonPointerToMvc.Translate(fv.Field.Path);
                var detail = !string.IsNullOrEmpty(fv.Detail) ? fv.Detail : fv.ReasonCode;
                freshModelState.AddModelError(combined, detail);
            }
        }
        else
        {
            freshModelState.AddModelError(entryParentPath ?? string.Empty, trellisEx.Message);
        }

        // Carry forward any other ModelState errors that were neither the Trellis-exception
        // entry nor the phantom body-parameter entry. The phantom entry is identified strictly
        // by key match against an action [FromBody] parameter name — we do NOT filter on the
        // "X field is required." text globally, because that would silently drop legitimate
        // required errors from query/route/form parameters and other DataAnnotations failures.
        var bodyParameterNames = GetBodyParameterNames(context);
        foreach (var (key, entry) in context.ModelState)
        {
            if (trellisEntryKeys.Contains(key))
                continue;

            if (bodyParameterNames.Contains(key))
                continue;

            foreach (var error in entry.Errors)
            {
                if (string.IsNullOrEmpty(error.ErrorMessage))
                    continue;

                freshModelState.AddModelError(key, error.ErrorMessage);
            }
        }

        var statusCode = ScalarValidationStatus.Resolve(context.HttpContext);
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(
            context.HttpContext,
            freshModelState,
            statusCode: statusCode,
            instance: context.HttpContext.Request.GetEncodedPathAndQuery());
        AttachViolations(context, problemDetails, structuredError);

        // A TrellisJsonValidationException means a value object rejected a value, so this is an
        // Error.InvalidInput even on the unstructured branch where no violations survived.
        AttachEnvelope(problemDetails, structuredError ?? EmptyInvalidInput, statusCode);
        context.Result = new ProblemDetailsActionResult(problemDetails, statusCode);
        return true;
    }

    private static System.Collections.Generic.HashSet<string> GetBodyParameterNames(ActionExecutingContext context)
    {
        var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            if (parameter.BindingInfo?.BindingSource?.CanAcceptDataFrom(BindingSource.Body) == true
                && parameter.Name is { Length: > 0 })
                names.Add(parameter.Name);
        }

        return names;
    }

    private static void HandleJsonValidationErrors(ActionExecutingContext context, Error.InvalidInput validationError)
    {
        // Create a fresh ModelStateDictionary to avoid key casing issues.
        // MVC's model validation adds errors with PascalCase C# property names (e.g., "State").
        // ModelStateDictionary's internal trie preserves the original key casing even after
        // Remove + re-Add with different casing. Using a fresh dictionary ensures our
        // camelCase field names (matching JSON property names) are preserved correctly.
        var modelState = new ModelStateDictionary();
        foreach (var fieldViolation in validationError.Fields)
        {
            modelState.AddModelError(JsonPointerToMvc.Translate(fieldViolation.Field.Path), fieldViolation.Detail ?? fieldViolation.ReasonCode);
        }

        var statusCode = ScalarValidationStatus.Resolve(context.HttpContext);
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(
            context.HttpContext,
            modelState,
            statusCode: statusCode,
            instance: context.HttpContext.Request.GetEncodedPathAndQuery());
        AttachViolations(context, problemDetails, validationError);
        AttachEnvelope(problemDetails, validationError, statusCode);
        context.Result = new ProblemDetailsActionResult(problemDetails, statusCode);
    }

    private static ProblemDetailsActionResult CreateValidationProblemResult(
        ActionExecutingContext context,
        int statusCode,
        Error.InvalidInput? rejectedValue = null)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState,
            statusCode: statusCode,
            instance: context.HttpContext.Request.GetEncodedPathAndQuery());
        AttachViolations(context, problemDetails, null);
        AttachEnvelope(problemDetails, rejectedValue, statusCode);
        return new ProblemDetailsActionResult(problemDetails, statusCode);
    }

    /// <summary>
    /// Adds the <c>code</c> / <c>kind</c> members every failure response carries, whichever
    /// layer wrote it.
    /// </summary>
    /// <remarks>
    /// A rejected value is an <see cref="Error.InvalidInput"/> and names its own kind, so a
    /// remapped status does not change it. A failure with no error behind it — a request whose
    /// bytes never parsed, or a parameter MVC itself could not bind — has nothing finer to
    /// report than the HTTP condition.
    /// </remarks>
    private static void AttachEnvelope(ProblemDetails problemDetails, Error.InvalidInput? rejectedValue, int statusCode)
    {
        var envelope = rejectedValue is null
            ? ProblemEnvelope.ForStatus(statusCode)
            : ProblemEnvelope.ForError(rejectedValue);

        foreach (var member in envelope)
            problemDetails.Extensions[member.Key] = member.Value;
    }

    /// <summary>
    /// The envelope source for a rejected value the seam did not express as a populated error:
    /// the wire members depend only on the case, so an empty one answers for all of them.
    /// </summary>
    private static readonly Error.InvalidInput EmptyInvalidInput = new(default);

    /// <summary>
    /// Adds the structured violations to the problem, merging the ones carried by
    /// <paramref name="error"/> with those the model binders recorded on the side-channel.
    /// </summary>
    /// <remarks>
    /// The <c>errors</c> map and the structured members describe the same failures — the map is
    /// the lossy MVC-shaped view retained for compatibility. Both are emitted from one source so
    /// they cannot disagree.
    /// </remarks>
    private static void AttachViolations(
        ActionExecutingContext context,
        ProblemDetails problemDetails,
        Error.InvalidInput? error)
    {
        var fields = new List<FieldViolation>();
        if (error is not null)
            fields.AddRange(error.Fields.Items);

        foreach (var bound in BoundViolationCollector.Get(context.HttpContext))
        {
            if (!fields.Contains(bound))
                fields.Add(bound);
        }

        if (fields.Count > 0)
            problemDetails.Extensions["fieldViolations"] = ViolationProjection.ToFieldViolations(EquatableArray.Create([.. fields]));

        if (error is { Rules.Items.Length: > 0 })
            problemDetails.Extensions["ruleViolations"] = ViolationProjection.ToRuleViolations(error.Rules);
    }
    [UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.",
        Justification = "The type check for IScalarValue interfaces is safe - we only check interface implementation, not instantiate or invoke members.")]
    private static void ValidateScalarValueParameters(ActionExecutingContext context)
    {
        var actionParameters = context.ActionDescriptor.Parameters;

        // Track whether THIS pass identified any scalar-value-object failures so the final
        // result discrimination can choose the correct status code:
        //   - Scalar VO failure (binder couldn't construct the value, or TryCreate rejected it)
        //     → 422 Unprocessable Content (semantic per RFC 9110 §15.5.21).
        //   - Pre-existing ModelState invalidity from non-Trellis sources (plain JsonException
        //     for malformed JSON, [Required] field missing, type-conversion failures) → 400.
        var addedScalarValueFailure = false;

        foreach (var parameter in actionParameters)
        {
            var parameterType = parameter.ParameterType;

            // Handle nullable types (e.g., OrderState?)
            var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

            // Treat both raw IScalarValue and Maybe<IScalarValue> parameters as scalar-VO,
            // so MaybeModelBinder failures land on the same 422 path as ScalarValueModelBinder
            // failures (matches ScalarValueValidationMiddleware behavior on the Minimal API path).
            var isScalarValue = ScalarValueTypeHelper.IsScalarValue(underlyingType);
            var isMaybeScalarValue = ScalarValueTypeHelper.IsMaybeScalarValue(underlyingType);
            if (!isScalarValue && !isMaybeScalarValue)
                continue;

            // The TryCreate helper expects the inner scalar VO type, so unwrap Maybe<T> when
            // re-running validation to synthesize a structured error.
            var validationType = isMaybeScalarValue
                ? ScalarValueTypeHelper.GetMaybeInnerType(underlyingType)!
                : underlyingType;

            // A scalar-VO parameter has failed semantic validation in either of two shapes:
            //   (a) binding succeeded but produced null (action argument is null);
            //   (b) the binder rejected the value at bind-time and added a ModelState entry
            //       under the parameter name (action argument absent from the dictionary).
            // Both are semantic failures of the input value, not JSON syntax errors.
            var hasArg = context.ActionArguments.TryGetValue(parameter.Name!, out var value);
            var valueIsNull = hasArg && value is null;
            var hasModelStateError = context.ModelState.TryGetValue(parameter.Name!, out var mse)
                && mse is { Errors.Count: > 0 };

            if (!valueIsNull && !hasModelStateError)
                continue;

            var rawValue = GetRawParameterValue(context, parameter.Name!);
            if (rawValue is null)
                continue;

            if (ShouldTreatEmptyQueryValueAsMissing(context, parameter, rawValue))
                continue;

            // Only synthesize a TryCreate-derived error if the binder didn't already record
            // one for this parameter — avoids duplicate entries on the wire. The collector is
            // consulted alongside ModelState, because a binder that recorded structurally has
            // already said everything this re-derivation would say.
            if (!hasModelStateError && !BoundViolationCollector.HasViolationFor(context.HttpContext, parameter.Name!))
            {
                var derived = ScalarValueTypeHelper.GetValidationError(validationType, rawValue, parameter.Name!);

                if (derived is not null)
                {
                    BoundViolationCollector.AddFrom(
                        context.HttpContext,
                        derived,
                        parameter.Name!,
                        ResolveLocation(context, parameter));

                    foreach (var (fieldName, details) in ScalarValueTypeHelper.ToModelStateErrors(derived, parameter.Name!))
                        foreach (var detail in details)
                            context.ModelState.AddModelError(fieldName, detail);
                }
                else
                {
                    // Fallback when TryCreate is not available. Avoid reflecting the raw
                    // request value into the response so we don't leak unexpected user input
                    // (XSS-adjacent surface even with JSON escaping; mirrors the middleware's
                    // hardening on the same path).
                    var typeName = validationType.Name;
                    var errorMessage = string.IsNullOrEmpty(rawValue)
                        ? $"'{parameter.Name}' is required."
                        : $"'{parameter.Name}' is not in a valid format for {typeName}.";

                    context.ModelState.AddModelError(parameter.Name!, errorMessage);
                    BoundViolationCollector.AddFrom(
                        context.HttpContext,
                        new Error.InvalidInput(EquatableArray.Create(
                            new FieldViolation(
                                InputPointer.ForProperty(parameter.Name!),
                                ValidationCodes.Unspecified,
                                Detail: errorMessage))),
                        parameter.Name!,
                        ResolveLocation(context, parameter));
                }
            }

            addedScalarValueFailure = true;
        }

        if (addedScalarValueFailure)
        {
            // Precedence guard: if a plain JsonException is also present (the body had a JSON
            // syntax error in the same request), 400 wins. The bytes weren't valid JSON, which
            // is a more fundamental client error than a semantic failure on a route/query VO.
            var hasPlainJsonException = false;
            foreach (var (_, entry) in context.ModelState)
            {
                foreach (var error in entry.Errors)
                {
                    if (error.Exception is System.Text.Json.JsonException
                        and not TrellisJsonValidationException)
                    {
                        hasPlainJsonException = true;
                        break;
                    }
                }

                if (hasPlainJsonException) break;
            }

            context.Result = CreateValidationProblemResult(
                context,
                statusCode: hasPlainJsonException
                    ? StatusCodes.Status400BadRequest
                    : ScalarValidationStatus.Resolve(context.HttpContext),
                // A scalar value object rejected the bound value, unless the body also failed to
                // parse -- in which case nothing was ever rejected and 400 wins for the kind too.
                rejectedValue: hasPlainJsonException ? null : EmptyInvalidInput);
        }
        else if (!context.ModelState.IsValid)
        {
            context.Result = CreateValidationProblemResult(context, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Resolves the wire location of a parameter, falling back to where the value was actually
    /// found when the descriptor carries no explicit binding source.
    /// </summary>
    /// <remarks>
    /// Route data is checked before the query string, matching <see cref="GetRawParameterValue"/>,
    /// so the reported location always names the place the reported value came from.
    /// </remarks>
    private static InputLocation ResolveLocation(ActionExecutingContext context, ParameterDescriptor parameter)
    {
        var declared = BoundViolationCollector.ToInputLocation(parameter.BindingInfo?.BindingSource);
        if (declared != InputLocation.Unspecified)
            return declared;

        if (context.RouteData.Values.ContainsKey(parameter.Name!))
            return InputLocation.Path;

        return context.HttpContext.Request.Query.ContainsKey(parameter.Name!)
            ? InputLocation.Query
            : InputLocation.Unspecified;
    }

    private static string? GetRawParameterValue(ActionExecutingContext context, string parameterName)
    {
        // Try to get the raw value from route data
        if (context.RouteData.Values.TryGetValue(parameterName, out var routeValue))
            return routeValue?.ToString();

        // Try to get from query string
        if (context.HttpContext.Request.Query.TryGetValue(parameterName, out var queryValue))
            return queryValue.ToString();

        return null;
    }

    private static bool ShouldTreatEmptyQueryValueAsMissing(
        ActionExecutingContext context,
        ParameterDescriptor parameter,
        string rawValue)
    {
        if (!string.IsNullOrEmpty(rawValue))
            return false;

        if (context.RouteData.Values.ContainsKey(parameter.Name!))
            return false;

        return IsNullableReferenceParameter(parameter);
    }

    private static bool IsNullableReferenceParameter(ParameterDescriptor parameter)
    {
        if (parameter is not ControllerParameterDescriptor controllerParameter)
            return false;

        if (controllerParameter.ParameterType.IsValueType)
            return Nullable.GetUnderlyingType(controllerParameter.ParameterType) is not null;

        // NullabilityInfoContext is documented as not thread-safe, so we instantiate per call
        // rather than caching a shared static instance that could be accessed concurrently
        // by parallel requests. See: https://learn.microsoft.com/dotnet/api/system.reflection.nullabilityinfocontext
        var nullabilityContext = new NullabilityInfoContext();
        var nullability = nullabilityContext.Create(controllerParameter.ParameterInfo);
        return nullability.ReadState == NullabilityState.Nullable;
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
        // No action needed after execution
    }
}