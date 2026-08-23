namespace Trellis.Asp.Validation;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

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
/// Execution still goes through MVC's own formatter pipeline via an inner
/// <see cref="ObjectResult"/>, so the serialized body is identical to what the bare
/// <see cref="ObjectResult"/> produced.
/// </para>
/// <para>
/// The inner result declares <c>application/problem+json</c> and <c>application/problem+xml</c>,
/// in that order, because those are precisely the two media types MVC's own
/// <c>ObjectResultExecutor</c> infers for a <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/>
/// value whose content-type list is empty. Declaring them explicitly keeps content negotiation --
/// including XML problem documents and <c>ReturnHttpNotAcceptable</c> behaviour -- identical to the
/// bare <see cref="ObjectResult"/> this replaced. The only thing that changes is that
/// <c>[Produces]</c> can no longer overwrite the list.
/// </para>
/// </remarks>
internal sealed class ProblemDetailsActionResult : ActionResult, IStatusCodeActionResult
{
    private const string ProblemJsonMediaType = "application/problem+json";
    private const string ProblemXmlMediaType = "application/problem+xml";

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

        var inner = new ObjectResult(_problemDetails)
        {
            StatusCode = _statusCode,
            ContentTypes = { ProblemJsonMediaType, ProblemXmlMediaType },
        };

        return inner.ExecuteResultAsync(context);
    }
}
