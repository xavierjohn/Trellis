namespace Trellis.Showcase.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

/// <summary>
/// Demonstrates a deterministic <see cref="Error.InternalServerError"/> path with a stable
/// fault identifier the client can quote in support tickets.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    [HttpGet("fault")]
    public ActionResult Fault() =>
        new Error.InternalServerError("DIAG-FAULT-001")
        {
            Detail = "Deterministic fault path used to demonstrate Error.InternalServerError mapping.",
        }.ToActionResult(this);
}
