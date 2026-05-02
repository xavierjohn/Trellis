namespace Trellis.Core.Tests.Results.Extensions.SequenceAll;

using Trellis.Testing;

public class SequenceAllTests : TestBase
{
    [Fact]
    public void SequenceAll_AllSuccess_ReturnsOkWithAllValuesInOrder()
    {
        var results = new[] { Result.Ok(1), Result.Ok(2), Result.Ok(3) };

        var result = results.SequenceAll();

        result.Should().BeSuccess();
        result.Unwrap().Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void SequenceAll_Empty_ReturnsOkWithEmptyList()
    {
        var results = Array.Empty<Result<int>>();

        var result = results.SequenceAll();

        result.Should().BeSuccess();
        result.Unwrap().Should().BeEmpty();
    }

    [Fact]
    public void SequenceAll_SingleFailure_ReturnsThatErrorWithoutAggregateWrap()
    {
        var results = new[] { Result.Ok(1), Result.Fail<int>(Error1), Result.Ok(3) };

        var result = results.SequenceAll();

        result.Should().BeFailure();
        result.Error.Should().NotBeOfType<Error.Aggregate>();
        result.Error.Should().BeSameAs(Error1);
    }

    [Fact]
    public void SequenceAll_MultipleHeterogeneousFailures_ReturnsAggregate()
    {
        var unprocessable = Error.UnprocessableContent.ForField("name", "validation.required", "Name is required");
        var conflict = new Error.Conflict(new ResourceRef("Resource", null), "duplicate");
        var results = new[]
        {
            Result.Ok(1),
            Result.Fail<int>(unprocessable),
            Result.Fail<int>(conflict),
        };

        var result = results.SequenceAll();

        result.Should().BeFailure();
        var aggregate = result.Error.Should().BeOfType<Error.Aggregate>().Subject;
        aggregate.Errors.Items.Should().HaveCount(2);
        aggregate.Errors.Items.Should().Contain(unprocessable);
        aggregate.Errors.Items.Should().Contain(conflict);
    }

    [Fact]
    public void SequenceAll_MultipleUnprocessableContentFailures_MergesFieldsAndRules()
    {
        var first = new Error.UnprocessableContent(
            EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.required") { Detail = "Name required" }),
            EquatableArray.Create(new RuleViolation("rule.one") { Detail = "Rule one" }));

        var second = new Error.UnprocessableContent(
            EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.format") { Detail = "Invalid email" }),
            EquatableArray.Create(new RuleViolation("rule.two") { Detail = "Rule two" }));

        var results = new[]
        {
            Result.Fail<int>(first),
            Result.Ok(99),
            Result.Fail<int>(second),
        };

        var result = results.SequenceAll();

        result.Should().BeFailure();
        var merged = result.Error.Should().BeOfType<Error.UnprocessableContent>().Subject;
        merged.Fields.Items.Select(f => f.Field.Path).Should().Equal(["/name", "/email"]);
        merged.Rules.Items.Select(r => r.ReasonCode).Should().Equal(["rule.one", "rule.two"]);
    }

    [Fact]
    public void SequenceAll_FailuresInterleavedWithSuccesses_ReturnsCombinedErrors()
    {
        var results = new[]
        {
            Result.Ok(1),
            Result.Fail<int>(Error1),
            Result.Ok(3),
            Result.Fail<int>(Error2),
            Result.Ok(5),
        };

        var result = results.SequenceAll();

        result.Should().BeFailure();
        var aggregate = result.Error.Should().BeOfType<Error.Aggregate>().Subject;
        aggregate.Errors.Items.Should().Equal([Error1, Error2]);
    }

    [Fact]
    public void SequenceAll_NullSource_Throws()
    {
        IEnumerable<Result<int>>? source = null;

        var act = () => source!.SequenceAll();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SequenceAll_DoesNotShortCircuit_EnumeratesAllItems()
    {
        var visited = new List<int>();
        IEnumerable<Result<int>> Gen()
        {
            foreach (var x in new[] { 1, 2, 3, 4, 5 })
            {
                visited.Add(x);
                yield return x == 1 ? Result.Fail<int>(Error1) : Result.Ok(x);
            }
        }

        var result = Gen().SequenceAll();

        result.Should().BeFailure();
        visited.Should().Equal([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void SequenceAll_OfUnit_AllSuccess_ReturnsOkUnit()
    {
        var results = new[] { Result.Ok(), Result.Ok(), Result.Ok() };

        var result = results.SequenceAll();

        result.Should().BeSuccess();
    }

    [Fact]
    public void SequenceAll_OfUnit_Empty_ReturnsOkUnit()
    {
        var results = Array.Empty<Result<Unit>>();

        var result = results.SequenceAll();

        result.Should().BeSuccess();
    }

    [Fact]
    public void SequenceAll_OfUnit_SingleFailure_ReturnsThatErrorWithoutAggregateWrap()
    {
        var results = new[] { Result.Ok(), Result.Fail(Error1), Result.Ok() };

        var result = results.SequenceAll();

        result.Should().BeFailure();
        result.Error.Should().NotBeOfType<Error.Aggregate>();
        result.Error.Should().BeSameAs(Error1);
    }

    [Fact]
    public void SequenceAll_OfUnit_MultipleFailures_ReturnsCombinedError()
    {
        var results = new[] { Result.Ok(), Result.Fail(Error1), Result.Fail(Error2) };

        var result = results.SequenceAll();

        result.Should().BeFailure();
        var aggregate = result.Error.Should().BeOfType<Error.Aggregate>().Subject;
        aggregate.Errors.Items.Should().Equal([Error1, Error2]);
    }

    [Fact]
    public void SequenceAll_OfUnit_NullSource_Throws()
    {
        IEnumerable<Result<Unit>>? source = null;

        var act = () => source!.SequenceAll();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SequenceAll_OfUnit_DoesNotShortCircuit_EnumeratesAllItems()
    {
        var visited = new List<int>();
        IEnumerable<Result<Unit>> Gen()
        {
            foreach (var x in new[] { 1, 2, 3, 4, 5 })
            {
                visited.Add(x);
                yield return x % 2 == 0 ? Result.Fail(Error1) : Result.Ok();
            }
        }

        var result = Gen().SequenceAll();

        result.Should().BeFailure();
        visited.Should().Equal([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void SequenceAll_VsSequence_DemonstratesDifference()
    {
        var results = new[]
        {
            Result.Ok(1),
            Result.Fail<int>(Error1),
            Result.Fail<int>(Error2),
            Result.Ok(4),
        };

        // Fail-fast: returns the first failure unchanged.
        var failFast = results.Sequence();
        failFast.Should().BeFailure();
        failFast.Error.Should().BeSameAs(Error1);
        failFast.Error.Should().NotBeOfType<Error.Aggregate>();

        // Accumulating: visits every item, folds both failures into an Aggregate.
        var accumulated = results.SequenceAll();
        accumulated.Should().BeFailure();
        var aggregate = accumulated.Error.Should().BeOfType<Error.Aggregate>().Subject;
        aggregate.Errors.Items.Should().Equal([Error1, Error2]);
    }
}
