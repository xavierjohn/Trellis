namespace Trellis.Asp;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Trellis;
using Trellis.Asp.Validation;

/// <summary>
/// Middleware that creates a validation error collection scope for each request.
/// This enables ValidatingJsonConverter to collect validation errors
/// across the entire request deserialization process.
/// </summary>
/// <remarks>
/// <para>
/// This middleware should be registered early in the pipeline, before any middleware
/// that might deserialize JSON request bodies.
/// </para>
/// <para>
/// For each request:
/// <list type="bullet">
/// <item>Creates a new validation error collection scope</item>
/// <item>Allows the request to proceed through the pipeline</item>
/// <item>Catches <see cref="BadHttpRequestException"/> for <see cref="IScalarValue{TSelf, TPrimitive}"/> parameter binding failures and returns validation problem</item>
/// <item>Cleans up the scope when the request completes</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Registering the middleware in Program.cs:
/// <code>
/// app.UseScalarValueValidation();
/// // ... other middleware
/// app.MapControllers();
/// </code>
/// </example>
public sealed class ScalarValueValidationMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates a new instance of <see cref="ScalarValueValidationMiddleware"/>.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public ScalarValueValidationMiddleware(RequestDelegate next) =>
        _next = next;

    /// <summary>
    /// Invokes the middleware, wrapping the request in a validation scope.
    /// </summary>
    /// <param name="context">The HTTP context for the request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        using (ValidationErrorsContext.BeginScope())
        {
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status400BadRequest)
            {
                if (ex.InnerException is JsonException)
                {
                    // Handle JSON body deserialization failures (e.g., missing required properties)
                    await WriteJsonDeserializationErrorAsync(context, ex).ConfigureAwait(false);
                }
                else if (TryCreateScalarBindingErrors(context, out var errors))
                {
                    await WriteValidationProblemAsync(context, errors).ConfigureAwait(false);
                }
                else if (HasEndpointParameterMetadata(context))
                {
                    // Endpoint binding failed, but Trellis could not map it to a scalar-value parameter.
                    // Let ASP.NET Core's normal BadHttpRequestException handling deal with non-Trellis parameters.
                    throw;
                }
                else
                {
                    // Unrecognized 400 format - return generic 400 to prevent 500 propagation
                    await WriteGenericBadRequestAsync(context).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task WriteGenericBadRequestAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        var errors = new Dictionary<string, string[]>
        {
            ["$"] = ["The request was invalid."]
        };

        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage("AOT", "IL2072:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.",
        Justification = "The type check for IScalarValue interfaces is safe - we only check interface implementation, not instantiate or invoke members.")]
    [UnconditionalSuppressMessage("AOT", "IL2073:Return type does not satisfy 'DynamicallyAccessedMembersAttribute' requirements.",
        Justification = "The returned type comes from ParameterInfo.ParameterType which preserves type metadata at runtime.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    private static Type? GetScalarValueParameterType(IParameterBindingMetadata parameterMetadata)
    {
        // Check if the parameter type implements IScalarValue<,>
        var parameterType = parameterMetadata.ParameterInfo.ParameterType;

        // Handle nullable types (e.g., OrderState?)
        var underlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

        if (ScalarValueTypeHelper.IsScalarValue(underlyingType))
            return underlyingType;

        var maybeInnerType = ScalarValueTypeHelper.GetMaybeInnerType(underlyingType);
        return maybeInnerType is not null && ScalarValueTypeHelper.IsScalarValue(maybeInnerType)
            ? maybeInnerType
            : null;
    }

    private static bool TryCreateScalarBindingErrors(
        HttpContext context,
        out IDictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var parameterMetadata in GetEndpointParameterMetadata(context))
        {
            var parameterName = parameterMetadata.Name;
            if (string.IsNullOrWhiteSpace(parameterName))
                continue;

            var scalarValueType = GetScalarValueParameterType(parameterMetadata);
            if (scalarValueType is null)
                continue;

            var rawValue = GetRawParameterValue(context, parameterName);
            if (rawValue is null)
                continue;

            var parameterErrors = ScalarValueTypeHelper.GetValidationErrors(scalarValueType, rawValue, parameterName);
            if (parameterErrors is null)
                continue;

            foreach (var (fieldName, details) in parameterErrors)
                errors[fieldName] = details;
        }

        return errors.Count > 0;
    }

    private static string? GetRawParameterValue(HttpContext context, string parameterName)
    {
        if (context.Request.RouteValues.TryGetValue(parameterName, out var routeValue))
            return routeValue?.ToString();

        if (context.Request.Query.TryGetValue(parameterName, out var queryValue))
            return queryValue.ToString();

        return null;
    }

    private static bool HasEndpointParameterMetadata(HttpContext context) =>
        GetEndpointParameterMetadata(context).Any();

    private static IEnumerable<IParameterBindingMetadata> GetEndpointParameterMetadata(HttpContext context) =>
        context.GetEndpoint()?.Metadata.OfType<IParameterBindingMetadata>() ?? [];

    private static async Task WriteValidationProblemAsync(
        HttpContext context,
        IDictionary<string, string[]> errors)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        // Use Results.ValidationProblem for consistent response format
        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    private static async Task WriteJsonDeserializationErrorAsync(HttpContext context, BadHttpRequestException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;

        // Surface the inner exception's message ONLY for TrellisJsonValidationException, which is
        // the dedicated marker thrown by Trellis converters with curated, client-safe messages
        // (e.g. Money: "Amount cannot be negative"). Plain JsonException keeps the generic message
        // because System.Text.Json's built-in errors can include internal type names.
        var (key, message) = ex.InnerException is Trellis.TrellisJsonValidationException tjx
            ? (string.IsNullOrEmpty(tjx.Path) ? "$" : tjx.Path!, tjx.Message)
            : ("$", "The request body contains invalid JSON.");

        var errors = new Dictionary<string, string[]>
        {
            [key] = [message],
        };

        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

}
