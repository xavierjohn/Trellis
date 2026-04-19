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
        var error = new Error.InternalServerError("test") { Detail = "original error" };
        var sut = Result.Fail<string>(error);
        var predicateInvoked = false;

        var result = sut.EnsureAll(
            (_ => { predicateInvoked = true; return false; }, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "should not appear" }));

        result.Should().BeFailure().Which.Should().Be(error);
        predicateInvoked.Should().BeFalse();
    }

    [Fact]
    public void EnsureAll_WhenAllPredicatesPass_ShouldReturnOriginalSuccess()
    {
        var sut = Result.Ok("Hello");

        var result = sut.EnsureAll(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "Name required" }))),
            (v => v.Length <= 100, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "Name too long" }))));

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public void EnsureAll_WhenOnePredicateFails_ShouldReturnFailureWithThatError()
    {
        var sut = Result.Ok("");

        var result = sut.EnsureAll(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "Name required" }))),
            (v => true, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Always passes" }));

        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public void EnsureAll_WhenMultiplePredicatesFail_ShouldAccumulateAllErrors()
    {
        var sut = Result.Ok("");

        var result = sut.EnsureAll(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "Name required" }))),
            (v => v.Contains('@'), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Invalid email" }))),
            (v => true, new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Always passes" }));

        result.Should().BeFailure();
        var validationError = result.Error!.Should().BeOfType<Error.UnprocessableContent>().Subject;
        validationError.Fields.Items.Should().HaveCount(2);
    }

    [Fact]
    public void EnsureAll_WithMixedErrorTypes_ShouldCreateAggregateError()
    {
        var sut = Result.Ok("test");

        var result = sut.EnsureAll(
            (_ => false, new Error.InternalServerError("test") { Detail = "unexpected" }),
            (_ => false, new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" }));

        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.Aggregate>();
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
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "required" }))));

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task EnsureAllAsync_Task_WhenMultipleFail_ShouldAccumulate()
    {
        var sut = Task.FromResult(Result.Ok(""));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "Name required" }))),
            (v => v.Contains('@'), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Invalid email" }))));

        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task EnsureAllAsync_Task_WithNullTask_ShouldThrowArgumentNullException()
    {
        Task<Result<string>> sut = null!;

        var act = async () => await sut.EnsureAllAsync(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "required" }))));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ValueTask

    [Fact]
    public async Task EnsureAllAsync_ValueTask_WhenAllPredicatesPass_ShouldReturnSuccess()
    {
        var sut = new ValueTask<Result<string>>(Result.Ok("Hello"));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "required" }))));

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task EnsureAllAsync_ValueTask_WhenMultipleFail_ShouldAccumulate()
    {
        var sut = new ValueTask<Result<string>>(Result.Ok(""));

        var result = await sut.EnsureAllAsync(
            (v => v.Length > 0, new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("name"), "validation.error") { Detail = "Name required" }))),
            (v => v.Contains('@'), new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), "validation.error") { Detail = "Invalid email" }))));

        result.Should().BeFailure();
        result.Error!.Should().BeOfType<Error.UnprocessableContent>();
    }

    #endregion
}