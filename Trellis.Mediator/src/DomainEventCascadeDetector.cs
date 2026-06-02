namespace Trellis.Mediator;

internal static class DomainEventCascadeDetector
{
    public static CascadeOffender? Detect(IAggregate aggregate, IDomainEvent[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(snapshot);

        var after = aggregate.UncommittedEvents();
        var matchingPrefixLength = GetMatchingPrefixLength(after, snapshot);
        if (after.Count == snapshot.Length && matchingPrefixLength == snapshot.Length)
            return null;

        return new CascadeOffender(aggregate.GetType(), GetEventTypeNames(after, matchingPrefixLength));
    }

    private static int GetMatchingPrefixLength(IReadOnlyList<IDomainEvent> after, IDomainEvent[] snapshot)
    {
        var max = Math.Min(after.Count, snapshot.Length);
        for (var i = 0; i < max; i++)
        {
            if (!ReferenceEquals(after[i], snapshot[i]))
                return i;
        }

        return max;
    }

    private static string[] GetEventTypeNames(IReadOnlyList<IDomainEvent> events, int startIndex)
    {
        if (startIndex >= events.Count)
            return [];

        var names = new string[events.Count - startIndex];
        for (var i = startIndex; i < events.Count; i++)
        {
            var eventType = events[i].GetType();
            names[i - startIndex] = eventType.FullName ?? eventType.Name;
        }

        return names;
    }
}