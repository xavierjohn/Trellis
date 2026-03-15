namespace Trellis.Results.Tests.Functional.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class EnsureTests_Task_Right
{
    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_successPredicate()
    {
        var initialResult = Result.Success("Initial message");

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Success("Success message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_failurePredicate()
    {
        var initialResult = Result.Success("Initial Result");

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Failure<string>(Error.Unauthorized("Error message"))));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(Error.Unauthorized("Error message"));
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_successPredicate()
    {
        var initialResult = Result.Failure<string>(Error.Conflict("Initial Error message"));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Success("Success message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(Error.Conflict("Initial Error message"));
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_failurePredicate()
    {
        var initialResult = Result.Failure<string>(Error.NotFound("Initial Error message"));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Failure<string>(Error.Unauthorized("Error message"))));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(Error.NotFound("Initial Error message"));
    }

    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Result.Success("Initial Success message");

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Failure<string>(Error.Conflict("Error Message"))));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(Error.Conflict("Error Message"));
    }

    [Fact]
    public async Task Ensure_Task_Right_with_successInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Result.Success("Initial Success message");

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Success("Success Message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial Success message");
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Result.Failure<string>(Error.Unexpected("Initial Error message"));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Success("Success Message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(Error.Unexpected("Initial Error message"));
    }

    [Fact]
    public async Task Ensure_Task_Right_with_failureInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Result.Failure<string>(Error.Unexpected("Initial Error message"));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Failure<string>(Error.Unexpected("Success Message"))));

        result.Should().BeFailure("Initial result and predicate is failure result")
            .Which.Should().Be(Error.Unexpected("Initial Error message"));
    }
}