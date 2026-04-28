namespace Trellis.Core.Tests.Functional.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class EnsureTests_Task_Right
{
    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_successPredicate()
    {
        var initialResult = Result.Ok("Initial message");

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Ok("Success message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_failurePredicate()
    {
        var initialResult = Result.Ok("Initial Result");

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Fail<string>(new Error.Unauthorized() { Detail = "Error message" })));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.Unauthorized() { Detail = "Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_successPredicate()
    {
        var initialResult = Result.Fail<string>(new Error.Conflict(null, "conflict") { Detail = "Initial Error message" });

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Ok("Success message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.Conflict(null, "conflict") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_failurePredicate()
    {
        var initialResult = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Initial Error message" });

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Fail<string>(new Error.Unauthorized() { Detail = "Error message" })));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Result.Ok("Initial Success message");

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Fail<string>(new Error.Conflict(null, "conflict") { Detail = "Error Message" })));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(new Error.Conflict(null, "conflict") { Detail = "Error Message" });
    }

    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Result.Ok("Initial Success message");

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Ok("Success Message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial Success message");
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" });

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Ok("Success Message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Initial Error message" });

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Fail<string>(new Error.InternalServerError("test") { Detail = "Success Message" })));

        result.Should().BeFailure("Initial result and predicate is failure result")
            .Which.Should().Be(new Error.InternalServerError("test") { Detail = "Initial Error message" });
    }
}