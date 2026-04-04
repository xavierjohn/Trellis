namespace Trellis.Results.Tests.Errors;

using System.Globalization;
using Trellis.Testing;

/// <summary>
/// Tests for the <see cref="IFormattable"/> instance overloads on <see cref="Error"/> factory methods.
/// Verifies that strongly-typed IDs (Guid, int, DateTime, decimal, and ScalarValueObjects)
/// are formatted to invariant-culture strings for the Error.Instance property.
/// </summary>
public class ErrorFormattableInstanceTests
{
    private static readonly Guid TestGuid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    #region NotFound

    [Fact]
    public void NotFound_Guid_FormatsToInvariantString()
    {
        var error = Error.NotFound("Order not found.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<NotFoundError>();
        error.Code.Should().Be("not.found.error");
    }

    [Fact]
    public void NotFound_Int_FormatsToInvariantString()
    {
        var error = Error.NotFound("Item not found.", 42);

        error.Instance.Should().Be("42");
    }

    [Fact]
    public void NotFound_Decimal_FormatsToInvariantString()
    {
        var error = Error.NotFound("Price not found.", 1234.56m);

        error.Instance.Should().Be("1234.56");
    }

    [Fact]
    public void NotFound_DateTime_FormatsToInvariantString()
    {
        var dt = new DateTime(2026, 4, 4, 12, 30, 0, DateTimeKind.Utc);

        var error = Error.NotFound("Entry not found.", dt);

        error.Instance.Should().Be(dt.ToString(null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NotFound_StringOverload_StillWorks()
    {
        var error = Error.NotFound("Not found.", "my-instance");

        error.Instance.Should().Be("my-instance");
    }

    [Fact]
    public void NotFound_NoInstance_StillWorks()
    {
        var error = Error.NotFound("Not found.");

        error.Instance.Should().BeNull();
    }

    #endregion

    #region All factory methods — Guid instance

    [Fact]
    public void BadRequest_Guid_FormatsCorrectly()
    {
        var error = Error.BadRequest("Bad request.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<BadRequestError>();
    }

    [Fact]
    public void Conflict_Guid_FormatsCorrectly()
    {
        var error = Error.Conflict("Conflict.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<ConflictError>();
    }

    [Fact]
    public void PreconditionFailed_Guid_FormatsCorrectly()
    {
        var error = Error.PreconditionFailed("Precondition failed.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<PreconditionFailedError>();
    }

    [Fact]
    public void PreconditionRequired_Guid_FormatsCorrectly()
    {
        var error = Error.PreconditionRequired("Precondition required.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<PreconditionRequiredError>();
    }

    [Fact]
    public void Unauthorized_Guid_FormatsCorrectly()
    {
        var error = Error.Unauthorized("Unauthorized.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<UnauthorizedError>();
    }

    [Fact]
    public void Forbidden_Guid_FormatsCorrectly()
    {
        var error = Error.Forbidden("Forbidden.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<ForbiddenError>();
    }

    [Fact]
    public void Unexpected_Guid_FormatsCorrectly()
    {
        var error = Error.Unexpected("Unexpected.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<UnexpectedError>();
    }

    [Fact]
    public void Domain_Guid_FormatsCorrectly()
    {
        var error = Error.Domain("Domain error.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<DomainError>();
    }

    [Fact]
    public void RateLimit_Guid_FormatsCorrectly()
    {
        var error = Error.RateLimit("Rate limit.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<RateLimitError>();
    }

    [Fact]
    public void ServiceUnavailable_Guid_FormatsCorrectly()
    {
        var error = Error.ServiceUnavailable("Service unavailable.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<ServiceUnavailableError>();
    }

    [Fact]
    public void Gone_Guid_FormatsCorrectly()
    {
        var error = Error.Gone("Gone.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<GoneError>();
    }

    [Fact]
    public void NotAcceptable_Guid_FormatsCorrectly()
    {
        var error = Error.NotAcceptable("Not acceptable.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<NotAcceptableError>();
    }

    [Fact]
    public void UnsupportedMediaType_Guid_FormatsCorrectly()
    {
        var error = Error.UnsupportedMediaType("Unsupported.", TestGuid);

        error.Instance.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
        error.Should().BeOfType<UnsupportedMediaTypeError>();
    }

    #endregion
}
