namespace Trellis;

using OpenTelemetry.Trace;

/// <summary>
/// Extension methods for configuring OpenTelemetry tracing for Railway Oriented Programming operations.
/// </summary>
public static class ResultsTraceProviderBuilderExtensions
{
    /// <summary>
    /// Adds Trellis Railway Oriented Programming instrumentation to the OpenTelemetry tracer provider.
    /// This enables distributed tracing and observability for Result operations.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to configure.</param>
    /// <returns>The same <see cref="TracerProviderBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method registers the Trellis ROP activity source with OpenTelemetry,
    /// allowing you to trace Result operations through your application using tools like
    /// Application Insights, Jaeger, Zipkin, or other OpenTelemetry-compatible backends.
    /// </para>
    /// <para>
    /// ROP operations will automatically create activities and spans when this instrumentation is enabled,
    /// providing visibility into success/failure paths and error information.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddOpenTelemetry()
    ///     .WithTracing(builder => builder
    ///         .AddResultsInstrumentation()
    ///         .AddAspNetCoreInstrumentation()
    ///         .AddConsoleExporter());
    /// </code>
    /// </example>
    public static TracerProviderBuilder AddResultsInstrumentation(this TracerProviderBuilder builder)
        => builder.AddSource(RopTrace.ActivitySourceName);
}
