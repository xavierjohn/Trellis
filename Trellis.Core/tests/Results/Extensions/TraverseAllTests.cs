namespace Trellis.Core.Tests.Results.Extensions.TraverseAll;

using Trellis.Testing;

public class TraverseAllTests : TestBase
{
    #region TraverseAll (sync)

    [Fact]
    public void TraverseAll_AllSuccess_ReturnsOkWithAllValuesInOrder()
    {
        var items = new[] { 1, 2, 3, 4, 5 };

        var result = items.TraverseAll(x => Result.Ok(x * 2));

        result.Should().BeSuccess();
        result.Unwrap().Should().Equal([2, 4, 6, 8, 10]);
    }

    [Fact]
    public void TraverseAll_Empty_ReturnsOkWithEmptyList()
    {
        var items = Array.Empty<int>();

        var result = items.TraverseAll(x => Result.Ok(x * 2));

        result.Should().BeSuccess();
        result.Unwrap().Should().BeEmpty();
    }

    [Fact]
    public void TraverseAll_SingleFailure_ReturnsThatErrorWithoutAggregateWrap()
    {
        var items = new[] { 1, 2, 3 };

        var result = items.TraverseAll(x =>
            x == 2 ? Result.Fail<int>(Error1) : Result.Ok(x));

        result.Should().BeFailure();
        result.Error.Should().NotBeOfType<Error.Aggregate>();
        result.Error.Should().BeSameAs(Error1);
    }

    [Fact]
    public void TraverseAll_MultipleHeterogeneousFailures_ReturnsAggregate()
    {
        var unprocessable = Error.UnprocessableContent.ForField("name", "validation.required", "Name is required");
        var conflict = new Error.Conflict(new ResourceRef("Resource", null), "duplicate");
        var items = new[] { "ok", "unprocessable", "conflict" };

        var result = items.TraverseAll(s => s switch
        {
            "unprocessable" => Result.Fail<string>(unprocessable),
            "conflict" => Result.Fail<string>(conflict),
            _ => Result.Ok(s),
        });

        result.Should().BeFailure();
        var aggregate = result.Error.Should().BeOfType<Error.Aggregate>().Subject;
        aggregate.Errors.Items.Should().Equal([unprocessable, conflict]);
    }

    [Fact]
    public void TraverseAll_MultipleUnprocessableContentFailures_MergesFieldsAndRules()
    {
        var items = new[] { "name", "ok", "email" };

        var result = items.TraverseAll(s => s switch
        {
            "name" => Result.Fail<string>(new Error.UnprocessableContent(
                EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.required") { Detail = "Name required" }),
                EquatableArray.Create(new RuleViolation("rule.one") { Detail = "Rule one" }))),
            "email" => Result.Fail<string>(new Error.UnprocessableContent(
                EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.format") { Detail = "Invalid email" }),
                EquatableArray.Create(new RuleViolation("rule.two") { Detail = "Rule two" }))),
            _ => Result.Ok(s),
        });

        result.Should().BeFailure();
        var merged = result.Error.Should().BeOfType<Error.UnprocessableContent>().Subject;
        merged.Fields.Items.Select(f => f.Field.Path).Should().Equal(["/name", "/email"]);
        merged.Rules.Items.Select(r => r.ReasonCode).Should().Equal(["rule.one", "rule.two"]);
    }

    [Fact]
    public void TraverseAll_NullSource_Throws()
    {
        IEnumerable<int>? source = null;

        var act = () => source!.TraverseAll(x => Result.Ok(x));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TraverseAll_NullSelector_Throws()
    {
        var items = new[] { 1, 2, 3 };
        Func<int, Result<int>>? selector = null;

        var act = () => items.TraverseAll(selector!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TraverseAll_DoesNotShortCircuit_InvokesSelectorForEveryItem()
    {
        var items = new[] { 1, 2, 3, 4, 5 };
        var visited = new List<int>();

        var result = items.TraverseAll(x =>
        {
            visited.Add(x);
            return x == 1 ? Result.Fail<int>(Error1) : Result.Ok(x);
        });

        result.Should().BeFailure();
        visited.Should().Equal([1, 2, 3, 4, 5]);
    }

    #endregion

    #region TraverseAllAsync

    [Fact]
    public async Task TraverseAllAsync_AllSuccess_ReturnsOkWithAllValuesInOrder()
    {
        var items = new[] { 1, 2, 3, 4, 5 };

        var result = await items.TraverseAllAsync(x => Task.FromResult(Result.Ok(x * 2)));

        result.Should().BeSuccess();
        result.Unwrap().Should().Equal([2, 4, 6, 8, 10]);
    }

    [Fact]
    public async Task TraverseAllAsync_Empty_ReturnsOkWithEmptyList()
    {
        var items = Array.Empty<int>();

        var result = await items.TraverseAllAsync(x => Task.FromResult(Result.Ok(x * 2)));

        result.Should().BeSuccess();
        result.Unwrap().Should().BeEmpty();
    }

    [Fact]
    public async Task TraverseAllAsync_SingleFailure_ReturnsThatErrorWithoutAggregateWrap()
    {
        var items = new[] { 1, 2, 3 };

        var result = await items.TraverseAllAsync(x =>
            Task.FromResult(x == 2 ? Result.Fail<int>(Error1) : Result.Ok(x)));

        result.Should().BeFailure();
        result.Error.Should().NotBeOfType<Error.Aggregate>();
        result.Error.Should().BeSameAs(Error1);
    }

    [Fact]
    public async Task TraverseAllAsync_MultipleFailures_ReturnsCombinedError()
    {
        var items = new[] { 1, 2, 3, 4 };

        var result = await items.TraverseAllAsync(x =>
            Task.FromResult(x switch
            {
                2 => Result.Fail<int>(Error1),
                4 => Result.Fail<int>(Error2),
                _ => Result.Ok(x),
            }));

        result.Should().BeFailure();
        var aggregate = result.Error.Should().BeOfType<Error.Aggregate>().Subject;
        aggregate.Errors.Items.Should().Equal([Error1, Error2]);
    }

    [Fact]
    public async Task TraverseAllAsync_DoesNotShortCircuit_AwaitsEverySelectorInvocation()
    {
        var items = new[] { 1, 2, 3, 4, 5 };
        var visited = new List<int>();

        var result = await items.TraverseAllAsync(async x =>
        {
            await Task.Yield();
            visited.Add(x);
            return x == 1 ? Result.Fail<int>(Error1) : Result.Ok(x);
        });

        result.Should().BeFailure();
        visited.Should().Equal([1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task TraverseAllAsync_NullSource_Throws()
    {
        IEnumerable<int>? source = null;

        var act = async () => await source!.TraverseAllAsync(x => Task.FromResult(Result.Ok(x)));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TraverseAllAsync_NullSelector_Throws()
    {
        var items = new[] { 1, 2, 3 };
        Func<int, Task<Result<int>>>? selector = null;

        var act = async () => await items.TraverseAllAsync(selector!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion
}
