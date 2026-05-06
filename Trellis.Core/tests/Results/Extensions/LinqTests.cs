using FluentAssertions;
using Trellis;
using Trellis.Testing;
using Xunit;

namespace Trellis.Core.Tests.Results.Extensions.Linq;

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

    // ---------- m-C-2 entry-point null-guards ----------
    // These tests pin that the LINQ extensions throw ArgumentNullException with the user's
    // declared paramName ("selector"/"collectionSelector"/"resultSelector") at the public-API
    // entry point — NOT the underlying Map/Bind paramName ("func") nor a NullReferenceException
    // from inside a compiler-generated lambda.

    [Fact]
    public void Select_NullSelector_ThrowsArgumentNullException_WithSelectorParamName()
    {
        var r = Result.Ok(5);
        Func<int, int> selector = null!;

        var act = () => r.Select(selector);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("selector");
    }

    [Fact]
    public void SelectMany_NullCollectionSelector_ThrowsArgumentNullException_WithCollectionSelectorParamName()
    {
        var r = Result.Ok(5);
        Func<int, Result<int>> collectionSelector = null!;

        var act = () => r.SelectMany(collectionSelector, (a, b) => a + b);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("collectionSelector");
    }

    [Fact]
    public void SelectMany_NullResultSelector_ThrowsArgumentNullException_WithResultSelectorParamName()
    {
        var r = Result.Ok(5);
        Func<int, int, int> resultSelector = null!;

        var act = () => r.SelectMany(_ => Result.Ok(7), resultSelector);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resultSelector");
    }
}