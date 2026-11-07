using Trellis.Testing;
namespace Trellis.Core.Tests.DomainDrivenDesign.Aggregates;

using Trellis;

/// <summary>
/// The four async <c>OptionalETagAsync</c> / <c>RequireETagAsync</c> overloads (Task and ValueTask)
/// must wait for their source, pick the matching sync rule and forward the caller's header tags.
/// The rules themselves are pinned by <see cref="AggregateETagExtensionsTests"/> and <see cref="AggregateTests"/>.
/// </summary>
public class AggregateETagAsyncExtensionsTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    private static readonly Dictionary<string, Func<Task<Result<TestAggregate>>, EntityTagValue[]?, Task<Result<TestAggregate>>>> s_operations = new()
    {
        ["Optional/Task"] = (source, tags) => source.OptionalETagAsync(tags),
        ["Optional/ValueTask"] = (source, tags) => new ValueTask<Result<TestAggregate>>(source).OptionalETagAsync(tags).AsTask(),
        ["Require/Task"] = (source, tags) => source.RequireETagAsync(tags),
        ["Require/ValueTask"] = (source, tags) => new ValueTask<Result<TestAggregate>>(source).RequireETagAsync(tags).AsTask(),
    };

    public static TheoryData<string> Operations =>
        new() { "Optional/Task", "Optional/ValueTask", "Require/Task", "Require/ValueTask" };

    private static bool RequiresHeader(string operation) => operation.StartsWith("Require", StringComparison.Ordinal);

    private static TestAggregate AggregateWithETag(string etag)
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag(etag);
        return aggregate;
    }

    private static Task<Result<TestAggregate>> Run(string operation, Result<TestAggregate> source, EntityTagValue[]? tags) =>
        s_operations[operation](Task.FromResult(source), tags);

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task An_absent_header_is_unconditional_for_Optional_and_428_for_Require(string operation)
    {
        var aggregate = AggregateWithETag("current");

        var result = await Run(operation, Result.Ok(aggregate), tags: null);

        if (RequiresHeader(operation))
        {
            var error = result.Should().HaveErrorDetail("This operation requires an If-Match header.")
                .And.BeFailureOfType<Error.TransportFault>().Which;
            error.Fault.Should().Be(new HttpError.PreconditionRequired(PreconditionKind.IfMatch));
        }
        else
        {
            result.Should().BeSuccess().Which.Should().BeSameAs(aggregate);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_matching_strong_tag_is_forwarded_and_accepted(string operation)
    {
        var aggregate = AggregateWithETag("current");

        var result = await Run(operation, Result.Ok(aggregate), [EntityTagValue.Strong("stale"), EntityTagValue.Strong("current")]);

        result.Should().BeSuccess().Which.Should().BeSameAs(aggregate);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_non_matching_tag_is_forwarded_and_rejected_as_412(string operation)
    {
        var result = await Run(operation, Result.Ok(AggregateWithETag("current")), [EntityTagValue.Strong("stale")]);

        var error = result.Should().HaveErrorDetail("Resource has been modified. Please reload and retry.")
            .And.BeFailureOfType<Error.TransportFault>().Which;
        error.Fault.Should().Be(new HttpError.PreconditionFailed(ResourceRef.For<TestAggregate>(), PreconditionKind.IfMatch));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_failed_source_keeps_its_own_error_with_or_without_a_header(string operation)
    {
        var original = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" };

        var withoutHeader = await Run(operation, Result.Fail<TestAggregate>(original), tags: null);
        var withHeader = await Run(operation, Result.Fail<TestAggregate>(original), [EntityTagValue.Strong("any")]);

        withoutHeader.Error.Should().BeSameAs(original);
        withHeader.Error.Should().BeSameAs(original);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_pending_source_is_awaited_before_the_header_is_applied(string operation)
    {
        var source = new TaskCompletionSource<Result<TestAggregate>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = s_operations[operation](source.Task, [EntityTagValue.Strong("stale")]);

        pending.IsCompleted.Should().BeFalse("the header cannot be checked before the aggregate is known");
        source.SetResult(Result.Ok(AggregateWithETag("current")));
        var result = await pending.WaitAsync(s_timeout, TestContext.Current.CancellationToken);
        result.Should().HaveErrorDetail("Resource has been modified. Please reload and retry.");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_faulted_source_propagates_its_exception(string operation)
    {
        var source = Task.FromException<Result<TestAggregate>>(new InvalidOperationException("boom"));

        var act = () => s_operations[operation](source, [EntityTagValue.Strong("any")]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_canceled_source_propagates_the_cancellation(string operation)
    {
        var source = Task.FromCanceled<Result<TestAggregate>>(new CancellationToken(canceled: true));

        var act = () => s_operations[operation](source, [EntityTagValue.Strong("any")]);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
