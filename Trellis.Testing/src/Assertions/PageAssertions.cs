namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>Extension methods to enable FluentAssertions on <see cref="Page{T}"/>.</summary>
public static class PageAssertionsExtensions
{
    /// <summary>Returns page assertions.</summary>
    /// <typeparam name="T">The page item type.</typeparam>
    /// <param name="page">The page under test.</param>
    /// <returns>Assertions for the page.</returns>
    public static PageAssertions<T> Should<T>(this Page<T> page) => new(page);
}

/// <summary>Assertions for a <see cref="Page{T}"/>: items, continuation cursors and applied limits.</summary>
/// <typeparam name="T">The page item type.</typeparam>
public class PageAssertions<T> : ReferenceTypeAssertions<Page<T>, PageAssertions<T>>
{
    /// <summary>Initializes a new instance of the <see cref="PageAssertions{T}"/> class.</summary>
    /// <param name="subject">The page under test.</param>
    public PageAssertions(Page<T> subject)
        : base(subject)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "page";

    /// <summary>Asserts the page equals <paramref name="expected"/> by record equality (items, cursors, limits).</summary>
    /// <param name="expected">The expected page.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> Be(Page<T> expected, string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Equals(Subject, expected))
            .FailWith("Expected {context:page} to be {0}{reason}, but found {1}.", expected, Subject);
        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the page items equal <paramref name="expected"/> in order.</summary>
    /// <param name="expected">The expected items.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> HaveItems(IEnumerable<T> expected, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (RequireSubject(because, becauseArgs))
            Subject.Items.Should().Equal(expected, because, becauseArgs);

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the page delivered <paramref name="expected"/> items.</summary>
    /// <param name="expected">The expected delivered count.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> HaveCount(int expected, string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.DeliveredCount == expected)
                .FailWith("Expected {context:page} to have {0} item(s){reason}, but found {1}.", expected, Subject.DeliveredCount);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the page delivered no items.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> BeEmpty(string because = "", params object[] becauseArgs) =>
        HaveCount(0, because, becauseArgs);

    /// <summary>Asserts the page has a next cursor.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint whose <c>Which</c> is the next cursor.</returns>
    public AndWhichConstraint<PageAssertions<T>, Cursor> HaveNextCursor(string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Next is not null)
                .FailWith("Expected {context:page} to have a next cursor{reason}, but it was <null>.");
        }

        return new AndWhichConstraint<PageAssertions<T>, Cursor>(this, Subject?.Next!);
    }

    /// <summary>Asserts the page has no next cursor.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> HaveNoNextCursor(string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Next is null)
                .FailWith("Expected {context:page} to have no next cursor{reason}, but found cursor {0}.", Subject.Next?.Token);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the page has a previous cursor.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint whose <c>Which</c> is the previous cursor.</returns>
    public AndWhichConstraint<PageAssertions<T>, Cursor> HavePreviousCursor(string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Previous is not null)
                .FailWith("Expected {context:page} to have a previous cursor{reason}, but it was <null>.");
        }

        return new AndWhichConstraint<PageAssertions<T>, Cursor>(this, Subject?.Previous!);
    }

    /// <summary>Asserts the page has no previous cursor.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> HaveNoPreviousCursor(string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.Previous is null)
                .FailWith("Expected {context:page} to have no previous cursor{reason}, but found cursor {0}.", Subject.Previous?.Token);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts there is no next cursor, so no further page follows.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> BeLastPage(string because = "", params object[] becauseArgs) =>
        HaveNoNextCursor(because, becauseArgs);

    /// <summary>Asserts there is no previous cursor, so no earlier page precedes.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> BeFirstPage(string because = "", params object[] becauseArgs) =>
        HaveNoPreviousCursor(because, becauseArgs);

    /// <summary>Asserts the limit the caller requested.</summary>
    /// <param name="expected">The expected requested limit.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> HaveRequestedLimit(int expected, string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.RequestedLimit == expected)
                .FailWith("Expected {context:page} to have requested limit {0}{reason}, but found {1}.", expected, Subject.RequestedLimit);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the limit the server actually applied.</summary>
    /// <param name="expected">The expected applied limit.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> HaveAppliedLimit(int expected, string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.AppliedLimit == expected)
                .FailWith("Expected {context:page} to have applied limit {0}{reason}, but found {1}.", expected, Subject.AppliedLimit);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the requested limit was reduced to a smaller applied limit.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> BeCapped(string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(Subject.WasCapped)
                .FailWith("Expected {context:page} to be capped{reason}, but requested limit {0} was applied as {1}.", Subject.RequestedLimit, Subject.AppliedLimit);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    /// <summary>Asserts the requested limit was applied unchanged.</summary>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<PageAssertions<T>> NotBeCapped(string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(!Subject.WasCapped)
                .FailWith("Expected {context:page} not to be capped{reason}, but requested limit {0} was capped to {1}.", Subject.RequestedLimit, Subject.AppliedLimit);
        }

        return new AndConstraint<PageAssertions<T>>(this);
    }

    private bool RequireSubject(string because, object[] becauseArgs) =>
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject is not null)
            .FailWith("Expected {context:page} to not be <null>{reason}.");
}