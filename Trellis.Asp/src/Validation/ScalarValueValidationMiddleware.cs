namespace Trellis.Asp;

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
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
public sealed partial class ScalarValueValidationMiddleware
{
    private readonly RequestDelegate _next;

    // Regex to parse: Failed to bind parameter "TypeName paramName" from "value".
    // The type name may include namespaces or nested type separators (e.g., Namespace.Type or Outer+Inner).
    [GeneratedRegex("""^Failed to bind parameter "(.+?)\s+([^"\s]+)" from "(.*)".$""", RegexOptions.Compiled)]
    private static partial Regex ParameterBindingFailedRegex();

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
            catch (BadHttpRequestException ex) when (IsParameterBindingFailureMessage(ex.Message))
            {
                // Parse the exception message to extract parameter info
                var (parameterName, typeName, invalidValue) = ParseBindingFailureMessage(ex.Message);

                // Only handle binding failures for IScalarValue types
                var scalarValueType = GetScalarValueParameterType(context, parameterName);
                if (scalarValueType is not null)
                {
                    // Call TryCreate to get the real validation error (e.g., with valid values list)
                    var errors = ScalarValueTypeHelper.GetValidationErrors(scalarValueType, invalidValue, parameterName)
                        ?? CreateFallbackErrors(parameterName, typeName, invalidValue);

                    await WriteValidationProblemAsync(context, errors).ConfigureAwait(false);
                }
                else
                {
                    // Re-throw for non-scalar value types (e.g., int, Guid, DateTime)
                    throw;
                }
            }
            catch (BadHttpRequestException ex) when (IsParameterReadFailureMessage(ex.Message))
            {
                // Handle JSON body deserialization failures (e.g., missing required properties)
                await WriteJsonDeserializationErrorAsync(context, ex).ConfigureAwait(false);
            }
        }
    }

    private static bool IsParameterBindingFailureMessage(string message) =>
        message.Contains("Failed to bind parameter", StringComparison.Ordinal);

    private static bool IsParameterReadFailureMessage(string message) =>
        message.Contains("Failed to read parameter", StringComparison.Ordinal);

    [UnconditionalSuppressMessage("AOT", "IL2072:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method. The return value of the source method does not have matching annotations.",
        Justification = "The type check for IScalarValue interfaces is safe - we only check interface implementation, not instantiate or invoke members.")]
    [UnconditionalSuppressMessage("AOT", "IL2073:Return type does not satisfy 'DynamicallyAccessedMembersAttribute' requirements.",
        Justification = "The returned type comes from ParameterInfo.ParameterType which preserves type metadata at runtime.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    private static Type? GetScalarValueParameterType(HttpContext context, string parameterName)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
            return null;

        // Get the parameter binding metadata from the endpoint
        var parameterMetadata = endpoint.Metadata
            .OfType<IParameterBindingMetadata>()
            .FirstOrDefault(p => p.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase));

        if (parameterMetadata is null)
            return null;

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

        var errors = new Dictionary<string, string[]>
        {
            ["$"] = ["The request body contains invalid JSON."]
        };

        var result = Results.ValidationProblem(errors);
        await result.ExecuteAsync(context).ConfigureAwait(false);
    }

    private static Dictionary<string, string[]> CreateFallbackErrors(
        string parameterName,
        string typeName,
        string invalidValue)
    {
        var errorMessage = string.IsNullOrEmpty(invalidValue)
            ? $"'{parameterName}' is required."
            : $"'{invalidValue}' is not a valid {typeName}.";

        return new Dictionary<string, string[]>
        {
            [parameterName] = [errorMessage]
        };
    }

    private static (string ParameterName, string TypeName, string InvalidValue) ParseBindingFailureMessage(string message)
    {
        // Try to parse: Failed to bind parameter "TypeName paramName" from "value".
        var match = ParameterBindingFailedRegex().Match(message);
        if (match.Success)
        {
            var typeName = match.Groups[1].Value;
            var paramName = match.Groups[2].Value;
            var invalidValue = match.Groups[3].Value;
            return (paramName, typeName, invalidValue);
        }

        // Fallback if regex doesn't match
        return ("parameter", "value", string.Empty);
    }
}