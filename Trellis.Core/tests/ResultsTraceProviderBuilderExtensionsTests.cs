namespace Trellis.Core.Tests;

using OpenTelemetry.Trace;
using Trellis.Testing;

public class ResultsTraceProviderBuilderExtensionsTests
{
    [Fact]
    public void AddTrellisResultsInstrumentation_NullBuilder_ThrowsArgumentNullException_WithBuilderParamName()
    {
        // N-C-6 (GPT-5.5 meta-review): public extension methods should throw
        // ArgumentNullException with the user's paramName at entry rather than relying on the
        // underlying TracerProviderBuilder.AddSource(...) call to surface a null failure
        // late.
        TracerProviderBuilder builder = null!;

        var act = () => builder.AddTrellisResultsInstrumentation();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("builder");
    }
}