namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;

/// <summary>
/// Extends <see cref="ResultTracingParentageTests"/> to the operator families it does not cover: each operator, run on a
/// success and on a failure, in its sync, <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/> forms, must leave
/// its spans as direct children of the ambient span, stop them all, and leave the ambient span current, so the step that
/// follows is a sibling and not nested under it.
/// </summary>
/// <remarks>
/// The callbacks start no spans of their own, so every captured span is a chain step. The async overloads record the span of
/// their synchronous operator (<c>BindZipAsync</c> records <c>BindZip</c>); the <c>TapOnFailureAsync</c>, <c>TraverseAsync</c>,
/// <c>TraverseAllAsync</c>, <c>MatchAsync</c> and <c>SwitchAsync</c> overloads are the exception, because they take the span name from their own method name.
/// </remarks>
public class ResultTracingSiblingContractTests
{
    private static readonly Error s_error = new Error.Conflict(Resource: null, Code: "boom") { Detail = "boom" };

    private static readonly Dictionary<string, Func<Result<int>, Task<Result<int>>>> s_steps = new()
    {
        ["BindZip"] = r => Task.FromResult(r.BindZip(x => Result.Ok(x + 1)).Map(pair => pair.Item1)),
        ["BindZipAsync Task"] = async r => (await Task.FromResult(r).BindZipAsync(x => Task.FromResult(Result.Ok(x + 1)))).Map(pair => pair.Item1),
        ["BindZipAsync Result"] = async r => (await r.BindZipAsync(x => Task.FromResult(Result.Ok(x + 1)))).Map(pair => pair.Item1),
        ["BindZipAsync ValueTask Result"] = async r => (await r.BindZipAsync(x => new ValueTask<Result<int>>(Result.Ok(x + 1)))).Map(pair => pair.Item1),
        ["BindZipAsync ValueTask"] = async r => (await new ValueTask<Result<int>>(r).BindZipAsync(x => new ValueTask<Result<int>>(Result.Ok(x + 1)))).Map(pair => pair.Item1),

        ["Check"] = r => Task.FromResult(r.Check(x => Result.Ok("checked"))),
        ["CheckAsync Task"] = async r => await Task.FromResult(r).CheckAsync(x => Task.FromResult(Result.Ok("checked"))),
        ["CheckAsync Result"] = async r => await r.CheckAsync(x => Task.FromResult(Result.Ok("checked"))),
        ["CheckAsync ValueTask Result"] = async r => await r.CheckAsync(x => new ValueTask<Result<string>>(Result.Ok("checked"))),
        ["CheckAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).CheckAsync(x => new ValueTask<Result<string>>(Result.Ok("checked"))),

        ["CheckIf"] = r => Task.FromResult(r.CheckIf(true, x => Result.Ok("checked"))),
        ["CheckIfAsync Task"] = async r => await Task.FromResult(r).CheckIfAsync(true, x => Task.FromResult(Result.Ok("checked"))),
        ["CheckIfAsync Result"] = async r => await r.CheckIfAsync(true, x => Task.FromResult(Result.Ok("checked"))),
        ["CheckIfAsync ValueTask Result"] = async r => await r.CheckIfAsync(true, x => new ValueTask<Result<string>>(Result.Ok("checked"))),
        ["CheckIfAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).CheckIfAsync(true, x => new ValueTask<Result<string>>(Result.Ok("checked"))),

        ["Ensure"] = r => Task.FromResult(r.Ensure(x => true, s_error)),
        ["EnsureAsync Task"] = async r => await Task.FromResult(r).EnsureAsync(x => Task.FromResult(true), s_error),
        ["EnsureAsync Task sync predicate"] = async r => await Task.FromResult(r).EnsureAsync(x => true, s_error),
        ["EnsureAsync Result"] = async r => await r.EnsureAsync(x => Task.FromResult(true), s_error),
        ["EnsureAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).EnsureAsync(x => ValueTask.FromResult(true), s_error),
        ["EnsureAsync ValueTask sync predicate"] = async r => await new ValueTask<Result<int>>(r).EnsureAsync(x => true, s_error),
        ["EnsureAsync Task async error"] = async r => await Task.FromResult(r).EnsureAsync(x => true, x => Task.FromResult<Error>(s_error)),
        ["EnsureAsync ValueTask async error"] = async r => await new ValueTask<Result<int>>(r).EnsureAsync(x => true, x => new ValueTask<Error>(s_error)),
        ["EnsureAsync ValueTask Result"] = async r => await r.EnsureAsync(x => ValueTask.FromResult(true), s_error),

        ["EnsureAll"] = r => Task.FromResult(r.EnsureAll((x => x > 0, s_error))),

        ["MapIf"] = r => Task.FromResult(r.MapIf(true, x => x + 1)),

        ["MapOnFailure"] = r => Task.FromResult(r.MapOnFailure(error => error)),
        ["MapOnFailureAsync Task"] = async r => await Task.FromResult(r).MapOnFailureAsync(error => error),
        ["MapOnFailureAsync Result"] = async r => await r.MapOnFailureAsync(error => Task.FromResult(error)),

        ["TapOnFailure"] = r => Task.FromResult(r.TapOnFailure(() => { })),
        ["TapOnFailureAsync Task"] = async r => await Task.FromResult(r).TapOnFailureAsync(() => Task.CompletedTask),
        ["TapOnFailureAsync Result"] = async r => await r.TapOnFailureAsync(() => Task.CompletedTask),
        ["TapOnFailureAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).TapOnFailureAsync(() => ValueTask.CompletedTask),

        ["Recover"] = r => Task.FromResult(r.Recover(0)),

        ["RecoverOnFailure"] = r => Task.FromResult(r.RecoverOnFailure(() => Result.Ok(0))),
        ["RecoverOnFailureAsync Task"] = async r => await Task.FromResult(r).RecoverOnFailureAsync(() => Result.Ok(0)),
        ["RecoverOnFailureAsync Result"] = async r => await r.RecoverOnFailureAsync(() => Task.FromResult(Result.Ok(0))),
        ["RecoverOnFailureAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).RecoverOnFailureAsync(() => Result.Ok(0)),

        ["When"] = r => Task.FromResult(r.When(true, x => Result.Ok(x + 1))),
        ["WhenAsync Task"] = async r => await Task.FromResult(r).WhenAsync(true, x => Task.FromResult(Result.Ok(x + 1))),
        ["WhenAsync Result"] = async r => await r.WhenAsync(true, x => Task.FromResult(Result.Ok(x + 1))),
        ["WhenAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).WhenAsync(true, x => new ValueTask<Result<int>>(Result.Ok(x + 1))),

        ["Unless"] = r => Task.FromResult(r.Unless(false, x => Result.Ok(x + 1))),
        ["UnlessAsync Task"] = async r => await Task.FromResult(r).UnlessAsync(false, x => Task.FromResult(Result.Ok(x + 1))),
        ["UnlessAsync Result"] = async r => await r.UnlessAsync(false, x => Task.FromResult(Result.Ok(x + 1))),
        ["UnlessAsync ValueTask"] = async r => await new ValueTask<Result<int>>(r).UnlessAsync(false, x => new ValueTask<Result<int>>(Result.Ok(x + 1))),

        ["Combine"] = r => Task.FromResult(r.Combine(Result.Ok(2)).Map(pair => pair.Item1)),
        ["CombineAsync Task"] = async r => (await Task.FromResult(r).CombineAsync(Result.Ok(2))).Map(pair => pair.Item1),

        ["Traverse"] = r => Task.FromResult(new[] { 1 }.Traverse(_ => r).Map(list => list.Count)),
        ["TraverseAsync"] = async r => (await new[] { 1 }.TraverseAsync(_ => Task.FromResult(r))).Map(list => list.Count),
        ["TraverseAll"] = r => Task.FromResult(new[] { 1 }.TraverseAll(_ => r).Map(list => list.Count)),
        ["TraverseAllAsync"] = async r => (await new[] { 1 }.TraverseAllAsync(_ => Task.FromResult(r))).Map(list => list.Count),
        ["Sequence"] = r => Task.FromResult(new[] { r }.Sequence().Map(list => list.Count)),
        ["SequenceAll"] = r => Task.FromResult(new[] { r }.SequenceAll().Map(list => list.Count)),

        ["Match"] = r =>
        {
            r.Match(x => x, error => 0);
            return Task.FromResult(r);
        },
        ["MatchAsync Result"] = async r =>
        {
            await r.MatchAsync(x => Task.FromResult(x), error => Task.FromResult(0));
            return r;
        },
        ["MatchAsync Result token"] = async r =>
        {
            await r.MatchAsync((x, _) => Task.FromResult(x), (error, _) => Task.FromResult(0), TestContext.Current.CancellationToken);
            return r;
        },
        ["MatchAsync ValueTask Result"] = async r =>
        {
            await r.MatchAsync(x => new ValueTask<int>(x), error => new ValueTask<int>(0));
            return r;
        },
        ["SwitchAsync Task"] = async r =>
        {
            await Task.FromResult(r).SwitchAsync(_ => Task.CompletedTask, _ => Task.CompletedTask);
            return r;
        },
        ["SwitchAsync Task token"] = async r =>
        {
            await Task.FromResult(r).SwitchAsync((_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);
            return r;
        },
        ["SwitchAsync ValueTask"] = async r =>
        {
            await new ValueTask<Result<int>>(r).SwitchAsync(_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask);
            return r;
        },
        ["Switch"] = r =>
        {
            r.Switch(_ => { }, _ => { });
            return Task.FromResult(r);
        },
        ["ToMaybe"] = r =>
        {
            r.ToMaybe();
            return Task.FromResult(r);
        },
    };

    public static TheoryData<string, bool> StepsWithOutcome()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in s_steps.Keys)
        {
            data.Add(name, true);
            data.Add(name, false);
        }

        return data;
    }

    private static string OperatorSpan(string step)
    {
        var name = step.Split(' ')[0];
        if (name is "TapOnFailureAsync" or "TraverseAsync" or "TraverseAllAsync" or "MatchAsync" or "SwitchAsync")
            return name;

        return name.EndsWith("Async", StringComparison.Ordinal) ? name[..^"Async".Length] : name;
    }

    private static Activity StartAmbient() => new Activity("ambient").Start();

    [Theory]
    [MemberData(nameof(StepsWithOutcome))]
    public async Task The_operator_spans_are_children_of_the_ambient_span_and_the_next_step_is_a_sibling(string step, bool success)
    {
        using var helper = new ActivityTestHelper();
        using var ambient = StartAmbient();
        var source = success ? Result.Ok(1) : Result.Fail<int>(s_error);

        (await s_steps[step](source)).Map(x => x + 1);

        helper.CapturedActivities.Should().Contain(span => span.DisplayName == OperatorSpan(step), "the operator's own span must be stopped to be reported");
        helper.CapturedActivities.Should().Contain(span => span.DisplayName == "Map", "the step after the operator records a span");
        helper.CapturedActivities.Should().OnlyContain(span => span.ParentId == ambient.Id, "no step runs inside another step");
        helper.CapturedActivities.Should().OnlyContain(span => span.IsStopped, "every span must be stopped before the next step starts");
        Activity.Current.Should().BeSameAs(ambient);
    }

    [Theory]
    [MemberData(nameof(StepsWithOutcome))]
    public async Task Without_an_ambient_span_the_operator_spans_are_roots_and_none_stays_current(string step, bool success)
    {
        Activity.Current = null;
        using var helper = new ActivityTestHelper();
        var source = success ? Result.Ok(1) : Result.Fail<int>(s_error);

        await s_steps[step](source);

        helper.CapturedActivities.Should().Contain(span => span.DisplayName == OperatorSpan(step), "the operator's own span must be stopped to be reported");
        helper.CapturedActivities.Should().OnlyContain(span => span.ParentId == null && span.IsStopped);
        Activity.Current.Should().BeNull();
    }
}
