namespace Trellis.Mediator;

using OpenTelemetry.Trace;

/// <summary>
/// Extension methods for configuring OpenTelemetry tracing for the Trellis mediator pipeline.
/// </summary>
public static class MediatorTraceProviderBuilderExtensions
{
    /// <summary>
    /// Adds Trellis mediator pipeline instrumentation to the OpenTelemetry tracer provider,
    /// so the per-command and per-query span opened by
    /// <see cref="TracingBehavior{TMessage, TResponse}"/> is collected.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to configure.</param>
    /// <returns>The same <see cref="TracerProviderBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>AddTrellisBehaviors()</c> registers <see cref="TracingBehavior{TMessage, TResponse}"/>,
    /// so every command and query already *calls* <c>StartActivity</c>. That call only produces a
    /// live activity if a listener is subscribed to the source, so without this registration
    /// <c>StartActivity</c> returns <see langword="null"/> and the handler span is never collected.
    /// </para>
    /// <para>
    /// <strong>Why this matters more than a missing span.</strong> The absence is indistinguishable
    /// from success: a service with no handler spans looks exactly like a service in which nothing
    /// failed. The failure tags this span carries — <c>error.code</c> and <c>error.type</c>, the
    /// same values the HTTP response body reports — are the ones an operator reaches for when
    /// asking which rule rejected a request, so the configuration gap is discovered during an
    /// incident rather than before one.
    /// </para>
    /// <para>
    /// The registered source is <see cref="TracingBehavior{TMessage, TResponse}.ActivitySourceName"/>
    /// (<c>"Trellis.Mediator"</c>). Consumers may equivalently call <c>AddSource("Trellis.Mediator")</c>;
    /// this helper exists so the name does not have to be repeated as a string literal, matching
    /// <c>AddResultsInstrumentation()</c> in Trellis.Core and
    /// <c>AddPrimitiveValueObjectInstrumentation()</c> in Trellis.Primitives. The method is named for
    /// the Trellis pipeline specifically because it instruments Trellis behaviors rather than the
    /// underlying Mediator library.
    /// </para>
    /// <para>
    /// This is the pipeline-behavior altitude that the Trellis.Core tracing guidance recommends for
    /// high-throughput services, in preference to per-<c>Result</c>-operator spans: one span per
    /// request carrying the business message name and the failure code.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing
    ///         .AddAspNetCoreInstrumentation()
    ///         .AddTrellisMediatorInstrumentation()
    ///         .AddOtlpExporter());
    /// </code>
    /// </example>
    public static TracerProviderBuilder AddTrellisMediatorInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSource(MediatorTrace.ActivitySourceName);
    }
}
