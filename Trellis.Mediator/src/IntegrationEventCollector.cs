namespace Trellis.Mediator;

using System.Collections.Generic;

/// <summary>
/// Default scoped <see cref="IIntegrationEventCollector"/> - an ordered, per-scope buffer of integration
/// events awaiting durable capture by the outbox.
/// </summary>
internal sealed class IntegrationEventCollector : IIntegrationEventCollector
{
    private readonly List<IIntegrationEvent> _pending = [];

    public void Add(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        _pending.Add(integrationEvent);
    }

    public IReadOnlyList<IIntegrationEvent> DrainPending()
    {
        if (_pending.Count == 0)
            return [];

        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }
}
