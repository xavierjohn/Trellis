namespace Trellis.Asp.ApiVersioning.Tests.SharedRoutes.v2026_11_12;

public sealed class WidgetsController : SharedRoutePagedControllerBase
{
    protected override string Version => "2026-11-12";
}

public sealed class LegacyWidgetsController : SharedRoutePagedControllerBase
{
    protected override string Version => "2026-11-12";
}