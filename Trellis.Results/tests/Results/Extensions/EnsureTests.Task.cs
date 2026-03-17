namespace Trellis.Results.Tests.Functional.Results.Extensions;

using Trellis;
using Trellis.Testing;

public class Ensure_Task_Tests
{
    [Fact]
    public async Task Ensure_Task_with_successInput_and_successPredicate()
    {
        var initialResult = Task.FromResult(Result.Success<string>("Initial message"));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Success<string>("Success message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial message");
    }

    [Fact]
    public async Task Ensure_Task_with_successInput_and_failurePredicate()
    {
        var initialResult = Task.FromResult(Result.Success<string>("Initial Result"));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Failure<string>(Error.Unexpected("Error message"))));

        result.Should().BeFailure("Predicate is failure result")
            .Which.Should().Be(Error.Unexpected("Error message"));
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_successPredicate()
    {
        var initialResult = Task.FromResult(Result.Failure<string>(Error.Unauthorized("Initial Error message")));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Success<string>("Success message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(Error.Unauthorized("Initial Error message"));
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_failurePredicate()
    {
        var initialResult = Task.FromResult(Result.Failure<string>(Error.Validation("Initial Error message")));

        var result = await initialResult.EnsureAsync(() => Task.FromResult(Result.Failure<string>(Error.Unauthorized("Error message"))));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(Error.Validation("Initial Error message"));
    }

    [Fact]
    public async Task Ensure_Task_with_successInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Task.FromResult(Result.Success<string>("Initial Success message"));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Success<string>("Success Message")));

        result.Should().BeSuccess("Initial result and predicate succeeded")
            .Which.Should().Be("Initial Success message");
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_parameterisedSuccessPredicate()
    {
        var initialResult = Task.FromResult(Result.Failure<string>(Error.Conflict("Initial Error message")));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Success<string>("Success Message")));

        result.Should().BeFailure("Initial result is failure result")
            .Which.Should().Be(Error.Conflict("Initial Error message"));
    }

    [Fact]
    public async Task Ensure_Task_with_failureInput_and_parameterisedFailurePredicate()
    {
        var initialResult = Task.FromResult(Result.Failure<string>(Error.Conflict("Initial Error message")));

        var result = await initialResult.EnsureAsync(_ => Task.FromResult(Result.Failure<string>(Error.Unexpected("Success Message"))));

        result.Should().BeFailure("Initial result and predicate is failure result")
            .Which.Should().Be(Error.Conflict("Initial Error message"));
    }
}