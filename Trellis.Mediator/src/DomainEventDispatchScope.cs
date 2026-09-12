namespace Trellis.Mediator;

internal sealed class DomainEventDispatchScope : IDisposable
{
    private static readonly AsyncLocal<DomainEventDispatchScope?> s_current = new();
    private readonly DomainEventDispatchScope? _parent;
    private readonly List<(IAggregate Aggregate, IDomainEventPublisher Publisher)> _pending = [];
    private IUnitOfWork? _unitOfWork;
    private bool _ownsCommit;
    private bool _committed;
    private bool _active = true;
    private bool _publishing;

    private DomainEventDispatchScope()
    {
        _parent = s_current.Value;
        s_current.Value = this;
    }

    internal static DomainEventDispatchScope Enter() => new();

    internal static DomainEventDispatchScope? ObserveTransaction(IUnitOfWork unitOfWork, bool ownsCommit)
    {
        var current = s_current.Value;
        if (current is null || !current._active || current._publishing || current._unitOfWork is not null)
            return null;
        current._unitOfWork = unitOfWork;
        current._ownsCommit = ownsCommit;
        return current;
    }

    internal bool CanReadCommittedAggregates => _unitOfWork is null || (_ownsCommit && _committed);

    internal void RecordCommit() => _committed = _ownsCommit;

    internal void Add(IAggregate aggregate, IDomainEventPublisher publisher)
    {
        if (!_pending.Any(item => ReferenceEquals(item.Aggregate, aggregate)))
            _pending.Add((aggregate, publisher));
    }

    internal async ValueTask CompleteAsync()
    {
        if (_unitOfWork is not null && !_ownsCommit)
        {
            for (var owner = _parent; owner is not null; owner = owner._parent)
            {
                if (!owner._active || owner._publishing || !owner._ownsCommit
                    || !ReferenceEquals(owner._unitOfWork, _unitOfWork))
                    continue;
                foreach (var (aggregate, publisher) in _pending)
                    owner.Add(aggregate, publisher);
                return;
            }

            throw new InvalidOperationException(
                "Domain-event dispatch cannot complete inside a manually owned unit-of-work scope. " +
                "Use the owning command pipeline, or disable automatic dispatch and call " +
                "DispatchAggregateEventsAsync after the manual owning commit succeeds.");
        }

        if (_unitOfWork is not null && !_committed)
            return;

        var snapshots = _pending.Select(item =>
            (item.Aggregate, item.Publisher, Events: item.Aggregate.UncommittedEvents().ToArray())).ToArray();
        _publishing = true;
        try
        {
            foreach (var (_, publisher, events) in snapshots)
                foreach (var domainEvent in events)
                    await publisher.PublishAsync(domainEvent, CancellationToken.None).ConfigureAwait(false);

            List<CascadeOffender>? offenders = null;
            foreach (var (aggregate, _, events) in snapshots)
            {
                if (DomainEventCascadeDetector.Detect(aggregate, events) is { } offender)
                    (offenders ??= []).Add(offender);
            }

            if (offenders is not null)
                throw new DomainEventHandlerCascadedException(offenders);

            foreach (var (aggregate, _, _) in snapshots)
                aggregate.AcceptChanges();
        }
        finally
        {
            _publishing = false;
        }
    }

    public void Dispose()
    {
        _active = false;
        _pending.Clear();
        s_current.Value = _parent;
    }
}
