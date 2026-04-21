using FluentAssertions;
using Trellis;
using Trellis.Testing;
using Xunit;

namespace Trellis.Core.Tests.Results.Extensions.Linq;

public class QueryExpressionTests : TestBase
{
    [Fact]
    public void Query_expression_success_chain()
    {
        var query =
            from a in Result.Ok(2)
            from b in Result.Ok(3)
            from c in Result.Ok(5)
            select a + b + c;

        query.Should().BeSuccess().Which.Should().Be(10);
    }

    [Fact]
    public void Query_expression_short_circuits_on_first_failure()
    {
        var query =
            from a in Result.Fail<int>(Error1)
            from b in Result.Ok(3)
            select a + b;

        query.Should().BeFailure().Which.Should().Be(Error1);
    }

    [Fact]
    public void Query_expression_where_clause_filters()
    {
        var query =
            from a in Result.Ok(4)
            where a % 2 == 1   // false
            select a;

        query.Should().HaveErrorDetail("Result filtered out by predicate.");
    }

    [Fact]
    public void Query_expression_where_clause_passes()
    {
        var query =
            from a in Result.Ok(9)
            where a > 3
            select a * 2;

        query.Should().BeSuccess().Which.Should().Be(18);
    }
}