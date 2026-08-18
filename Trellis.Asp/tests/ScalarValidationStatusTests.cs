namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Unit tests for <see cref="ScalarValidationStatus"/> — the single resolver every binder/filter
/// validation seam delegates to. Pins the default (422) and the configurable override behavior.
/// </summary>
public sealed class ScalarValidationStatusTests
{
    [Fact]
    public void Resolve_WithDefaultOptions_Returns422()
    {
        var context = ContextWith(_ => { });

        ScalarValidationStatus.Resolve(context).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void Resolve_WithInvalidInputMappedTo400_Returns400()
    {
        var context = ContextWith(o => o.MapError<Error.InvalidInput>(StatusCodes.Status400BadRequest));

        ScalarValidationStatus.Resolve(context).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Resolve_WithNoAmbientOptions_FallsBackTo422()
    {
        // No RequestServices / TrellisAspOptions registered → ErrorStatusCodeResolver uses
        // TrellisAspOptions.SystemDefault, which maps Error.InvalidInput to 422.
        var context = new DefaultHttpContext();

        ScalarValidationStatus.Resolve(context).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    private static DefaultHttpContext ContextWith(System.Action<TrellisAspOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp(configure);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }
}