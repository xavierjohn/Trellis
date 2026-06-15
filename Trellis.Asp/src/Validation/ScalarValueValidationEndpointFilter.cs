namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Trellis;

/// <summary>
/// An endpoint filter that checks for scalar value validation errors collected during JSON deserialization.
/// For Minimal APIs, this filter returns validation problem results when validation errors are detected.
/// </summary>
/// <remarks>
/// <para>
/// This filter works in conjunction with ValidatingJsonConverter and
/// <see cref="ValidationErrorsContext"/> to provide comprehensive validation error handling.
/// </para>
/// <para>
/// Unlike the MVC <see cref="ScalarValueValidationFilter"/>, this filter is designed for Minimal APIs
/// and returns <see cref="Microsoft.AspNetCore.Http.IResult"/> instead of manipulating ModelStateDictionary.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// app.MapPost("/users", (RegisterUserDto dto) => ...)
///    .AddEndpointFilter&lt;ScalarValueValidationEndpointFilter&gt;();
/// </code>
/// </example>
public sealed class ScalarValueValidationEndpointFilter : IEndpointFilter
{
    /// <summary>
    /// Invokes the filter, checking for validation errors collected during JSON deserialization.
    /// </summary>
    /// <param name="context">The endpoint filter invocation context.</param>
    /// <param name="next">The next filter in the pipeline.</param>
    /// <returns>
    /// A validation problem result if validation errors exist, otherwise the result from the next filter.
    /// </returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validationError = ValidationErrorsContext.GetUnprocessableContent();
        if (validationError is not null)
        {
            var dictionary = validationError.Fields.Items
                .GroupBy(fv => JsonPointerToMvc.Translate(fv.Field.Path))
                .ToDictionary(g => g.Key, g => g.Select(fv => fv.Detail ?? fv.ReasonCode).ToArray());

            // Trellis-driven scalar VO validation rejection — semantic per RFC 9110 §15.5.21.
            // The status honors the configured TrellisAspOptions Error.InvalidInput map (default
            // 422), aligning with the status code emitted by the rest of the framework
            // (ResponseFailureWriter for domain handler failures, ScalarValueValidationFilter
            // and ScalarValueValidationMiddleware for the same condition on other code paths).
            // instance: RFC 9457 §3.1 — server-relative path+query per ResponseFailureWriter convention.
            return Results.ValidationProblem(
                dictionary,
                instance: context.HttpContext.Request.GetEncodedPathAndQuery(),
                statusCode: ScalarValidationStatus.Resolve(context.HttpContext));
        }

        return await next(context).ConfigureAwait(false);
    }
}