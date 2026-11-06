namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;

/// <summary>
/// Pins the parent/child shape of the spans the ROP extensions emit: steps of one chain are siblings
/// under whatever span was ambient, and an operation started inside a step's callback is a child of that step.
/// </summary>
public class TracingParentageTests
{
    private static readonly Error s_error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "invalid" };

    private static Activity StartAmbient() => new Activity("ambient").Start();

    private static Activity Single(ActivityTestHelper helper, string displayName) =>
        helper.CapturedActivities.Where(a => a.DisplayName == displayName).Should().ContainSingle().Subject;

    #region Steps of one chain

    [Fact]
    public void Chain_steps_are_siblings_under_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        var result = Result.Ok(1)
            .Bind(x => Result.Ok(x + 1))
            .Map(x => x * 2)
            .Tap(_ => { });

        result.IsSuccess.Should().BeTrue();
        helper.CapturedActivities.Should().HaveCount(3);
        helper.CapturedActivities.Should().OnlyContain(a => a.ParentId == ambient.Id, "no step runs inside another step");
        helper.CapturedActivities.Should().OnlyContain(a => a.TraceId == ambient.TraceId);
    }

    [Fact]
    public void Skipped_steps_of_a_failed_chain_are_still_siblings_under_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        var result = Result.Fail<int>(s_error)
            .Bind(x => Result.Ok(x + 1))
            .Map(x => x * 2)
            .Tap(_ => { });

        result.IsFailure.Should().BeTrue();
        helper.CapturedActivities.Should().HaveCount(3);
        helper.CapturedActivities.Should().OnlyContain(a => a.ParentId == ambient.Id);
    }

    [Fact]
    public void Chain_steps_without_an_ambient_span_are_roots()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1)
            .Bind(x => Result.Ok(x + 1))
            .Map(x => x * 2);

        helper.CapturedActivities.Should().HaveCount(2);
        helper.CapturedActivities.Should().OnlyContain(a => a.ParentId == null);
    }

    [Fact]
    public void Chain_leaves_the_ambient_span_current_when_it_completes()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1)
            .Bind(x => Result.Ok(x + 1))
            .Map(x => x * 2)
            .Tap(_ => { });

        Activity.Current.Should().BeSameAs(ambient, "every step must stop its span so the next step does not nest under it");
    }

    [Fact]
    public void Chain_without_an_ambient_span_leaves_no_current_span()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1).Bind(x => Result.Ok(x + 1)).Map(x => x * 2);

        Activity.Current.Should().BeNull();
    }

    #endregion

    #region Operations started inside a callback

    [Fact]
    public void Operation_inside_a_Bind_callback_is_a_child_of_the_Bind_span()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1).Bind(x => Result.Ok(x).Map(y => y + 1));

        var bind = Single(helper, "Bind");
        var map = Single(helper, "Map");
        bind.ParentId.Should().BeNull();
        map.ParentId.Should().Be(bind.Id);
        map.TraceId.Should().Be(bind.TraceId);
    }

    [Fact]
    public void Operation_inside_a_Map_callback_is_a_child_of_the_Map_span()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1).Map(x => Result.Ok(x).Tap(_ => { }).IsSuccess);

        Single(helper, "Tap").ParentId.Should().Be(Single(helper, "Map").Id);
    }

    [Fact]
    public void Operation_inside_a_Tap_callback_is_a_child_of_the_Tap_span()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1).Tap(x => Result.Ok(x).Map(y => y + 1));

        Single(helper, "Map").ParentId.Should().Be(Single(helper, "Tap").Id);
    }

    [Fact]
    public void Operation_inside_an_Ensure_predicate_is_a_child_of_the_Ensure_span()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1).Ensure(x => Result.Ok(x).Map(y => y > 0).IsSuccess, s_error);

        Single(helper, "Map").ParentId.Should().Be(Single(helper, "Ensure").Id);
    }

    [Fact]
    public void Nested_operations_form_a_chain_of_parents_and_share_one_trace()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        Result.Ok(1).Bind(x => Result.Ok(x).Bind(y => Result.Ok(y).Map(z => z + 1)));

        var binds = helper.CapturedActivities.Where(a => a.DisplayName == "Bind").ToArray();
        binds.Should().HaveCount(2);
        var outer = binds.Single(a => a.ParentId == null);
        var inner = binds.Single(a => a.ParentId == outer.Id);
        Single(helper, "Map").ParentId.Should().Be(inner.Id);
        helper.CapturedActivities.Should().OnlyContain(a => a.TraceId == outer.TraceId);
    }

    #endregion

    #region Every overload family

    private static bool NestedMap() => Result.Ok(0).Map(x => x).IsSuccess;

    private static bool NestedTap() => Result.Ok(0).Tap(_ => { }).IsSuccess;

    private static void AssertStepUnderAmbient(ActivityTestHelper helper, Activity ambient, string step, string? nested = null)
    {
        var span = Single(helper, step);
        span.ParentId.Should().Be(ambient.Id);
        span.TraceId.Should().Be(ambient.TraceId);
        if (nested is not null)
            Single(helper, nested).ParentId.Should().Be(span.Id);
    }

    private static void AssertSyncStepUnderAmbient(ActivityTestHelper helper, Activity ambient, string step, string? nested = null)
    {
        AssertStepUnderAmbient(helper, ambient, step, nested);
        Activity.Current.Should().BeSameAs(ambient, "the step must stop its span before returning");
    }

    [Fact]
    public void Tap_Action_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1).Tap(() => NestedMap());

        AssertSyncStepUnderAmbient(helper, ambient, "Tap", "Map");
    }

    [Fact]
    public void Ensure_bool_factory_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1).Ensure(NestedMap, s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "Ensure", "Map");
    }

    [Fact]
    public void Ensure_predicate_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1).Ensure(_ => NestedMap(), s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "Ensure", "Map");
    }

    [Fact]
    public void Ensure_predicate_with_error_factory_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1).Ensure(_ => NestedMap(), _ => s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "Ensure", "Map");
    }

    [Fact]
    public void Ensure_result_factory_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1).Ensure(() => Result.Ok(1).Map(x => x));

        AssertSyncStepUnderAmbient(helper, ambient, "Ensure", "Map");
    }

    [Fact]
    public void Ensure_value_to_result_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok(1).Ensure(x => Result.Ok(x).Map(y => y));

        AssertSyncStepUnderAmbient(helper, ambient, "Ensure", "Map");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_with_error_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        "text".EnsureNotNullOrWhiteSpace(s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "EnsureNotNullOrWhiteSpace");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_with_error_factory_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        "text".EnsureNotNullOrWhiteSpace(() => s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "EnsureNotNullOrWhiteSpace");
    }

    [Fact]
    public void EnsureNotNullOrWhiteSpace_with_field_name_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        "text".EnsureNotNullOrWhiteSpace("field");

        AssertSyncStepUnderAmbient(helper, ambient, "EnsureNotNullOrWhiteSpace");
    }

    [Fact]
    public void EnsureNotNull_reference_type_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok<string?>("text").EnsureNotNull(s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "EnsureNotNull");
    }

    [Fact]
    public void EnsureNotNull_value_type_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        Result.Ok<int?>(1).EnsureNotNull(s_error);

        AssertSyncStepUnderAmbient(helper, ambient, "EnsureNotNull");
    }

    [Fact]
    public async Task BindAsync_ValueTask_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        await Result.Ok(1).BindAsync(x => new ValueTask<Result<int>>(Result.Ok(x).Map(y => y)));

        AssertStepUnderAmbient(helper, ambient, "Bind", "Map");
    }

    [Fact]
    public async Task MapAsync_ValueTask_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        await Result.Ok(1).MapAsync(_ => new ValueTask<bool>(NestedTap()));

        AssertStepUnderAmbient(helper, ambient, "Map", "Tap");
    }

    [Fact]
    public async Task TapAsync_Task_factory_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        await Result.Ok(1).TapAsync(() =>
        {
            NestedMap();
            return Task.CompletedTask;
        });

        AssertStepUnderAmbient(helper, ambient, "Tap", "Map");
    }

    [Fact]
    public async Task TapAsync_ValueTask_factory_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        await Result.Ok(1).TapAsync(() =>
        {
            NestedMap();
            return ValueTask.CompletedTask;
        });

        AssertStepUnderAmbient(helper, ambient, "Tap", "Map");
    }

    [Fact]
    public async Task TapAsync_ValueTask_value_callback_span_is_a_child_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        await Result.Ok(1).TapAsync(_ =>
        {
            NestedMap();
            return ValueTask.CompletedTask;
        });

        AssertStepUnderAmbient(helper, ambient, "Tap", "Map");
    }

    #endregion

    #region Async

    [Fact]
    public async Task Operation_inside_a_BindAsync_callback_after_an_await_is_a_child_of_the_Bind_span()
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();

        await Result.Ok(1).BindAsync(async x =>
        {
            await Task.Yield();
            return Result.Ok(x).Map(y => y + 1);
        });

        var bind = Single(helper, "Bind");
        Single(helper, "Map").ParentId.Should().Be(bind.Id);
        bind.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task Async_chain_steps_are_children_of_the_ambient_span()
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();

        await Result.Ok(1)
            .BindAsync(x => Task.FromResult(Result.Ok(x + 1)))
            .MapAsync(x => Task.FromResult(x * 2))
            .TapAsync(_ => Task.CompletedTask);

        helper.CapturedActivities.Should().HaveCount(3);
        helper.CapturedActivities.Should().OnlyContain(a => a.ParentId == ambient.Id);
    }

    #endregion
}
