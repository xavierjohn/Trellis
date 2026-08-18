namespace Trellis.Testing.Worker;

using System.Threading;
using System.Threading.Tasks;
using Trellis.Mediator;

/// <summary>
/// Open-generic <see cref="IDomainEventHandler{TEvent}"/> implementation that forwards every
/// dispatched event to the shared <see cref="DomainEventCapture"/>. Registered by
/// <see cref="WorkerHarness{TWorker}"/> as
/// <c>(typeof(IDomainEventHandler&lt;&gt;), typeof(DomainEventCaptureHandler&lt;&gt;))</c>
/// so Microsoft.Extensions.DependencyInjection closes the open generic against every concrete
/// event type the mediator publisher resolves.
/// </summary>
/// <remarks>
/// <para>
/// The framework's <c>MediatorDomainEventPublisher</c> matches handlers by the event's exact
/// runtime type — a single <c>IDomainEventHandler&lt;IDomainEvent&gt;</c> registration would
/// never receive concrete events. Registering the capture handler as an open generic side-steps
/// that constraint without forcing the test author to enumerate event types up front.
/// </para>
/// <para>
/// The capture handler runs alongside any production handler the user registered for the same
/// event; it appends to the capture and returns synchronously without throwing. The publisher's
/// best-effort semantics still apply to production handlers — the capture handler never
/// participates in their failure paths.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The concrete domain event type closed by DI at resolve time.</typeparam>
internal sealed class DomainEventCaptureHandler<TEvent>(DomainEventCapture capture) : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    public ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
    {
        capture.Record(domainEvent);
        return ValueTask.CompletedTask;
    }
}