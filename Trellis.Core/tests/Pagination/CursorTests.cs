namespace Trellis.Core.Tests.Pagination;

using System;

/// <summary>
/// Unit tests for the <see cref="Cursor"/> opaque pagination token.
/// </summary>
public class CursorTests
{
    [Fact]
    public void Constructor_with_non_empty_token_round_trips_value()
    {
        var cursor = new Cursor("abc123");

        cursor.Token.Should().Be("abc123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_rejects_null_or_empty_token(string? token)
    {
        var act = () => new Cursor(token!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(token));
    }

    [Fact]
    public void Default_struct_throws_invalid_operation_when_token_accessed()
    {
        // default(Cursor) bypasses ctor validation; reading Token surfaces the
        // violation loudly instead of returning a misleading null/empty string.
        var defaulted = default(Cursor);
        var constructed = new Cursor("x");

        var act = () => defaulted.Token;
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*default-constructed*");

        defaulted.Should().NotBe(constructed);
    }

    [Fact]
    public void Cursors_with_same_token_are_equal()
    {
        var a = new Cursor("token-1");
        var b = new Cursor("token-1");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}