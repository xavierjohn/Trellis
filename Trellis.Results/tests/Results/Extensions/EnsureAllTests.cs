namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for EnsureAll extension methods that run all validation checks and accumulate errors.
/// </summary>
public class EnsureAllTests
{
    #region Sync

    [Fact]
    public void EnsureAll_WithNullChecks_ShouldThrowArgumentNullException()
    {
        var sut = Result.Ok("Hello");

        var act = () => sut.EnsureAll(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "checks");
    }

    [Fact]
    public void EnsureAll_WhenResultIsFailure_ShouldReturnOriginalFailure()
    {
        var error = Error.Unexpected("original error");
        var sut = Result.Fail<string>(error);
        var predicateInvoked = false;

        var result = sut.EnsureAll(
            (_ => { predicateInvoked = true; return false; }, Error.Validation("should not appear")));

        result.Should().BeFailure().Which.Should().Be(error);
        predicateInvoked.Should().BeFalse();
    }

    [Fact]
    public void EnsureAll_WhenAllPredicatesPass_ShouldReturnOriginalSuccess()
    {
        var sut = Result.Ok("Hello");

        var result = sut.EnsureAll(
            (v => v.Length > 0, Error.Validation("Name required", "name")),
            (v => v.Length <= 100, Error.Validation("Name too long", "name")));

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public void EnsureAll_WhenOnePredicateFails_ShouldReturnFailureWithThatError()
    {
        var sut = Result.Ok("");

        var result = sut.EnsureAll(
            (v => v.Length > 0, Error.Validation("Name required", "name")),
            (v => true, Error.Validation("Always passes")));

        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public void EnsureAll_WhenMultiplePredicatesFail_ShouldAccumulateAllErrors()
    {
        var sut = Result.Ok("");

        var result = sut.EnsureAll(
            (v => v.Length > 0, Error.Validation("Name required", "name")),
            (v => v.Contains('@'), Error.Validation("Invalid email", "email")),
            (v => true, Error.Validation("Always passes")));

        result.Should().BeFailure();
        var validationError = result.Error.Should().BeOfType<ValidationError>().Subject;
        validationError.FieldErrors.Should().HaveCount(2);
    }

    [Fact]
    public void EnsureAll_WithMixedErrorTypes_ShouldCreateAggregateError()
    {
        var sut = Result.Ok("test");

        var result = sut.EnsureAll(
            (_ => false, Error.Unexpected("unexpected")),
            (_ => false, Error.NotFound("not found")));

        result.Should().BeFailure();
        result.Error.Should().BeOfType<AggregateError>();
    }

    [Fact]
    public void EnsureAll_WithEmptyChecks_ShouldReturnOriginalSuccess()
    {
        var sut = Result.Ok("Hello");

        var result = sut.EnsureAll();

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    #endregion

    #region Task

    [Fact]
    public async Task EnsureAllAsync_Task_WhenAllPredicatesPass_ShouldReturnSuccess()
    {
        var sut = Task.FromResult(Result.Ok("Hello"));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, Error.Validation("required", "name")));

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task EnsureAllAsync_Task_WhenMultipleFail_ShouldAccumulate()
    {
        var sut = Task.FromResult(Result.Ok(""));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, Error.Validation("Name required", "name")),
            (v => v.Contains('@'), Error.Validation("Invalid email", "email")));

        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task EnsureAllAsync_Task_WithNullTask_ShouldThrowArgumentNullException()
    {
        Task<Result<string>> sut = null!;

        var act = async () => await sut.EnsureAllAsync(
            (v => v.Length > 0, Error.Validation("required", "name")));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ValueTask

    [Fact]
    public async Task EnsureAllAsync_ValueTask_WhenAllPredicatesPass_ShouldReturnSuccess()
    {
        var sut = new ValueTask<Result<string>>(Result.Ok("Hello"));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, Error.Validation("required", "name")));

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task EnsureAllAsync_ValueTask_WhenMultipleFail_ShouldAccumulate()
    {
        var sut = new ValueTask<Result<string>>(Result.Ok(""));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, Error.Validation("Name required", "name")),
            (v => v.Contains('@'), Error.Validation("Invalid email", "email")));

        result.Should().BeFailure();
        result.Error.Should().BeOfType<ValidationError>();
    }

    #endregion
}