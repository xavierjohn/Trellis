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
    /// <para>
    /// <strong>Performance characteristics.</strong> Trellis is designed so the per-operation tracing
    /// is essentially free when no listener is registered (the production default — calling
    /// <c>AddResultsInstrumentation</c> is the only way to register the <c>"Trellis.Core"</c> source).
    /// Measured on .NET 10 / x64 with an ambient ASP.NET request activity present:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    /// <b>No listener registered</b> (default): a 10-step <c>Bind</c> chain costs ~242 ns total
    /// (~19 ns per <c>Bind</c>) with <b>0 bytes allocated</b>. The per-extension
    /// <c>using var activity = ActivitySource.StartActivity(...)</c> returns null almost immediately,
    /// and the <c>Result&lt;T&gt;</c> constructor's <c>Activity.Current?.SetStatus(...)</c>
    /// updates the ambient activity in place without allocating.
    ///   </description></item>
    ///   <item><description>
    /// <b>With listener registered</b> via <c>AddResultsInstrumentation</c> and
    /// <c>AllDataAndRecorded</c> sampling: each <c>Bind</c>/<c>Map</c>/<c>Tap</c> call costs
    /// ~200 ns and allocates ~400 B (the Activity object + name + tags). A 10-step chain
    /// costs ~2.3 μs and ~4 KB. At 10 000 RPS with a 10-step pipeline this adds up to
    /// ~22 ms/sec of CPU and ~40 MB/sec of GC pressure — material at high throughput.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <strong>Granularity guidance.</strong> Per-Result-extension spans add limited signal beyond
    /// the outer pipeline span (<c>Trellis.Mediator.TracingBehavior</c>) or the ASP.NET request span.
    /// They appear as a deeply nested tree under the outer span with no business context — most
    /// observability backends collapse or charge per span. For high-throughput services prefer
    /// instrumenting at the pipeline-behavior or HTTP-boundary altitude and reserve
    /// <c>AddResultsInstrumentation</c> for development/debugging or low-rate request paths where
    /// step-by-step ROP visibility is the diagnostic goal.
    /// </para>
    /// <para>
    /// Benchmark source: <c>Trellis.Benchmark/TracingOverheadBenchmarks.cs</c>. Re-run before
    /// drawing conclusions for a different runtime (e.g., NativeAOT) or hardware tier.
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