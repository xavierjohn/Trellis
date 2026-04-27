// Cookbook Recipe 10 — Test: handler test using Trellis.Testing assertions.
namespace CookbookSnippets.Recipe10;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Trellis;
using CookbookSnippets.Recipe02;
using CookbookSnippets.Stubs;
using Trellis.Testing;
using Xunit;

public class PlaceOrderHandlerTests
{
#pragma warning disable CA1707 // Cookbook test recipe intentionally shows readable xUnit-style test names.
    [Fact]
    public async Task PlaceOrder_returns_id_on_success()
    {
        var repo = new InMemoryOrderRepository();
        var sut  = new PlaceOrderHandler(repo);

        var result = await sut.Handle(
            new PlaceOrderCommand(Guid.NewGuid(), 100m, "USD"),
            CancellationToken.None);

        result.Should().BeSuccess();
        result.Should().HaveValue(repo.Last().Id);
    }

    [Fact]
    public async Task PlaceOrder_fails_with_validation_when_currency_invalid()
    {
        var sut = new PlaceOrderHandler(new InMemoryOrderRepository());

        var result = await sut.Handle(
            new PlaceOrderCommand(Guid.NewGuid(), 100m, "US"),
            CancellationToken.None);

        var error = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        var unwrapped = result.UnwrapError();
        unwrapped.Should().BeOfType<Error.UnprocessableContent>();
        error.Should().HaveFieldError("currency");
    }
#pragma warning restore CA1707
}
