namespace Trellis.Showcase.MinimalApi.Endpoints;

using Trellis;
using Trellis.Asp;

/// <summary>
/// Demonstrates a deterministic <see cref="Error.Unexpected"/> path with a stable
/// fault identifier the client can quote in support tickets.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/diagnostics").WithTags("Diagnostics");

        group.MapGet("/fault", () =>
            new Error.Unexpected(Code: "diagnostics-fault", FaultId: "DIAG-FAULT-001")
            {
                Detail = "Deterministic fault path used to demonstrate Error.Unexpected mapping.",
            }.ToHttpResponse())
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return routes;
    }
}