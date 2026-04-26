namespace Trellis.Asp;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
/// Returns a 400 Bad Request response with validation problem details when validation fails.
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
/// <item>A 400 Bad Request response with validation problem details is returned</item>
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
        // First, check for validation errors from JSON deserialization
        var validationError = ValidationErrorsContext.GetUnprocessableContent();
        if (validationError is not null)
        {
            HandleJsonValidationErrors(context, validationError);
            return;
        }

        // Second, check for null IScalarValue route/query parameters (binding failures)
        ValidateScalarValueParameters(context);
    }

    private static void HandleJsonValidationErrors(ActionExecutingContext context, Error.UnprocessableContent validationError)
    {
        // Create a fresh ModelStateDictionary to avoid key casing issues.
        // MVC's model validation adds errors with PascalCase C# property names (e.g., "State").
        // ModelStateDictionary's internal trie preserves the original key casing even after
        // Remove + re-Add with different casing. Using a fresh dictionary ensures our
        // camelCase field names (matching JSON property names) are preserved correctly.
        var modelState = new ModelStateDictionary();
        foreach (var fieldViolation in validationError.Fields)
        {
            modelState.AddModelError(fieldViolation.Field.Path.TrimStart('/'), fieldViolation.Detail ?? fieldViolation.ReasonCode);
        }

        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(context.HttpContext, modelState, statusCode: 400);
        context.Result = new BadRequestObjectResult(problemDetails);
    }

    private static BadRequestObjectResult CreateValidationProblemResult(ActionExecutingContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState, statusCode: 400);
        return new BadRequestObjectResult(problemDetails);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.",
        Justification = "The type check for IScalarValue interfaces is safe - we only check interface implementation, not instantiate or invoke members.")]
    private static void ValidateScalarValueParameters(ActionExecutingContext context)
    {
        var actionParameters = context.ActionDescriptor.Parameters;

        foreach (var parameter in actionParameters)
        {
            var parameterType = parameter.ParameterType;

            // Handle nullable types (e.g., OrderState?)
            var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

            // Skip if not an IScalarValue type
            if (!ScalarValueTypeHelper.IsScalarValue(underlyingType))
                continue;

            // Check if the parameter value is null (indicates binding failure for non-nullable IScalarValue)
            if (context.ActionArguments.TryGetValue(parameter.Name!, out var value) && value is null)
            {
                // Check whether a raw value was actually provided in the request.
                // If no raw value exists in route data or query string, the parameter
                // was simply not provided (e.g., optional query param like OrderState? state).
                // Only validate when a raw value is present but binding produced null (invalid input).
                var rawValue = GetRawParameterValue(context, parameter.Name!);
                if (rawValue is null)
                    continue;

                if (ShouldTreatEmptyQueryValueAsMissing(context, parameter, rawValue))
                    continue;

                // Call TryCreate to get the real validation error (e.g., with valid values list)
                var errors = ScalarValueTypeHelper.GetValidationErrors(underlyingType, rawValue, parameter.Name!);

                if (errors is not null)
                {
                    foreach (var (fieldName, details) in errors)
                        foreach (var detail in details)
                            context.ModelState.AddModelError(fieldName, detail);
                }
                else
                {
                    // Fallback if TryCreate method wasn't found
                    var typeName = underlyingType.Name;
                    var errorMessage = string.IsNullOrEmpty(rawValue)
                        ? $"'{parameter.Name}' is required."
                        : $"'{rawValue}' is not a valid {typeName}.";

                    context.ModelState.AddModelError(parameter.Name!, errorMessage);
                }
            }
        }

        // Return validation problem if any errors were added
        if (!context.ModelState.IsValid)
            context.Result = CreateValidationProblemResult(context);
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

    private static readonly NullabilityInfoContext s_nullabilityInfoContext = new();

    private static bool IsNullableReferenceParameter(ParameterDescriptor parameter)
    {
        if (parameter is not ControllerParameterDescriptor controllerParameter)
            return false;

        if (controllerParameter.ParameterType.IsValueType)
            return Nullable.GetUnderlyingType(controllerParameter.ParameterType) is not null;

        var nullability = s_nullabilityInfoContext.Create(controllerParameter.ParameterInfo);
        return nullability.ReadState == NullabilityState.Nullable;
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
        // No action needed after execution
    }
}
