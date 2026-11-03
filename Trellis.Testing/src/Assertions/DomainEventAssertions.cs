namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Collections;
using FluentAssertions.Execution;

/// <summary>
/// Assertions over a sequence of <see cref="IDomainEvent"/> values, such as
/// <c>aggregate.UncommittedEvents()</c> or <c>FakeRepository.PublishedEvents</c>.
/// </summary>
/// <remarks>
/// These extend the collection assertions returned by <c>events.Should()</c> rather than adding a new
/// <c>Should()</c> entry point, so existing assertions on aggregates keep binding to FluentAssertions.
/// </remarks>
public static class DomainEventAssertionsExtensions
{
    /// <summary>Asserts exactly one event of <typeparamref name="TEvent"/> (optionally matching) was raised.</summary>
    /// <typeparam name="TEvent">The expected event type.</typeparam>
    /// <param name="assertions">The event collection assertions.</param>
    /// <param name="predicate">Optional filter; only events that match count.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint whose <c>Which</c> is the single matching event.</returns>
    public static AndWhichConstraint<GenericCollectionAssertions<IDomainEvent>, TEvent> HaveRaised<TEvent>(
        this GenericCollectionAssertions<IDomainEvent> assertions,
        Func<TEvent, bool>? predicate = null,
        string because = "",
        params object[] becauseArgs)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(assertions);
        var raised = RaisedEvents(assertions);
        var matches = raised.OfType<TEvent>().Where(evt => predicate?.Invoke(evt) ?? true).ToArray();

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(matches.Length == 1)
            .FailWith(
                "Expected {context:domain events} to contain exactly one {0}{reason}, but found {1}. Raised: {2}.",
                typeof(TEvent).Name, matches.Length, Describe(raised));

        return new AndWhichConstraint<GenericCollectionAssertions<IDomainEvent>, TEvent>(
            assertions, matches.FirstOrDefault()!);
    }

    /// <summary>Asserts no event of <typeparamref name="TEvent"/> was raised.</summary>
    /// <typeparam name="TEvent">The event type that must be absent.</typeparam>
    /// <param name="assertions">The event collection assertions.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public static AndConstraint<GenericCollectionAssertions<IDomainEvent>> NotHaveRaised<TEvent>(
        this GenericCollectionAssertions<IDomainEvent> assertions,
        string because = "",
        params object[] becauseArgs)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(assertions);
        var raised = RaisedEvents(assertions);

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(!raised.OfType<TEvent>().Any())
            .FailWith(
                "Expected {context:domain events} not to contain a {0}{reason}, but it did. Raised: {1}.",
                typeof(TEvent).Name, Describe(raised));

        return new AndConstraint<GenericCollectionAssertions<IDomainEvent>>(assertions);
    }

    /// <summary>Asserts exactly <paramref name="expected"/> events of <typeparamref name="TEvent"/> were raised.</summary>
    /// <typeparam name="TEvent">The event type to count.</typeparam>
    /// <param name="assertions">The event collection assertions.</param>
    /// <param name="expected">The expected count.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public static AndConstraint<GenericCollectionAssertions<IDomainEvent>> HaveRaisedCount<TEvent>(
        this GenericCollectionAssertions<IDomainEvent> assertions,
        int expected,
        string because = "",
        params object[] becauseArgs)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(assertions);
        var raised = RaisedEvents(assertions);
        var actual = raised.OfType<TEvent>().Count();

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(actual == expected)
            .FailWith(
                "Expected {context:domain events} to contain {0} {1} event(s){reason}, but found {2}. Raised: {3}.",
                expected, typeof(TEvent).Name, actual, Describe(raised));

        return new AndConstraint<GenericCollectionAssertions<IDomainEvent>>(assertions);
    }

    /// <summary>Asserts the raised events have exactly these runtime types, in this order.</summary>
    /// <param name="assertions">The event collection assertions.</param>
    /// <param name="eventTypes">The expected runtime event types in order.</param>
    /// <returns>A constraint for chaining.</returns>
    public static AndConstraint<GenericCollectionAssertions<IDomainEvent>> HaveRaisedExactlyInOrder(
        this GenericCollectionAssertions<IDomainEvent> assertions,
        params Type[] eventTypes)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        ArgumentNullException.ThrowIfNull(eventTypes);
        var raised = RaisedEvents(assertions);
        var actualTypes = raised.Select(evt => evt.GetType());

        Execute.Assertion
            .ForCondition(actualTypes.SequenceEqual(eventTypes))
            .FailWith(
                "Expected {context:domain events} to be [{0}] in order, but found [{1}].",
                string.Join(", ", eventTypes.Select(type => type.Name)), Describe(raised));

        return new AndConstraint<GenericCollectionAssertions<IDomainEvent>>(assertions);
    }

    private static IDomainEvent[] RaisedEvents(GenericCollectionAssertions<IDomainEvent> assertions)
    {
        Execute.Assertion
            .ForCondition(assertions.Subject is not null)
            .FailWith("Expected {context:domain events} to not be <null>.");

        return assertions.Subject?.ToArray() ?? [];
    }

    private static string Describe(IEnumerable<IDomainEvent> events)
    {
        var names = events.Select(evt => evt.GetType().Name).ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }
}