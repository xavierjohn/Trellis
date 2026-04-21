namespace Trellis.Core.Tests.Functional.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class EnsureTests_Task_Left
{
    [Fact]
    public async Task Ensure_Task_Left_with_errorPredicate_does_not_throw_when_given_result_failure()
    {
        var result = Task.FromResult(Result.Fail<string>(new Error.Conflict(null, "conflict") { Detail = "initial error message" }));
        Func<Task> ensure = () => result.EnsureAsync(
            x => x != "",
            x => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" });

        await ensure.Should().NotThrowAsync<Exception>("passing in a Result.Fail is a valid use case");
    }

    [Fact]
    public async Task Ensure_Task_Left_with_errorPredicate_initial_result_has_failure_state()
    {
        var tResult = Task.FromResult(Result.Fail<string>(new Error.Conflict(null, "conflict") { Detail = "initial error message" }));

        var result = await tResult.EnsureAsync(x => x != "",
            x => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" });

        result.Should().BeFailure("Input Result.Fail should be returned")
            .Which.Should().Be(new Error.Conflict(null, "conflict") { Detail = "initial error message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_errorPredicate_predicate_passes()
    {
        var tResult = Task.FromResult(Result.Ok<string>("initial ok"));

        var result = await tResult.EnsureAsync(x => x != "",
            x => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" });

        result.Should().BeSuccess("Input Result passes predicate condition")
            .Which.Should().Be("initial ok");
    }

    [Fact]
    public async Task Ensure_Task_Left_with_errorPredicate_does_not_execute_error_predicate_when_predicate_passes()
    {
        var tResult = Task.FromResult(Result.Ok<int?>(default(int?)));

        Result<int?> result = await tResult.EnsureAsync(value => !value.HasValue, value => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = $"should be null but found {value}" });

        result.Should().BeSuccess()
            .Which.Should().BeNull();
    }

    [Fact]
    public async Task Ensure_Task_Left_with_errorPredicate_using_errorPredicate()
    {
        var tResult = Task.FromResult(Result.Ok<string>(""));

        var result = await tResult.EnsureAsync(x => x != "",
            x => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" });

        result.Should().BeFailure("Input Result fails predicate condition")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_successInput_and_successPredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial message"));

        var result = await initialResult.EnsureAsync(() => Result.Ok<string>("Success message"));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task Ensure_Task_Left_with_successInput_and_failurePredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial Result"));

        var result = await initialResult.EnsureAsync(() => Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error message" }));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_failureInput_and_successPredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(() => Result.Ok<string>("Success message"));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_failureInput_and_failurePredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(() => Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error message" }));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_successInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial Success message"));

        var result = await initialResult.EnsureAsync(_ => Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error Message" }));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Error Message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_successInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial Success message"));

        var result = await initialResult.EnsureAsync(_ => Result.Ok<string>("Success Message"));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial Success message");
    }

    [Fact]
    public async Task Ensure_Task_Left_with_failureInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(_ => Result.Ok<string>("Success Message"));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_failureInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(_ => Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Success Message" }));

        result.Should().BeFailure("Initial result and predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Left_with_asyncErrorPredicate_does_not_execute_when_initial_result_is_failure()
    {
        var initialError = new Error.Conflict(null, "conflict") { Detail = "initial error message" };
        var tResult = Task.FromResult(Result.Fail<string>(initialError));
        bool predicateCalled = false;
        bool errorPredicateCalled = false;

        var result = await tResult.EnsureAsync(
            x =>
            {
                predicateCalled = true;
                return x != "";
            },
            async x =>
            {
                errorPredicateCalled = true;
                await Task.Yield();
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message" };
            });

        result.Should().BeFailure("Input Result.Fail should be returned")
            .Which.Should().Be(initialError);
        predicateCalled.Should().BeFalse("Predicate should not be called when initial result is failure");
        errorPredicateCalled.Should().BeFalse("Error predicate should not be called when initial result is failure");
    }

    [Fact]
    public async Task Ensure_Task_Left_with_asyncErrorPredicate_predicate_passes()
    {
        var tResult = Task.FromResult(Result.Ok<string>("initial ok"));
        bool errorPredicateCalled = false;

        var result = await tResult.EnsureAsync(
            x => x != "",
            async x =>
            {
                errorPredicateCalled = true;
                await Task.Yield();
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" };
            });

        result.Should().BeSuccess("Input Result passes predicate condition")
            .Which.Should().Be("initial ok");
        errorPredicateCalled.Should().BeFalse("Error predicate should not be called when predicate passes");
    }

    [Fact]
    public async Task Ensure_Task_Left_with_asyncErrorPredicate_predicate_fails()
    {
        var tResult = Task.FromResult(Result.Ok<string>(""));

        var result = await tResult.EnsureAsync(
            x => x != "",
            async x =>
            {
                await Task.Yield();
                return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" };
            });

        result.Should().BeFailure("Input Result fails predicate condition")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "new error message: string should not be empty" });
    }
}