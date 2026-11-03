namespace Trellis.Testing.Tests;

using System.Linq.Expressions;
using Xunit.Sdk;

public class SpecificationAssertionsTests
{
    private sealed class EvenSpecification : Specification<int>
    {
        public override Expression<Func<int, bool>> ToExpression() => value => value % 2 == 0;
    }

    private sealed class PositiveSpecification : Specification<int>
    {
        public override Expression<Func<int, bool>> ToExpression() => value => value > 0;
    }

    [Fact]
    public void BeSatisfiedBy_Satisfying_Passes() =>
        new EvenSpecification().Should().BeSatisfiedBy(4);

    [Fact]
    public void BeSatisfiedBy_NotSatisfying_FailsNamingTheCandidate()
    {
        var act = () => new EvenSpecification().Should().BeSatisfiedBy(3);

        act.Should().Throw<XunitException>().WithMessage("*satisfied by 3*");
    }

    [Fact]
    public void NotBeSatisfiedBy_NotSatisfying_Passes_And_Satisfying_Fails()
    {
        new EvenSpecification().Should().NotBeSatisfiedBy(3);
        var act = () => new EvenSpecification().Should().NotBeSatisfiedBy(4);

        act.Should().Throw<XunitException>().WithMessage("*not*satisfied*4*");
    }

    [Fact]
    public void BeSatisfiedByAll_ReportsEveryFailingCandidate()
    {
        new EvenSpecification().Should().BeSatisfiedByAll([2, 4, 6]);
        var act = () => new EvenSpecification().Should().BeSatisfiedByAll([2, 3, 5]);

        act.Should().Throw<XunitException>().WithMessage("*3*5*");
    }

    [Fact]
    public void BeSatisfiedByNone_ReportsEveryUnexpectedMatch()
    {
        new EvenSpecification().Should().BeSatisfiedByNone([1, 3, 5]);
        var act = () => new EvenSpecification().Should().BeSatisfiedByNone([1, 2, 4]);

        act.Should().Throw<XunitException>().WithMessage("*2*4*");
    }

    [Fact]
    public void SelectExactly_Matches_AreCheckedInCandidateOrder()
    {
        new EvenSpecification().Should().SelectExactly([1, 2, 3, 4], expected: [2, 4]);
        var act = () => new EvenSpecification().Should().SelectExactly([1, 2, 3, 4], expected: [4, 2]);

        act.Should().Throw<XunitException>();
    }

    [Fact]
    public void Composed_Specifications_AreAssertedLikeAnyOther()
    {
        var evenAndPositive = new EvenSpecification().And(new PositiveSpecification());
        var notEven = new EvenSpecification().Not();

        evenAndPositive.Should().BeSatisfiedBy(2).And.NotBeSatisfiedBy(-2).And.NotBeSatisfiedBy(3);
        notEven.Should().BeSatisfiedBy(3);
    }

    [Fact]
    public void NullCandidates_Throw()
    {
        var act = () => new EvenSpecification().Should().BeSatisfiedByAll(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("candidates");
    }

    private sealed class CountingSpecification : Specification<int>
    {
        public int ExpressionCalls { get; private set; }

        public override Expression<Func<int, bool>> ToExpression()
        {
            ExpressionCalls++;
            return value => value > 0;
        }
    }

    [Fact]
    public void BeSatisfiedBy_Passing_DoesNotBuildFailureText()
    {
        var specification = new CountingSpecification();

        specification.Should().BeSatisfiedBy(1).And.NotBeSatisfiedBy(-1);

        specification.ExpressionCalls.Should().Be(1);
    }
}