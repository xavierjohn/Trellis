namespace Trellis.Core.Tests.Results.Extensions;

/// <summary>
/// Tests for <c>Result&lt;T&gt;.AsUnit()</c> and the <see cref="AsUnitExtensions.AsUnitAsync{T}(Task{Result{T}})"/> bridges:
/// the success value is discarded, while a failure keeps the same <see cref="Error"/> instance and its commit intent.
/// </summary>
public class AsUnitTests
{
    private static Error.Conflict MakeError() =>
        new(Resource: null, Code: "domain.violation") { Detail = "bad" };

    private static Error.Unexpected MakeSentinel() =>
        new("default-initialized") { Detail = "Result was default-initialized; use Result.Ok(...) or Result.Fail(...) instead." };

    private static bool PersistsOnFailure(Result<Unit> result) => ((IPersistOnFailure)result).PersistOnFailure;

    #region Sync AsUnit

    [Fact]
    public void AsUnit_success_discards_the_value_and_is_a_plain_success()
    {
        var unit = Result.Ok(42).AsUnit();

        unit.IsSuccess.Should().BeTrue();
        unit.Equals(Result.Ok()).Should().BeTrue();
    }

    [Fact]
    public void AsUnit_failure_keeps_the_same_error_instance()
    {
        var error = MakeError();

        var unit = Result.Fail<int>(error).AsUnit();

        unit.IsFailure.Should().BeTrue();
        ReferenceEquals(unit.Error, error).Should().BeTrue("the failure must carry the original Error, not a copy");
    }

    [Fact]
    public void AsUnit_ordinary_failure_does_not_request_persist()
    {
        var unit = Result.Fail<int>(MakeError()).AsUnit();

        PersistsOnFailure(unit).Should().BeFalse("only FailAfterCommit sources carry the commit intent");
    }

    [Fact]
    public void AsUnit_FailAfterCommit_keeps_the_error_and_the_persist_flag()
    {
        var error = MakeError();

        var unit = Result.FailAfterCommit<int>(error).AsUnit();

        unit.IsFailure.Should().BeTrue();
        ReferenceEquals(unit.Error, error).Should().BeTrue();
        PersistsOnFailure(unit).Should().BeTrue();
    }

    #endregion

    #region AsUnitAsync over Task

    [Fact]
    public async Task AsUnitAsync_Task_success_discards_the_value()
    {
        var unit = await Task.FromResult(Result.Ok("value")).AsUnitAsync();

        unit.IsSuccess.Should().BeTrue();
        unit.Equals(Result.Ok()).Should().BeTrue();
    }

    [Fact]
    public async Task AsUnitAsync_Task_failure_keeps_the_same_error_instance_without_persist()
    {
        var error = MakeError();

        var unit = await Task.FromResult(Result.Fail<string>(error)).AsUnitAsync();

        unit.IsFailure.Should().BeTrue();
        ReferenceEquals(unit.Error, error).Should().BeTrue();
        PersistsOnFailure(unit).Should().BeFalse();
    }

    [Fact]
    public async Task AsUnitAsync_Task_FailAfterCommit_keeps_the_error_and_the_persist_flag()
    {
        var error = MakeError();

        var unit = await Task.FromResult(Result.FailAfterCommit<string>(error)).AsUnitAsync();

        ReferenceEquals(unit.Error, error).Should().BeTrue();
        PersistsOnFailure(unit).Should().BeTrue();
    }

    [Fact]
    public async Task AsUnitAsync_Task_default_result_becomes_the_explicit_sentinel_failure()
    {
        Result<int> defaulted = default;

        var unit = await Task.FromResult(defaulted).AsUnitAsync();

        unit.IsFailure.Should().BeTrue();
        unit.Error.Should().BeOfType<Error.Unexpected>();
        unit.Equals(Result.Fail(MakeSentinel())).Should().BeTrue();
    }

    #endregion

    #region AsUnitAsync over ValueTask

    [Fact]
    public async Task AsUnitAsync_ValueTask_success_discards_the_value()
    {
        var unit = await new ValueTask<Result<string>>(Result.Ok("value")).AsUnitAsync();

        unit.IsSuccess.Should().BeTrue();
        unit.Equals(Result.Ok()).Should().BeTrue();
    }

    [Fact]
    public async Task AsUnitAsync_ValueTask_failure_keeps_the_same_error_instance_without_persist()
    {
        var error = MakeError();

        var unit = await new ValueTask<Result<string>>(Result.Fail<string>(error)).AsUnitAsync();

        unit.IsFailure.Should().BeTrue();
        ReferenceEquals(unit.Error, error).Should().BeTrue();
        PersistsOnFailure(unit).Should().BeFalse();
    }

    [Fact]
    public async Task AsUnitAsync_ValueTask_FailAfterCommit_keeps_the_error_and_the_persist_flag()
    {
        var error = MakeError();

        var unit = await new ValueTask<Result<string>>(Result.FailAfterCommit<string>(error)).AsUnitAsync();

        ReferenceEquals(unit.Error, error).Should().BeTrue();
        PersistsOnFailure(unit).Should().BeTrue();
    }

    [Fact]
    public async Task AsUnitAsync_ValueTask_default_result_becomes_the_explicit_sentinel_failure()
    {
        Result<int> defaulted = default;

        var unit = await new ValueTask<Result<int>>(defaulted).AsUnitAsync();

        unit.IsFailure.Should().BeTrue();
        unit.Equals(Result.Fail(MakeSentinel())).Should().BeTrue();
    }

    #endregion
}
