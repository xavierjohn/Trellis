namespace Trellis.Core.Tests.Errors;

public class ErrorAccumulationTests
{
    [Theory]
    [InlineData("SequenceAll", "validation")]
    [InlineData("SequenceAll", "conflict")]
    [InlineData("SequenceAll", "detail")]
    [InlineData("TraverseAll", "validation")]
    [InlineData("TraverseAll", "conflict")]
    [InlineData("TraverseAll", "detail")]
    [InlineData("EnsureAll", "validation")]
    [InlineData("EnsureAll", "conflict")]
    [InlineData("EnsureAll", "detail")]
    public void Accumulation_Sync_ManyFailures_AllocationsGrowLinearly(string operation, string errorKind)
    {
        var small = CreateOperation(operation, CreateErrors(128, errorKind));
        var large = CreateOperation(operation, CreateErrors(256, errorKind));

        var smallAllocation = MeasureAllocations(small);
        var largeAllocation = MeasureAllocations(large);

        largeAllocation.Should().BeLessThan(smallAllocation * 3,
            "doubling a batch must not repeatedly copy all preceding errors or details");
    }

    [Theory]
    [InlineData("SequenceAll")]
    [InlineData("TraverseAll")]
    [InlineData("EnsureAll")]
    public void Accumulation_Sync_MixedFailures_PreservesLeftFoldSemantics(string operation)
    {
        var firstCause = new Error.Unexpected("first-cause");
        var secondCause = new Error.Unexpected("second-cause");
        var first = Error.InvalidInput.ForField("first", "one") with
        {
            Code = "custom.first",
            Detail = "first",
            Cause = firstCause
        };
        var second = Error.InvalidInput.ForRule("second") with
        {
            Code = "custom.second",
            Detail = "second",
            Cause = secondCause
        };
        var sameCombinedDetail = Error.InvalidInput.ForField("third", "three") with
        {
            Detail = "first; second"
        };
        var conflict = new Error.Conflict("duplicate");
        var unavailable = new Error.Unavailable();
        var nested = new Error.Aggregate(conflict, unavailable) { Detail = "discarded wrapper" };
        var tailOne = Error.InvalidInput.ForField("fourth", "four");
        var tailTwo = Error.InvalidInput.ForField("fifth", "five");
        Error[] errors = [first, second, sameCombinedDetail, nested, tailOne, tailTwo];
        Error? expected = null;
        foreach (var error in errors)
            expected = expected.Combine(error);

        var actual = CreateOperation(operation, errors)();

        actual!.Equals(expected).Should().BeTrue();
        var aggregate = actual.Should().BeOfType<Error.Aggregate>().Which;
        aggregate.Errors.Length.Should().Be(5);
        var merged = aggregate.Errors[0].Should().BeOfType<Error.InvalidInput>().Which;
        merged.Detail.Should().Be("first; second");
        merged.Cause.Should().BeSameAs(firstCause);
        merged.Code.Should().Be(ValidationCodes.Unspecified);
        aggregate.Errors[3].Should().BeSameAs(tailOne);
        aggregate.Errors[4].Should().BeSameAs(tailTwo);
    }

    private static Error[] CreateErrors(int count, string kind) =>
        Enumerable.Range(0, count).Select(index => kind switch
        {
            "validation" => (Error)Error.InvalidInput.ForField("invalid", "value"),
            "conflict" => new Error.Conflict("duplicate"),
            "detail" => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty)
            {
                Detail = $"Failure {index}"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }).ToArray();

    private static Func<Error?> CreateOperation(string operation, Error[] errors)
    {
        if (operation == "EnsureAll")
        {
            var checks = errors.Select(error => ((Func<int, bool>)(static _ => false), error)).ToArray();
            return () => Result.Ok(0).EnsureAll(checks).Error;
        }

        return operation switch
        {
            "SequenceAll" => () => errors.Select(static error => Result.Fail<int>(error)).SequenceAll().Error,
            "TraverseAll" => () => errors.TraverseAll(static error => Result.Fail<int>(error)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static long MeasureAllocations(Func<Error?> operation)
    {
        for (var i = 0; i < 64; i++)
            _ = operation();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = operation();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().NotBeNull();
        return allocated;
    }
}