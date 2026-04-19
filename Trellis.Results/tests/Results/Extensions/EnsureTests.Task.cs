namespace Trellis.Results.Tests.Functional.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class Ensure_Task_Tests
{
    [Fact]
    public async Task Ensure_Task_with_successInput_and_successPredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial message"));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Ok<string>("Success message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task Ensure_Task_with_successInput_and_failurePredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial Result"));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Error message" })));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Error message" });
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_successPredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.Unauthorized() { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Ok<string>("Success message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.Unauthorized() { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_failurePredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Fail<string>(new Error.Unauthorized() { Detail = "Error message" })));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_with_successInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Task.FromResult(Result.Ok<string>("Initial Success message"));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Ok<string>("Success Message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial Success message");
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.Conflict(null, "conflict") { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Ok<string>("Success Message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.Conflict(null, "conflict") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Task.FromResult(Result.Fail<string>(new Error.Conflict(null, "conflict") { Detail = "Initial Error message" }));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Success Message" })));

        result.Should().BeFailure("Initial result and predicate is failure result")
            .Which.Should().Be(new Error.Conflict(null, "conflict") { Detail = "Initial Error message" });
    }
}