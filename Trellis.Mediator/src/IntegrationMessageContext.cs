namespace Trellis.Mediator;

using System.Diagnostics;

/// <summary>
/// Carries the current inbound integration message's lineage across asynchronous handler work.
/// Applications can also supply an explicit business correlation id without deriving one from a trace.
/// </summary>
public static class IntegrationMessageContext
{
    private static readonly AsyncLocal<Frame?> s_current = new();

    /// <summary>The message currently being handled, if any; the direct cause of newly captured domain events.</summary>
    public static Guid? CurrentMessageId => ProcessingFrame?.MessageId;

    /// <summary>The inbound business correlation id, or an explicitly supplied application value.</summary>
    public static string? CorrelationId => ProcessingFrame?.InboundCorrelationId ?? FindExplicitCorrelationId();

    /// <summary>The explicitly supplied producer namespace, if any.</summary>
    public static string? MessageSource => FindMessageSource();

    /// <summary>The valid inbound W3C parent when no activity is available.</summary>
    public static string? TraceParent => ProcessingFrame?.TraceParent;

    /// <summary>The inbound W3C trace state when no activity is available.</summary>
    public static string? TraceState => ProcessingFrame?.TraceState;

    private static Frame? ProcessingFrame
    {
        get
        {
            for (var frame = s_current.Value; frame is { IsActive: true }; frame = frame.Previous)
            {
                if (frame.MessageId is not null)
                    return frame;
            }

            return null;
        }
    }

    private static string? FindExplicitCorrelationId()
    {
        for (var frame = s_current.Value; frame is { IsActive: true }; frame = frame.Previous)
        {
            if (frame.ExplicitCorrelationId is { } correlationId)
                return correlationId;
        }

        return null;
    }

    private static string? FindMessageSource()
    {
        for (var frame = s_current.Value; frame is { IsActive: true }; frame = frame.Previous)
        {
            if (frame.MessageSource is { } messageSource)
                return messageSource;
        }

        return null;
    }

    /// <summary>
    /// Supplies an application-owned business workflow id for the current async scope.
    /// A nonblank inbound <see cref="IntegrationEnvelope.CorrelationId"/> takes precedence.
    /// </summary>
    /// <param name="correlationId">The opaque business workflow id; must not be blank.</param>
    /// <param name="messageSource">Optional producer namespace to persist with captured events.</param>
    /// <returns>A scope that restores the previous correlation context on disposal.</returns>
    public static IDisposable BeginCorrelation(string correlationId, string? messageSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return Push(new Frame(
            s_current.Value,
            explicitCorrelationId: correlationId,
            messageSource: string.IsNullOrWhiteSpace(messageSource) ? null : messageSource));
    }

    /// <summary>Starts the async context for an inbound integration message.</summary>
    /// <param name="envelope">The received message and its optional lineage.</param>
    /// <returns>A scope that restores the previous message context on disposal.</returns>
    public static IDisposable BeginProcessing(IntegrationEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var validTrace = ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, isRemote: true, out _);
        return Push(new Frame(
            s_current.Value,
            messageId: envelope.MessageId,
            inboundCorrelationId: string.IsNullOrWhiteSpace(envelope.CorrelationId) ? null : envelope.CorrelationId,
            traceParent: validTrace ? envelope.TraceParent : null,
            traceState: validTrace ? envelope.TraceState : null));
    }

    private static Scope Push(Frame frame)
    {
        s_current.Value = frame;
        return new Scope(frame);
    }

    private sealed class Frame(
        Frame? previous,
        Guid? messageId = null,
        string? inboundCorrelationId = null,
        string? explicitCorrelationId = null,
        string? messageSource = null,
        string? traceParent = null,
        string? traceState = null)
    {
        private volatile bool _isActive = true;

        public Frame? Previous { get; } = previous;
        public Guid? MessageId { get; } = messageId;
        public string? InboundCorrelationId { get; } = inboundCorrelationId;
        public string? ExplicitCorrelationId { get; } = explicitCorrelationId;
        public string? MessageSource { get; } = messageSource;
        public string? TraceParent { get; } = traceParent;
        public string? TraceState { get; } = traceState;
        public bool IsActive => _isActive;

        public void Deactivate() => _isActive = false;
    }

    private sealed class Scope(Frame frame) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            if (!ReferenceEquals(s_current.Value, frame))
                throw new InvalidOperationException("Integration message scopes must be disposed in reverse order.");

            _disposed = true;
            frame.Deactivate();
            s_current.Value = frame.Previous;
        }
    }
}
