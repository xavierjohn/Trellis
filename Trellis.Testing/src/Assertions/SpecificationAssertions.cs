namespace Trellis.Testing;

using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

/// <summary>Extension methods to enable FluentAssertions on <see cref="Specification{T}"/>.</summary>
public static class SpecificationAssertionsExtensions
{
    /// <summary>Returns specification assertions.</summary>
    /// <typeparam name="T">The candidate type.</typeparam>
    /// <param name="specification">The specification under test.</param>
    /// <returns>Assertions for the specification.</returns>
    public static SpecificationAssertions<T> Should<T>(this Specification<T> specification) => new(specification);
}

/// <summary>Assertions that a <see cref="Specification{T}"/> accepts or rejects candidates, naming each offender.</summary>
/// <typeparam name="T">The candidate type.</typeparam>
public class SpecificationAssertions<T> : ReferenceTypeAssertions<Specification<T>, SpecificationAssertions<T>>
{
    /// <summary>Initializes a new instance of the <see cref="SpecificationAssertions{T}"/> class.</summary>
    /// <param name="subject">The specification under test.</param>
    public SpecificationAssertions(Specification<T> subject)
        : base(subject)
    {
    }

    /// <inheritdoc/>
    protected override string Identifier => "specification";

    /// <summary>Asserts the specification is satisfied by <paramref name="candidate"/>.</summary>
    /// <param name="candidate">The candidate that must satisfy the specification.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<SpecificationAssertions<T>> BeSatisfiedBy(T candidate, string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs) && !Subject.IsSatisfiedBy(candidate))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .FailWith("Expected {context:specification} to be satisfied by {0}{reason}, but it was not. Expression: {1}",
                    candidate, Subject.ToExpression().ToString());
        }

        return new AndConstraint<SpecificationAssertions<T>>(this);
    }

    /// <summary>Asserts the specification is not satisfied by <paramref name="candidate"/>.</summary>
    /// <param name="candidate">The candidate that must be rejected.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<SpecificationAssertions<T>> NotBeSatisfiedBy(T candidate, string because = "", params object[] becauseArgs)
    {
        if (RequireSubject(because, becauseArgs) && Subject.IsSatisfiedBy(candidate))
        {
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .FailWith("Expected {context:specification} not to be satisfied by {0}{reason}, but it was. Expression: {1}",
                    candidate, Subject.ToExpression().ToString());
        }

        return new AndConstraint<SpecificationAssertions<T>>(this);
    }

    /// <summary>Asserts every candidate satisfies the specification, reporting each one that does not.</summary>
    /// <param name="candidates">The candidates that must all be accepted.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<SpecificationAssertions<T>> BeSatisfiedByAll(IEnumerable<T> candidates, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (RequireSubject(because, becauseArgs))
        {
            var rejected = candidates.Where(candidate => !Subject.IsSatisfiedBy(candidate)).ToArray();
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(rejected.Length == 0)
                .FailWith("Expected {context:specification} to be satisfied by every candidate{reason}, but it rejected: {0}.",
                    string.Join(", ", rejected));
        }

        return new AndConstraint<SpecificationAssertions<T>>(this);
    }

    /// <summary>Asserts no candidate satisfies the specification, reporting each one that does.</summary>
    /// <param name="candidates">The candidates that must all be rejected.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<SpecificationAssertions<T>> BeSatisfiedByNone(IEnumerable<T> candidates, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (RequireSubject(because, becauseArgs))
        {
            var accepted = candidates.Where(Subject.IsSatisfiedBy).ToArray();
            Execute.Assertion
                .BecauseOf(because, becauseArgs)
                .ForCondition(accepted.Length == 0)
                .FailWith("Expected {context:specification} to be satisfied by no candidate{reason}, but it accepted: {0}.",
                    string.Join(", ", accepted));
        }

        return new AndConstraint<SpecificationAssertions<T>>(this);
    }

    /// <summary>Asserts the candidates the specification accepts are exactly <paramref name="expected"/>, in candidate order.</summary>
    /// <param name="candidates">The candidates to filter.</param>
    /// <param name="expected">The candidates that must be accepted, in order.</param>
    /// <param name="because">A formatted explanation of the assertion.</param>
    /// <param name="becauseArgs">Arguments for the explanation.</param>
    /// <returns>A constraint for chaining.</returns>
    public AndConstraint<SpecificationAssertions<T>> SelectExactly(
        IEnumerable<T> candidates, IEnumerable<T> expected, string because = "", params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(expected);
        if (RequireSubject(because, becauseArgs))
            candidates.Where(Subject.IsSatisfiedBy).Should().Equal(expected, because, becauseArgs);

        return new AndConstraint<SpecificationAssertions<T>>(this);
    }

    private bool RequireSubject(string because, object[] becauseArgs) =>
        Execute.Assertion
            .BecauseOf(because, becauseArgs)
            .ForCondition(Subject is not null)
            .FailWith("Expected {context:specification} to not be <null>{reason}.");
}