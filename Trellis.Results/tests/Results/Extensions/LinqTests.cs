using FluentAssertions;
using Trellis;
using Trellis.Testing;
using Xunit;

namespace Trellis.Results.Tests.Results.Extensions.Linq;

public class LinqTests : TestBase
{
    [Fact]
    public void Select_projects_success_value()
    {
        var r = Result.Ok(5);

        var projected = r.Select(x => x * 2); // query Select extension

        projected.Should().BeSuccess().Which.Should().Be(10);
    }

    [Fact]
    public void Select_propagates_failure()
    {
        var r = Result.Fail<int>(Error1);

        var projected = r.Select(x => x * 2);

        projected.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public void SelectMany_combines_two_success_results()
    {
        var a = Result.Ok(2);
        var b = Result.Ok(3);

        var combined =
            a.SelectMany(_ => b, (x, y) => x + y);

        combined.Should().BeSuccess().Which.Should().Be(5);
    }

    [Fact]
    public void SelectMany_propagates_first_failure()
    {
        var a = Result.Fail<int>(Error1);
        var b = Result.Ok(3);

        var combined =
            a.SelectMany(_ => b, (x, y) => x + y);

        combined.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public void Where_filters_out_value_and_returns_failure_when_predicate_false()
    {
        var r =
            Result.Ok(5)
                  .Where(v => v > 10); // predicate false

        r.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Result filtered out by predicate." });
    }

    [Fact]
    public void Where_keeps_success_when_predicate_true()
    {
        Result<int> r =
            Result.Ok(15)
                  .Where(v => v > 10);

        r.Should().BeSuccess().Which.Should().Be(15);
    }

    [Fact]
    public void SelectMany_combines_four_success_results()
    {
        var a = Result.Ok(1);
        var b = Result.Ok(2);
        var c = Result.Ok(3);
        var d = Result.Ok(4);

        // LINQ query syntax exercises chained SelectMany calls for 4 Results
        Result<int> combined =
            from av in a
            from bv in b
            from cv in c
            from dv in d
            select av + bv + cv + dv;

        combined.Should().BeSuccess().Which.Should().Be(1 + 2 + 3 + 4);
    }
}