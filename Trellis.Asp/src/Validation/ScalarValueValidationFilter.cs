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
        var validationError = ValidationErrorsContext.GetUnprocessableContent();
        if (validationError is not null)
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

    private static bool TryHandleStructuredModelStateErrors(ActionExecutingContext context)
    {
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
                    entryParentPath ??= ScalarValueValidationMiddleware.JsonPathToMvcKey(tjx.Path);
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
        if (trellisEx.InvalidInput is { Fields.Length: > 0 } structured)
        {
            foreach (var fv in structured.Fields)
            {
                var leafKey = JsonPointerToMvc.Translate(fv.Field.Path);
                var combined = ScalarValueValidationMiddleware.CombineMvcKeys(entryParentPath ?? string.Empty, leafKey);
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
        context.Result = new ObjectResult(problemDetails) { StatusCode = statusCode };
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
        context.Result = new ObjectResult(problemDetails) { StatusCode = statusCode };
    }

    private static ObjectResult CreateValidationProblemResult(ActionExecutingContext context, int statusCode)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(
            context.HttpContext,
            context.ModelState,
            statusCode: statusCode,
            instance: context.HttpContext.Request.GetEncodedPathAndQuery());
        return new ObjectResult(problemDetails) { StatusCode = statusCode };
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
            // one for this parameter — avoids duplicate entries on the wire.
            if (!hasModelStateError)
            {
                var errors = ScalarValueTypeHelper.GetValidationErrors(validationType, rawValue, parameter.Name!);

                if (errors is not null)
                {
                    foreach (var (fieldName, details) in errors)
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
                    : ScalarValidationStatus.Resolve(context.HttpContext));
        }
        else if (!context.ModelState.IsValid)
        {
            context.Result = CreateValidationProblemResult(context, statusCode: StatusCodes.Status400BadRequest);
        }
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