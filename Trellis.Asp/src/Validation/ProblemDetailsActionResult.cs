namespace Trellis.Asp.Validation;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// An <see cref="ActionResult"/> that writes an RFC 9457 problem document and pins its media type
/// to <c>application/problem+json</c>.
/// </summary>
/// <remarks>
/// <para>
/// This exists to be invisible to <see cref="ProducesAttribute"/>. <c>[Produces]</c> is a result
/// filter that rewrites <see cref="ObjectResult.ContentTypes"/> wholesale, so a failure assigned as
/// a bare <see cref="ObjectResult"/> silently loses <c>application/problem+json</c> and stops
/// conforming to RFC 9457 -- while its status code and body stay correct, which is what makes the
/// regression so easy to miss. Setting <see cref="ObjectResult.ContentTypes"/> is not a defence,
/// because the filter overwrites it; only not being an <see cref="ObjectResult"/> is.
/// </para>
/// <para>
/// It no longer executes an inner <see cref="ObjectResult"/>. Doing so put the document through
/// MVC's formatter pipeline, and MVC would select an XML formatter for a request whose
/// <c>Accept</c> asked for one -- even when the inner result declared <c>problem+json</c> as its
/// only content type. That was fatal rather than merely surprising: this result carries
/// <c>fieldViolations</c> / <c>ruleViolations</c> entries in <c>ProblemDetails.Extensions</c>,
/// which is typed <c>object?</c>, and <c>XmlDataContractSerializerOutputFormatter</c> throws
/// <see cref="InvalidCastException"/> on values <c>DataContractSerializer</c> was not given via
/// <c>KnownTypeAttribute</c> -- which no application can supply for an <c>object?</c> member. Any
/// client could therefore turn every scalar-validation failure into an unhandled exception with a
/// single request header.
/// </para>
/// <para>
/// The body is written directly instead, through <see cref="ProblemJsonWriter"/> and using MVC's
/// own <see cref="JsonOptions"/>, so it stays byte-identical to what the JSON output formatter
/// produced for the same document -- including any <c>AddJsonOptions(...)</c> the application
/// configured. Only the possibility of negotiating away from JSON is removed.
/// </para>
/// </remarks>
internal sealed class ProblemDetailsActionResult : ActionResult, IStatusCodeActionResult
{
    private readonly object _problemDetails;
    private readonly int _statusCode;

    internal ProblemDetailsActionResult(object problemDetails, int statusCode)
    {
        _problemDetails = problemDetails;
        _statusCode = statusCode;
    }

    /// <summary>The problem document that will be written (exposed for testing).</summary>
    internal object Value => _problemDetails;

    /// <summary>
    /// The status code that will be written. Declared through
    /// <see cref="IStatusCodeActionResult"/> so MVC infrastructure can still read the status off
    /// the result, exactly as it could when this was an <see cref="ObjectResult"/>.
    /// </summary>
    public int? StatusCode => _statusCode;

    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.HttpContext;
        httpContext.Response.StatusCode = _statusCode;

        var serializerOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<JsonOptions>>()
            .Value.JsonSerializerOptions;

        return ProblemJsonWriter.WriteAsync(httpContext, _problemDetails, serializerOptions);
    }
}
