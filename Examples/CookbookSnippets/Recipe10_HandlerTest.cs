// Cookbook Recipe 10 — Test: handler test using Trellis.Testing assertions.
namespace CookbookSnippets.Recipe10;

using System;
using System.Threading;
using System.Threading.Tasks;
using CookbookSnippets.Recipe01;
using CookbookSnippets.Recipe02;
using CookbookSnippets.Stubs;
using FluentAssertions;
using Trellis;
using Trellis.Authorization;
using Trellis.Testing;
using Xunit;

public class PlaceOrderHandlerTests
{
#pragma warning disable CA1707 // Cookbook test recipe intentionally shows readable xUnit-style test names.
    [Fact]
    public async Task PlaceOrder_returns_id_on_success()
    {
        var repo = new InMemoryOrderRepository();
        var sut = new PlaceOrderHandler(repo);

        var command = new PlaceOrderCommand(
            OrderId.TryCreate(Guid.NewGuid()).Unwrap(),
            new Money(100m, CurrencyCode.TryCreate("USD").Unwrap()),
            ActorId.TryCreate("alice").Unwrap());

        var result = await sut.Handle(command, CancellationToken.None);

        result.Should().BeSuccess();
        result.Should().HaveValue(repo.Last().Id);
    }

    [Fact]
    public void PlaceOrder_request_adapter_fails_when_currency_invalid()
    {
        var request = new PlaceOrderRequest(Guid.NewGuid(), 100m, "US", "alice"); // 2 chars, not 3

        var result = PlaceOrderCommand.TryCreate(request);

        result.Should().BeFailureOfType<Error.InvalidInput>()
            .Which.Should().HaveFieldError("currency");
    }
#pragma warning restore CA1707
}

internal static class Recipe10TestingSurface
{
    public static void ValidationAssertionSurface()
    {
        var error = Error.InvalidInput.ForField(
            "currency",
            ValidationCodes.StringExactLength,
            "Currency must be 3 characters.");

        ValidationErrorAssertions assertions = error.Should();
        assertions
            .HaveFieldErrorWithDetail("currency", "Currency must be 3 characters.")
            .And.HaveFieldCount(1);

        _ = assertions;
        _ = Result.Fail<int>(error).UnwrapError();
    }

    public static async Task AsyncResultAssertionSurface()
    {
        var idResult = Result.Ok(1);
        var failureResult = Result.Fail<int>(new Error.Unexpected("async-assertion-failure"));
        Task<Result<int>> taskResult = Task.FromResult(idResult);
        ValueTask<Result<int>> valueTaskResult = new(idResult);
        Task<Result<int>> failureTaskResult = Task.FromResult(failureResult);
        ValueTask<Result<int>> failureValueTaskResult = new(failureResult);

        var taskAssertion = await taskResult.BeSuccessAsync();
        var valueTaskAssertion = await valueTaskResult.BeSuccessAsync();
        var taskFailureAssertion = await failureTaskResult.BeFailureAsync();
        var valueTaskFailureAssertion = await failureValueTaskResult.BeFailureAsync();

        _ = (taskAssertion, valueTaskAssertion, taskFailureAssertion, valueTaskFailureAssertion);
    }
}