namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

public class EnsureTests
{
    [Fact]
    public void Ensure_WithNullPredicate_ShouldThrowArgumentNullException()
    {
        var sut = Result.Ok("Hello");

        var act = () => sut.Ensure((Func<bool>)null!, new Error.InternalServerError("test") { Detail = "predicate failed" });

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "predicate");
    }

    [Fact]
    public void Ensure_WithNullErrorPredicate_ShouldThrowArgumentNullException()
    {
        var sut = Result.Ok("Hello");

        var act = () => sut.Ensure(static _ => false, (Func<string, Error>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "errorPredicate");
    }

    [Fact]
    public void Ensure_source_result_is_failure_predicate_do_not_invoked_expect_is_result_failure()
    {
        var sut = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "some error" });

        var result = sut.Ensure(() => true, new Error.InternalServerError("test") { Detail = "can't be this error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "some error" });
    }

    [Fact]
    public void Ensure_source_result_is_success_predicate_is_failed_expected_result_failure()
    {
        var sut = Result.Ok("Hello");

        var result = sut.Ensure(() => false, new Error.InternalServerError("test") { Detail = "predicate failed" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "predicate failed" });
    }

    [Fact]
    public void Ensure_source_result_is_success_predicate_is_passed_expected_result_success()
    {
        var sut = Result.Ok("Hello");

        var result = sut.Ensure(() => true, new Error.InternalServerError("test") { Detail = "can't be this error" });

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task Ensure_source_result_is_failure_async_predicate_do_not_invoked_expect_is_result_failure()
    {
        var sut = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "some error" });

        var result = await sut.EnsureAsync(() => Task.FromResult(true), new Error.InternalServerError("test") { Detail = "can't be this error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "some error" });
    }

    [Fact]
    public async Task Ensure_source_result_is_success_async_predicate_is_failed_expected_result_failure()
    {
        var sut = Result.Ok("Hello");

        var result = await sut.EnsureAsync(() => Task.FromResult(false), new Error.InternalServerError("test") { Detail = "predicate problems" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "predicate problems" });
    }

    [Fact]
    public async Task Ensure_source_result_is_success_async_predicate_is_passed_expected_result_success()
    {
        var sut = Result.Ok("Hello");

        var result = await sut.EnsureAsync(() => Task.FromResult(true), new Error.InternalServerError("test") { Detail = "can't be this error" });

        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task EnsureAsync_Right_Task_WithNullPredicate_ShouldThrowArgumentNullException()
    {
        var sut = Result.Ok("Hello");

        var act = async () => await sut.EnsureAsync((Func<Task<bool>>)null!, new Error.InternalServerError("test") { Detail = "predicate failed" });

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "predicate");
    }

    [Fact]
    public async Task Ensure_task_source_result_is_success_predicate_is_passed_error_predicate_is_not_invoked()
    {
        var sut = Task.FromResult(Result.Ok<int?>(default(int?)));

        var result = await sut.EnsureAsync(value => !value.HasValue,
            value => Task.FromResult((Error)new Error.InternalServerError("test") { Detail = $"should be null but found {value}" }));
        result.Should().BeSuccess().Which.Should().Be(default(int?));
    }

    [Fact]
    public async Task Ensure_task_source_result_is_failure_predicate_do_not_invoked_expect_is_result_failure()
    {
        var sut = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "some error" }));

        var result = await sut.EnsureAsync(() => true, new Error.InternalServerError("test") { Detail = "can't be this error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "some error" });
    }

    [Fact]
    public void Ensure_generic_source_result_is_failure_predicate_do_not_invoked_expect_is_error_result_failure()
    {
        var sut = Result.Fail<TimeSpan>(new Error.InternalServerError("test") { Detail = "some error" });

        var result = sut.Ensure(time => true, new Error.InternalServerError("test") { Detail = "test error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "some error" });
    }

    [Fact]
    public void Ensure_generic_source_result_is_success_predicate_is_failed_expected_error_result_failure()
    {
        var sut = Result.Ok<int>(10101);

        var result = sut.Ensure(i => false, new Error.InternalServerError("test") { Detail = "test error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "test error" });
    }

    [Fact]
    public void Ensure_generic_source_result_is_success_predicate_is_passed_expected_error_result_success()
    {
        var sut = Result.Ok<decimal>(.03m);

        var result = sut.Ensure(d => true, new Error.InternalServerError("test") { Detail = "test error" });

        result.Should().BeSuccess().Which.Should().Be(.03m);
    }

    [Fact]
    public async Task
    Ensure_generic_source_result_is_failure_async_predicate_do_not_invoked_expect_is_error_result_failure()
    {
        var sut = Result.Fail<DateTimeOffset>(new Error.InternalServerError("test") { Detail = "test error" });

        var result = await sut.EnsureAsync(d => Task.FromResult(true), new Error.InternalServerError("test") { Detail = "test ensure error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "test error" });
    }

    [Fact]
    public async Task
    Ensure_generic_source_result_is_success_async_predicate_is_failed_expected_error_result_failure()
    {
        var sut = Result.Ok<int>(333);

        var result = await sut.EnsureAsync(i => Task.FromResult(false), new Error.InternalServerError("test") { Detail = "test ensure error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "test ensure error" });
    }

    [Fact]
    public async Task
    Ensure_generic_source_result_is_success_async_predicate_is_passed_expected_error_result_success()
    {
        var sut = Result.Ok<decimal>(.33m);

        var result = await sut.EnsureAsync(d => Task.FromResult(true), new Error.InternalServerError("test") { Detail = "test error" });

        result.Should().BeSuccess().Which.Should().Be(.33m);
    }

    [Fact]
    public async Task
    Ensure_generic_task_source_result_is_failure_async_predicate_do_not_invoked_expect_is_error_result_failure()
    {
        var sut = Task.FromResult(Result.Fail<TimeSpan>(new Error.InternalServerError("test") { Detail = "some result error" }));

        var result = await sut.EnsureAsync(t => Task.FromResult(true), new Error.InternalServerError("test") { Detail = "test ensure error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "some result error" });
    }

    [Fact]
    public async Task
    Ensure_generic_task_source_result_is_success_async_predicate_is_failed_expected_error_result_failure()
    {
        var sut = Task.FromResult(Result.Ok<long>(333));

        var result = await sut.EnsureAsync(l => Task.FromResult(false), new Error.InternalServerError("test") { Detail = "test ensure error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "test ensure error" });
    }

    [Fact]
    public async Task
    Ensure_generic_task_source_result_is_success_async_predicate_is_passed_expected_error_result_success()
    {
        var sut = Task.FromResult(Result.Ok<double>(.33));

        var result = await sut.EnsureAsync(d => Task.FromResult(true), new Error.InternalServerError("test") { Detail = "test error" });

        result.Should().BeSuccess().Which.Should().Be(.33);
    }

    [Fact]
    public async Task
    Ensure_generic_task_source_result_is_failure_predicate_do_not_invoked_expect_is_error_result_failure()
    {
        var sut = Task.FromResult(Result.Fail<TimeSpan>(new Error.InternalServerError("test") { Detail = "some result error" }));

        var result = await sut.EnsureAsync(t => true, new Error.InternalServerError("test") { Detail = "test ensure error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "some result error" });
    }

    [Fact]
    public void Ensure_with_successInput_and_successPredicate()
    {
        var initialResult = Result.Ok("Initial message");

        var result = initialResult.Ensure(() => Result.Ok<string>("Success message"));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public void Ensure_with_successInput_and_failurePredicate()
    {
        var initialResult = Result.Ok("Initial Result");

        var result = initialResult.Ensure(() => Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error message" }));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Error message" });
    }

    [Fact]
    public void Ensure_with_failureInput_and_successPredicate()
    {
        var initialResult = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" });

        var result = initialResult.Ensure(() => Result.Ok<string>("Success message"));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public void Ensure_with_failureInput_and_failurePredicate()
    {
        var initialResult = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" });

        var result = initialResult.Ensure(() => Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error message" }));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public void Ensure_with_successInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Result.Ok("Initial Success message");

        var result = initialResult.Ensure(_ => Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error Message" }));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Error Message" });
    }

    [Fact]
    public void Ensure_with_successInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Result.Ok("Initial Success message");

        var result = initialResult.Ensure(_ => Result.Ok<string>("Success Message"));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial Success message");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Ensure_string_is_not_whitespace(string? str)
    {
        var result = str.EnsureNotNullOrWhiteSpace(new Error.InternalServerError("test") { Detail = "test error" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "test error" });
    }

    #region EnsureNotNull for reference types

    [Fact]
    public void EnsureNotNull_Success_NotNull_ReturnsSuccess()
    {
        Result<string?> sut = Result.Ok<string?>("hello");

        Result<string> result = sut.EnsureNotNull(new Error.InternalServerError("test") { Detail = "was null" });

        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void EnsureNotNull_Success_Null_ReturnsFailure()
    {
        Result<string?> sut = Result.Ok<string?>(value: null);

        Result<string> result = sut.EnsureNotNull(new Error.InternalServerError("test") { Detail = "was null" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "was null" });
    }

    [Fact]
    public void EnsureNotNull_Failure_ReturnsOriginalFailure()
    {
        Result<string?> sut = Result.Fail<string?>(new Error.InternalServerError("test") { Detail = "original error" });

        Result<string> result = sut.EnsureNotNull(new Error.InternalServerError("test") { Detail = "was null" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "original error" });
    }

    #endregion

    #region EnsureNotNull for value types

    [Fact]
    public void EnsureNotNull_Struct_Success_HasValue_ReturnsSuccess()
    {
        Result<int?> sut = Result.Ok<int?>(42);

        Result<int> result = sut.EnsureNotNull(new Error.InternalServerError("test") { Detail = "was null" });

        result.Should().BeSuccess().Which.Should().Be(42);
    }

    [Fact]
    public void EnsureNotNull_Struct_Success_Null_ReturnsFailure()
    {
        Result<int?> sut = Result.Ok<int?>(value: null);

        Result<int> result = sut.EnsureNotNull(new Error.InternalServerError("test") { Detail = "was null" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "was null" });
    }

    [Fact]
    public void EnsureNotNull_Struct_Failure_ReturnsOriginalFailure()
    {
        Result<int?> sut = Result.Fail<int?>(new Error.InternalServerError("test") { Detail = "original error" });

        Result<int> result = sut.EnsureNotNull(new Error.InternalServerError("test") { Detail = "was null" });

        result.Should().BeFailure().Which.Should().Be(new Error.InternalServerError("test") { Detail = "original error" });
    }

    #endregion
}