namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

internal sealed class OutboxRelayObservations<TContext>(TimeProvider timeProvider)
    where TContext : DbContext
{
    private readonly Lock _gate = new();
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastPositiveAt;
    private DateTimeOffset? _lastDeliveryAt;
    private long _deliveredRows;

    public void Start()
    {
        lock (_gate)
        {
            _startedAt = timeProvider.GetUtcNow();
            _lastPositiveAt = null;
            _lastDeliveryAt = null;
            _deliveredRows = 0;
        }
    }

    public void RecordPositiveObservation(int deliveredRows)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            _lastPositiveAt = now;
            if (deliveredRows > 0)
            {
                _lastDeliveryAt = now;
                _deliveredRows += deliveredRows;
            }
        }
    }

    public OutboxRelayEvidence Read()
    {
        lock (_gate)
            return new(timeProvider.GetUtcNow(), _startedAt, _lastPositiveAt, _lastDeliveryAt, _deliveredRows);
    }
}

internal sealed record OutboxRelayEvidence(
    DateTimeOffset SampledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastPositiveAt,
    DateTimeOffset? LastDeliveryAt,
    long DeliveredRows);