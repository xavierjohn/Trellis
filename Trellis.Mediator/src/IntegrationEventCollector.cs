namespace Trellis.Mediator;

using System.Collections.Generic;

/// <summary>
/// Default scoped <see cref="IIntegrationEventCollector"/> - an ordered, per-scope buffer of integration
/// events awaiting durable capture by the outbox.
/// </summary>
internal sealed class IntegrationEventCollector : IIntegrationEventCollector
{
    private readonly AsyncLocal<TranslationScope?> _current = new();

    public IDisposable BeginTranslation()
    {
        if (_current.Value is { Active: true })
            throw new InvalidOperationException("A relay translation lease is already active.");

        var scope = new TranslationScope(this);
        _current.Value = scope;
        return scope;
    }

    public void Add(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var scope = _current.Value;
        if (scope is null)
            throw InactiveTranslation();
        lock (scope.Pending)
        {
            if (!scope.Active)
                throw InactiveTranslation();
            scope.Pending.Add(integrationEvent);
        }
    }

    public IReadOnlyList<IIntegrationEvent> DrainPending()
    {
        var scope = _current.Value;
        if (scope is null)
            return [];

        lock (scope.Pending)
        {
            var drained = scope.Pending.ToArray();
            scope.Pending.Clear();
            return drained;
        }
    }

    private static InvalidOperationException InactiveTranslation() => new(
        "Integration events can only be added during an active outbox relay translation. " +
        "Command handlers and in-process domain-event dispatch cannot durably capture collector events.");

    private sealed class TranslationScope(IntegrationEventCollector owner) : IDisposable
    {
        internal List<IIntegrationEvent> Pending { get; } = [];
        internal bool Active { get; private set; } = true;

        public void Dispose()
        {
            lock (Pending)
            {
                Active = false;
                Pending.Clear();
            }

            if (ReferenceEquals(owner._current.Value, this))
                owner._current.Value = null;
        }
    }
}