namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class PageAssertionsTests
{
    private static Page<int> Make(int[] items, Cursor? next = null, Cursor? previous = null, int requested = 10, int applied = 10) =>
        new(items, next, previous, requested, applied);

    [Fact]
    public void HaveItems_SameSequence_Passes() =>
        Make([1, 2, 3]).Should().HaveItems([1, 2, 3]);

    [Fact]
    public void HaveItems_DifferentOrder_Fails()
    {
        var act = () => Make([1, 2, 3]).Should().HaveItems([3, 2, 1]);

        act.Should().Throw<XunitException>();
    }

    [Fact]
    public void HaveCount_UsesDeliveredCount()
    {
        Make([1, 2]).Should().HaveCount(2);
        var act = () => Make([1, 2]).Should().HaveCount(3);

        act.Should().Throw<XunitException>().WithMessage("*3*2*");
    }

    [Fact]
    public void BeEmpty_EmptyPage_Passes_And_NonEmptyFails()
    {
        Page.Empty<int>(10, 10).Should().BeEmpty();
        var act = () => Make([1]).Should().BeEmpty();

        act.Should().Throw<XunitException>();
    }

    [Fact]
    public void HaveNextCursor_Present_ReturnsTheCursor()
    {
        var next = new Cursor("next-token");

        var constraint = Make([1], next: next).Should().HaveNextCursor();

        constraint.Which.Should().BeSameAs(next);
    }

    [Fact]
    public void HaveNextCursor_Absent_Fails_And_HaveNoNextCursor_Passes()
    {
        var page = Make([1]);

        var act = () => page.Should().HaveNextCursor();

        act.Should().Throw<XunitException>().WithMessage("*next cursor*");
        page.Should().HaveNoNextCursor();
    }

    [Fact]
    public void HaveNoNextCursor_Present_FailsShowingToken()
    {
        var act = () => Make([1], next: new Cursor("abc")).Should().HaveNoNextCursor();

        act.Should().Throw<XunitException>().WithMessage("*abc*");
    }

    [Fact]
    public void PreviousCursor_Assertions_MirrorNext()
    {
        var previous = new Cursor("prev");
        var page = Make([1], previous: previous);

        page.Should().HavePreviousCursor().Which.Should().BeSameAs(previous);
        Make([1]).Should().HaveNoPreviousCursor();
        var act = () => page.Should().HaveNoPreviousCursor();
        act.Should().Throw<XunitException>().WithMessage("*prev*");
    }

    [Fact]
    public void BeLastPage_And_BeFirstPage_UseCursorAbsence()
    {
        Make([1], previous: new Cursor("p")).Should().BeLastPage();
        Make([1], next: new Cursor("n")).Should().BeFirstPage();

        var notLast = () => Make([1], next: new Cursor("n")).Should().BeLastPage();
        var notFirst = () => Make([1], previous: new Cursor("p")).Should().BeFirstPage();

        notLast.Should().Throw<XunitException>();
        notFirst.Should().Throw<XunitException>();
    }

    [Fact]
    public void Limits_AreAssertedIndependently()
    {
        var page = Make([1], requested: 500, applied: 100);

        page.Should().HaveRequestedLimit(500).And.HaveAppliedLimit(100).And.BeCapped();
        Make([1]).Should().NotBeCapped();
        var act = () => page.Should().HaveAppliedLimit(500);
        act.Should().Throw<XunitException>().WithMessage("*500*100*");
    }

    [Fact]
    public void NotBeCapped_Capped_Fails()
    {
        var act = () => Make([1], requested: 500, applied: 100).Should().NotBeCapped();

        act.Should().Throw<XunitException>().WithMessage("*500*100*");
    }

    [Fact]
    public void NullPage_ReportsAssertionFailureNotNullReference()
    {
        var act = () => ((Page<int>)null!).Should().HaveCount(0);

        act.Should().Throw<XunitException>();
    }
}