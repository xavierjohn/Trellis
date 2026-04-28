namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
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
                .GroupBy(fv => fv.Field.Path.TrimStart('/'))
                .ToDictionary(g => g.Key, g => g.Select(fv => fv.Detail ?? fv.ReasonCode).ToArray());
            return Results.ValidationProblem(dictionary);
        }

        return await next(context).ConfigureAwait(false);
    }
}