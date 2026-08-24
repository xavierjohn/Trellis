namespace Trellis.Showcase.Mvc.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Demonstrates a deterministic <see cref="Error.Unexpected"/> path with a stable
/// fault identifier the client can quote in support tickets.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("fault")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Microsoft.AspNetCore.Http.IResult Fault() =>
        new Error.Unexpected(Code: "diagnostics-fault", FaultId: "DIAG-FAULT-001")
        {
            Detail = "Deterministic fault path used to demonstrate Error.Unexpected mapping.",
        }.ToHttpResponse();
}