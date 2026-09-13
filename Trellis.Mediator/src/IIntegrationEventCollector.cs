namespace Trellis.Mediator;

using System.Collections.Generic;

/// <summary>
/// Collects <see cref="IIntegrationEvent"/> instances produced while a domain-event handler (the
/// <em>translator</em>) handles a domain event, so the transactional outbox can capture them durably and
/// the relay can publish them after the producing work is committed. This is the translation seam: a
/// domain-event handler observes a domain event and adds the integration events that describe the same
/// fact in contract terms.
/// </summary>
/// <remarks>
/// <para>
/// Register the collector as scoped (the Trellis registration helpers do this for you). Translators add
/// to it; the outbox relay drains it after dispatching each domain event. The relay is the only drain
/// point - integration events added outside a domain-event handler dispatched by the relay (for example
/// in a command handler) are rejected with <see cref="InvalidOperationException"/>. The collector is
/// only a hand-off buffer: durable storage and publishing are the outbox's job.
/// </para>
/// </remarks>
public interface IIntegrationEventCollector
{
    /// <summary>
    /// Opens a translation lease. For relay infrastructure only: surround domain-event publishing
    /// and the subsequent drain with this lease. Disposal rejects late additions and discards any
    /// undrained events; it does not persist or publish them. Nested leases are not supported.
    /// </summary>
    /// <returns>The lease that limits collection to this relay translation invocation.</returns>
    IDisposable BeginTranslation();

    /// <summary>
    /// Enqueues an integration event for durable capture and later publishing.
    /// </summary>
    /// <param name="integrationEvent">The integration event to publish once the producing work is committed.</param>
    /// <exception cref="InvalidOperationException">No relay translation lease is active in this execution context.</exception>
    void Add(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Returns the integration events collected so far and clears the buffer, so a subsequent drain in
    /// the same scope does not observe them again. The outbox calls this after a domain event has been
    /// dispatched to capture whatever its translators produced.
    /// </summary>
    /// <returns>The collected integration events in insertion order; empty when none were added.</returns>
    IReadOnlyList<IIntegrationEvent> DrainPending();
}