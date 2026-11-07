namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;

/// <summary>
/// Pins the fan-in contract of the tuple <c>WhenAllAsync</c> overloads: the returned task does not complete until
/// every child has completed, a fault or cancellation escapes as an unwrapped exception instead of being folded into a
/// <see cref="Result{TValue}"/>, domain failures are merged in tuple order, and the span records the task count.
/// </summary>
public class WhenAllAsyncFanInTests
{
    private static TaskCompletionSource<Result<int>> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Task<Result<int>> Ok(int value) => Task.FromResult(Result.Ok(value));

    private static Task<Result<int>> Failed(Error error) => Task.FromResult(Result.Fail<int>(error));

    private static Task<Result<int>> Faulted(string message) =>
        Task.FromException<Result<int>>(new InvalidOperationException(message));

    private static Task<Result<int>> Canceled() => Task.FromCanceled<Result<int>>(new CancellationToken(canceled: true));

    private static Error.InvalidInput Invalid(string field) => Error.InvalidInput.ForField("required", field);

    #region The returned task waits for every child

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAllAsync_waits_for_a_pending_child_after_a_domain_failure(bool failedChildFirst)
    {
        var gate = Gate();
        var error = Invalid("a");
        var failed = Failed(error);

        var all = failedChildFirst ? (failed, gate.Task).WhenAllAsync() : (gate.Task, failed).WhenAllAsync();

        all.IsCompleted.Should().BeFalse("the other child has not completed yet");
        gate.SetResult(Result.Ok(2));
        var result = await all;
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAllAsync_waits_for_a_pending_child_after_a_fault_and_then_rethrows_it(bool faultedChildFirst)
    {
        var gate = Gate();
        var faulted = Faulted("boom");

        var all = faultedChildFirst ? (faulted, gate.Task).WhenAllAsync() : (gate.Task, faulted).WhenAllAsync();

        all.IsCompleted.Should().BeFalse("a fault must not short-circuit the children that are still running");
        gate.SetResult(Result.Ok(2));
        Func<Task> act = () => all;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAllAsync_waits_for_a_pending_child_after_a_cancellation_and_then_cancels(bool canceledChildFirst)
    {
        var gate = Gate();
        var canceled = Canceled();

        var all = canceledChildFirst ? (canceled, gate.Task).WhenAllAsync() : (gate.Task, canceled).WhenAllAsync();

        all.IsCompleted.Should().BeFalse("a cancellation must not short-circuit the children that are still running");
        gate.SetResult(Result.Ok(2));
        Func<Task> act = () => all;
        await act.Should().ThrowAsync<OperationCanceledException>();
        all.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task WhenAllAsync_three_way_waits_for_every_pending_child()
    {
        var first = Gate();
        var second = Gate();
        var all = (first.Task, second.Task, Failed(Invalid("c"))).WhenAllAsync();

        first.SetResult(Result.Ok(1));
        all.IsCompleted.Should().BeFalse("one child is still pending");
        second.SetResult(Result.Ok(2));
        var result = await all;

        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Exceptions win over domain failures

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAllAsync_a_fault_wins_over_a_domain_failure(bool faultedChildFirst)
    {
        var faulted = Faulted("boom");
        var failed = Failed(Invalid("a"));

        Func<Task> act = async () => _ = await (faultedChildFirst ? (faulted, failed).WhenAllAsync() : (failed, faulted).WhenAllAsync());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAllAsync_a_cancellation_wins_over_a_domain_failure(bool canceledChildFirst)
    {
        var canceled = Canceled();
        var failed = Failed(Invalid("a"));

        Func<Task> act = async () => _ = await (canceledChildFirst ? (canceled, failed).WhenAllAsync() : (failed, canceled).WhenAllAsync());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Domain failures are merged in tuple order

    [Fact]
    public async Task WhenAllAsync_two_validation_failures_keep_both_fields_in_tuple_order()
    {
        var result = await (Failed(Invalid("a")), Failed(Invalid("b"))).WhenAllAsync();

        FieldsOf(result).Should().Equal(InputPointer.ForProperty("a"), InputPointer.ForProperty("b"));
    }

    [Fact]
    public async Task WhenAllAsync_validation_failures_are_ordered_by_tuple_position_not_completion_order()
    {
        var first = Gate();
        var second = Gate();
        var all = (first.Task, second.Task).WhenAllAsync();

        second.SetResult(Result.Fail<int>(Invalid("b")));
        first.SetResult(Result.Fail<int>(Invalid("a")));
        var result = await all;

        FieldsOf(result).Should().Equal(InputPointer.ForProperty("a"), InputPointer.ForProperty("b"));
    }

    [Fact]
    public async Task WhenAllAsync_three_validation_failures_keep_every_field_in_tuple_order()
    {
        var result = await (Failed(Invalid("a")), Failed(Invalid("b")), Failed(Invalid("c"))).WhenAllAsync();

        FieldsOf(result).Should().Equal(InputPointer.ForProperty("a"), InputPointer.ForProperty("b"), InputPointer.ForProperty("c"));
    }

    private static List<InputPointer> FieldsOf<T>(Result<T> result)
    {
        result.IsFailure.Should().BeTrue();
        var invalid = result.Error.Should().BeOfType<Error.InvalidInput>().Which;
        var fields = new List<InputPointer>();
        foreach (var violation in invalid.Fields)
            fields.Add(violation.Field);

        return fields;
    }

    #endregion

    #region Span tags

    private static readonly Dictionary<int, Func<Task>> s_successByArity = new()
    {
        [2] = async () => await (Ok(1), Ok(2)).WhenAllAsync(),
        [3] = async () => await (Ok(1), Ok(2), Ok(3)).WhenAllAsync(),
        [4] = async () => await (Ok(1), Ok(2), Ok(3), Ok(4)).WhenAllAsync(),
        [5] = async () => await (Ok(1), Ok(2), Ok(3), Ok(4), Ok(5)).WhenAllAsync(),
        [6] = async () => await (Ok(1), Ok(2), Ok(3), Ok(4), Ok(5), Ok(6)).WhenAllAsync(),
        [7] = async () => await (Ok(1), Ok(2), Ok(3), Ok(4), Ok(5), Ok(6), Ok(7)).WhenAllAsync(),
        [8] = async () => await (Ok(1), Ok(2), Ok(3), Ok(4), Ok(5), Ok(6), Ok(7), Ok(8)).WhenAllAsync(),
        [9] = async () => await (Ok(1), Ok(2), Ok(3), Ok(4), Ok(5), Ok(6), Ok(7), Ok(8), Ok(9)).WhenAllAsync(),
    };

    public static TheoryData<int> Arities() => new(s_successByArity.Keys);

    [Theory]
    [MemberData(nameof(Arities))]
    public async Task WhenAllAsync_records_the_task_count_for_every_arity(int arity)
    {
        using var activityTest = new ActivityTestHelper();

        await s_successByArity[arity]();

        var activity = activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Ok);
        activity.GetTagItem("parallel.task_count").Should().Be(arity);
    }

    [Fact]
    public async Task WhenAllAsync_a_canceled_child_marks_the_span_as_an_error_with_the_exception_type()
    {
        using var activityTest = new ActivityTestHelper();

        Func<Task> act = async () => _ = await (Canceled(), Ok(2)).WhenAllAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
        var activity = activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
        activity.GetTagItem("error.type").Should().Be(nameof(TaskCanceledException));
    }

    [Fact]
    public async Task WhenAllAsync_a_domain_failure_marks_the_span_as_an_error_without_an_exception_type()
    {
        using var activityTest = new ActivityTestHelper();

        await (Failed(Invalid("a")), Ok(2)).WhenAllAsync();

        var activity = activityTest.AssertActivityCapturedWithStatus("WhenAllAsync", ActivityStatusCode.Error);
        activity.GetTagItem("error.type").Should().BeNull("a domain failure is a result, not an exception");
    }

    #endregion
}
