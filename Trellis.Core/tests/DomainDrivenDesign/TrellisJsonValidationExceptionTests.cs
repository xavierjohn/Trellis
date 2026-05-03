namespace Trellis.Core.Tests.DomainDrivenDesign;

using System.Text.Json;

/// <summary>
/// Tests for <see cref="TrellisJsonValidationException"/> — the marker subclass
/// that <c>Trellis.Asp</c>'s ScalarValueValidationMiddleware uses to surface
/// curated, user-safe validation messages.
/// </summary>
public class TrellisJsonValidationExceptionTests
{
    [Fact]
    public void Default_ctor_produces_an_instance()
    {
        var exception = new TrellisJsonValidationException();

        exception.Should().BeAssignableTo<JsonException>();
    }

    [Fact]
    public void Message_ctor_preserves_message()
    {
        const string message = "Money.Amount must be greater than zero.";

        var exception = new TrellisJsonValidationException(message);

        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void Message_and_inner_ctor_preserves_both()
    {
        var inner = new InvalidOperationException("root cause");
        const string message = "Currency code is invalid.";

        var exception = new TrellisJsonValidationException(message, inner);

        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Is_a_JsonException_so_middleware_can_special_case_it()
    {
        var exception = new TrellisJsonValidationException("invalid");

        // The middleware contract: TrellisJsonValidationException flows through any
        // catch-block that targets JsonException, but is distinguishable by exact type.
        (exception is JsonException).Should().BeTrue();
        exception.GetType().Should().Be<TrellisJsonValidationException>();
    }
}
