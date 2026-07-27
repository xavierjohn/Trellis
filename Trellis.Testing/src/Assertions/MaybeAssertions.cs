namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>
/// Extension methods to enable FluentAssertions on Maybe types.
/// </summary>
public static class MaybeAssertionsExtensions
{
    /// <summary>
    /// Returns an assertions object for fluent assertions on Maybe.
    /// </summary>
    public static MaybeAssertions<T> Should<T>(this Maybe<T> maybe) where T : notnull => new MaybeAssertions<T>(maybe);
}

/// <summary>
/// Contains assertion methods for Maybe types.
/// </summary>
public class MaybeAssertions<T> : ReferenceTypeAssertions<Maybe<T>, MaybeAssertions<T>> where T : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MaybeAssertions{T}"/> class.
    /// </summary>
    public MaybeAssertions(Maybe<T> maybe)
        : base(maybe)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "maybe";

    /// <summary>
    /// Asserts that the Maybe has a value.
    /// </summary>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndWhichConstraint<MaybeAssertions<T>, T> HaveValue(
        string because = "",
        params object[] becauseArgs)
    {
        var hasValue = Subject.TryGetValue(out var value);
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(hasValue)
            .FailWith("Expected {context:maybe} to have a value{reason}, but it was None");

        return new AndWhichConstraint<MaybeAssertions<T>, T>(this, hasValue ? value! : default!);
    }

    /// <summary>
    /// Asserts that the Maybe has no value (is None).
    /// </summary>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<MaybeAssertions<T>> BeNone(
        string because = "",
        params object[] becauseArgs)
    {
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject.HasNoValue)
            .FailWith("Expected {context:maybe} to be None{reason}, but it had value: {0}",
                Subject.HasValue ? Subject.Value : default);

        return new AndConstraint<MaybeAssertions<T>>(this);
    }

    /// <summary>
    /// Asserts that the Maybe has a value equal to the expected value.
    /// </summary>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<MaybeAssertions<T>> HaveValueEqualTo(
        T expectedValue,
        string because = "",
        params object[] becauseArgs)
    {
        HaveValue(because, becauseArgs);

        if (Subject.TryGetValue(out var actualValue))
            actualValue.Should().Be(expectedValue, because, becauseArgs);

        return new AndConstraint<MaybeAssertions<T>>(this);
    }

    /// <summary>
    /// Asserts that the Maybe has a value that satisfies the given predicate.
    /// </summary>
    /// <param name="predicate">The predicate the value should satisfy.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<MaybeAssertions<T>> HaveValueMatching(
        Func<T, bool> predicate,
        string because = "",
        params object[] becauseArgs)
    {
        HaveValue(because, becauseArgs);

        if (!Subject.TryGetValue(out var actualValue))
            return new AndConstraint<MaybeAssertions<T>>(this);

        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(predicate(actualValue))
            .FailWith("Expected {context:maybe} value to match predicate{reason}, but it did not. Value: {0}",
                actualValue);

        return new AndConstraint<MaybeAssertions<T>>(this);
    }

    /// <summary>
    /// Asserts that the Maybe has a value that is equivalent to the expected value using structural comparison.
    /// </summary>
    /// <param name="expectedValue">The expected value.</param>
    /// <param name="because">
    /// A formatted phrase explaining why the assertion is needed.
    /// </param>
    /// <param name="becauseArgs">
    /// Zero or more objects to format using the placeholders in <paramref name="because" />.
    /// </param>
    public AndConstraint<MaybeAssertions<T>> HaveValueEquivalentTo(
        T expectedValue,
        string because = "",
        params object[] becauseArgs)
    {
        HaveValue(because, becauseArgs);

        if (Subject.TryGetValue(out var actualValue))
            actualValue.Should().BeEquivalentTo(expectedValue, because, becauseArgs);

        return new AndConstraint<MaybeAssertions<T>>(this);
    }
}