namespace Trellis.Core.Tests.Results.Extensions.TraverseAll;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class TraverseAllIndexedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TraverseAll_Indexed_Success_PreservesPositionsAndOrder(bool useIterator)
    {
        int[] items = [40, 10, 40];
        IEnumerable<int> source = useIterator ? items.Select(value => value) : items;
        var visited = new List<(int, int)>();

        var result = source.TraverseAll((value, index) =>
        {
            visited.Add((value, index));
            return Result.Ok(value + index);
        });

        result.Should().BeSuccess().Which.Should().Equal([40, 11, 42]);
        visited.Should().Equal([(40, 0), (10, 1), (40, 2)]);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
    }

    [Fact]
    public void TraverseAll_Indexed_Empty_DoesNotInvokeSelector()
    {
        var calls = 0;
        var result = Array.Empty<int>().TraverseAll((value, index) =>
        {
            calls++;
            return Result.Ok(value + index);
        });

        result.Should().BeSuccess().Which.Should().BeEmpty();
        calls.Should().Be(0);
    }

    [Fact]
    public void TraverseAll_Indexed_SingleFailure_PreservesErrorAndContinues()
    {
        var error = new Error.Forbidden("denied");
        var visited = new List<int>();
        int[] items = [7, 7, 7];

        var result = items.TraverseAll((value, index) =>
        {
            visited.Add(index);
            return index == 1 ? Result.Fail<int>(error) : Result.Ok(value);
        });

        result.Should().BeFailure().Which.Should().BeSameAs(error);
        visited.Should().Equal([0, 1, 2]);
    }

    [Fact]
    public void TraverseAll_Indexed_ValidationFailures_PreserveOriginalInputPointers()
    {
        string?[] items = [null, "valid", null];
        var owner = InputPointer.Root.AppendProperty("items");

        var result = items.TraverseAll((value, index) =>
            value.ToResult(() => Error.InvalidInput.ForField(
                owner.AppendIndex(index), ValidationCodes.ValueNotNull)));

        var error = result.Should().BeFailureOfType<Error.InvalidInput>().Which;
        error.Fields.Items.Select(field => field.Field.Path).Should().Equal(["/items/0", "/items/2"]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void TraverseAll_Indexed_MixedFailures_AccumulatesErrorsAndPersistIntent(int persistIndex)
    {
        var first = new Error.Forbidden("denied");
        var last = new Error.Conflict(null, "duplicate");
        int[] items = [0, 1, 2];

        var result = items.TraverseAll((value, index) =>
        {
            if (index == 1)
                return Result.Ok(value);
            Error error = index == 0 ? first : last;
            return index == persistIndex ? Result.FailAfterCommit<int>(error) : Result.Fail<int>(error);
        });

        result.Should().BeFailureOfType<Error.Aggregate>().Which.Errors.Items.Should().Equal([first, last]);
        ((IPersistOnFailure)result).PersistOnFailure.Should().Be(persistIndex >= 0);
    }

    [Fact]
    public void TraverseAll_Indexed_Iterator_EnumeratesOnceAndDisposes()
    {
        var enumerations = 0;
        var disposals = 0;
        IEnumerable<int> Source()
        {
            enumerations++;
            try
            {
                yield return 10;
                yield return 20;
            }
            finally
            {
                disposals++;
            }
        }

        Source().TraverseAll((value, index) => Result.Ok(value + index))
            .Should().BeSuccess().Which.Should().Equal([10, 21]);

        enumerations.Should().Be(1);
        disposals.Should().Be(1);
    }

    [Fact]
    public void TraverseAll_Indexed_SelectorThrows_PropagatesAndDisposes()
    {
        var exception = new InvalidOperationException("selector failed");
        var visited = new List<int>();
        var disposed = false;
        IEnumerable<int> Source()
        {
            try
            {
                yield return 10;
                yield return 20;
                yield return 30;
            }
            finally
            {
                disposed = true;
            }
        }

        Result<int> Select(int value, int index)
        {
            visited.Add(index);
            if (index == 1)
                throw exception;
            return Result.Ok(value);
        }

        var act = () => Source().TraverseAll(Select);

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
        visited.Should().Equal([0, 1]);
        disposed.Should().BeTrue();
    }

    [Fact]
    public void TraverseAll_Indexed_NullArguments_ThrowBeforeEnumeration()
    {
        IEnumerable<int> source = null!;
        var nullSource = () => source.TraverseAll((int value, int index) => Result.Ok(value + index));
        var enumerated = false;
        IEnumerable<int> Source()
        {
            enumerated = true;
            yield return 1;
        }

        var nullSelector = () => Source().TraverseAll((Func<int, int, Result<int>>)null!);

        nullSource.Should().Throw<ArgumentNullException>().WithParameterName("source");
        nullSelector.Should().Throw<ArgumentNullException>().WithParameterName("selector");
        enumerated.Should().BeFalse();
    }

    [Fact]
    public void TraverseAll_Unindexed_NullLiteral_RemainsUnambiguous()
    {
        var act = () => Array.Empty<int>().TraverseAll<int, int>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("selector");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TraverseAll_Indexed_Tracing_UsesOneTraversalActivity(bool success)
    {
        using var tracing = new ActivityTestHelper();
        int[] items = [1, 2];

        var result = items.TraverseAll((value, index) => success || index == 1
            ? Result.Ok(value)
            : Result.Fail<int>(new Error.Forbidden("denied")));

        result.IsSuccess.Should().Be(success);
        var activity = tracing.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be("TraverseAll");
        activity.Status.Should().Be(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }
}
