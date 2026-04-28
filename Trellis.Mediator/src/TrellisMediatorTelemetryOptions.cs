namespace Trellis.Mediator;

/// <summary>
/// Operator-tunable redaction settings for the Trellis mediator pipeline's logging and
/// tracing behaviors. Resolved from DI; if not registered, the behaviors fall back to
/// the safe-by-default values exposed by this type's parameterless constructor.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LoggingBehavior{TMessage,TResponse}"/> and
/// <see cref="TracingBehavior{TMessage,TResponse}"/> emit the failed-result's
/// <see cref="Trellis.Error.Code"/> and stable type name unconditionally — those values
/// are operator-defined identifiers with no PII risk. The richer
/// <see cref="Trellis.Error.Detail"/> string, by contrast, is frequently composed from
/// user input or domain payloads (e.g. an order id, an email address, a free-text
/// validation message) and must not leak into telemetry pipelines, log aggregation, or
/// distributed traces by default.
/// </para>
/// <para>
/// Set <see cref="IncludeErrorDetail"/> to <c>true</c> in development or in environments
/// where you have explicitly verified that no PII can flow through any
/// <see cref="Trellis.Error"/> instance.
/// </para>
/// </remarks>
public sealed class TrellisMediatorTelemetryOptions
{
    /// <summary>
    /// When <c>true</c>, the logging and tracing behaviors include
    /// <see cref="Trellis.Error.Detail"/> in their emitted message / status description.
    /// Defaults to <c>false</c> (Detail is redacted; only <see cref="Trellis.Error.Code"/>
    /// and the stable type name are emitted).
    /// </summary>
    public bool IncludeErrorDetail { get; set; }
}