namespace Trellis.Testing.AspNetCore.Tests.Http;

using System;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Public-constructor coverage for <see cref="HttpFileAssertionException"/>. The
/// <see cref="HttpFileAssertions"/> API only ever constructs it with a message, so the
/// parameterless ctor and the (message, inner) ctor are exercised directly here.
/// </summary>
public class HttpFileAssertionExceptionTests
{
    [Fact]
    public void Parameterless_ctor_produces_usable_exception()
    {
        var ex = new HttpFileAssertionException();
        FluentActions.Invoking(() => throw ex)
            .Should().Throw<HttpFileAssertionException>();
    }

    [Fact]
    public void Message_ctor_preserves_message()
    {
        var ex = new HttpFileAssertionException("boom");
        ex.Message.Should().Be("boom");
    }

    [Fact]
    public void Message_and_inner_ctor_preserves_both()
    {
        var inner = new InvalidOperationException("cause");
        var ex = new HttpFileAssertionException("wrapped", inner);

        ex.Message.Should().Be("wrapped");
        ex.InnerException.Should().BeSameAs(inner);
    }
}
