namespace Trellis;

using OpenTelemetry.Metrics;

/// <summary>
/// Extension methods for collecting Trellis validation metrics with OpenTelemetry.
/// </summary>
public static class ValidationMeterProviderBuilderExtensions
{
    /// <summary>
    /// Subscribes the meter provider to Trellis validation instruments.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> to configure.</param>
    /// <returns>The same <see cref="MeterProviderBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Trellis counts a violation as each validation failure is created, but a
    /// <see cref="System.Diagnostics.Metrics.Counter{T}"/> with no listener records nothing. Until
    /// this is called the instrument is inert, and its absence is silent — there is no error and no
    /// warning, only a metric that never appears in the backend, which reads exactly like a rule
    /// that never fires. That ambiguity is the thing the counter exists to remove, so registering
    /// it is not optional if the dead-rule question is the reason you enabled it.
    /// </para>
    /// <para>
    /// Equivalent to <c>AddMeter("Trellis.Validation")</c>; prefer this so the name is read from
    /// <see cref="ValidationMetrics.MeterName"/> and cannot drift from the meter Trellis publishes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(metrics => metrics
    ///         .AddTrellisValidationInstrumentation()
    ///         .AddOtlpExporter());
    /// </code>
    /// </example>
    public static MeterProviderBuilder AddTrellisValidationInstrumentation(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddMeter(ValidationMetrics.MeterName);
    }
}
