namespace Trellis.Results.Tests;

using Xunit;
using static Trellis.ValidationError;

public class ErrorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_conflict_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Conflict("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<ConflictError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: ConflictError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_not_found_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.NotFound("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<NotFoundError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: NotFoundError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_not_found_error_default()
    {
        // Arrange
        // Act
        var error = Error.NotFound("message");

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("not.found.error");
        error.Should().BeOfType<NotFoundError>();
        error.Instance.Should().BeNull();

        error.ToString().Should().Be($"Type: NotFoundError, Code: not.found.error, Detail: message, Instance: N/A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_Forbidden_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Forbidden("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<ForbiddenError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: ForbiddenError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_Unauthorized_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Unauthorized("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<UnauthorizedError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: UnauthorizedError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_Unexpected_error(string? instance)
    {
        // Arrange
        // Act
        var error = Error.Unexpected("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<UnexpectedError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: UnexpectedError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_Validation_error()
    {
        // Arrange
        // Act
        var error = Error.Validation("field detail.", "field name");

        // Assert
        error.Detail.Should().Be("field detail.");
        error.Code.Should().Be("validation.error");
        error.Should().BeOfType<ValidationError>();
        error.Instance.Should().BeNull();
        var validationError = (ValidationError)error;
        validationError.FieldErrors.Should().HaveCount(1);
        validationError.FieldErrors[0].FieldName.Should().Be("field name");
        validationError.FieldErrors[0].Details.Should().HaveCount(1);
        validationError.FieldErrors[0].Details[0].Should().Be("field detail.");
        validationError.ToString().Should().Be($"Type: ValidationError, Code: validation.error, Detail: field detail., Instance: N/A{Environment.NewLine}field name: field detail.");
    }

    [Fact]
    public void Create_Combine_Validation_error()
    {
        // Arrange
        var error1 = Error.Validation("Too short.", "password");
        FieldError fieldDetails = new("password", ["Not complex.", "Make it complex."]);
        var error2 = Error.Validation([fieldDetails]);

        // Act
        Error combinedError = error1.Combine(error2);

        // Assert
        combinedError.Detail.Should().Be("Too short.");
        combinedError.Code.Should().Be("validation.error");
        combinedError.Should().BeOfType<ValidationError>();
        combinedError.Instance.Should().BeNull();
        var validationError = (ValidationError)combinedError;
        validationError.FieldErrors.Should().HaveCount(1);
        validationError.FieldErrors[0].FieldName.Should().Be("password");
        validationError.FieldErrors[0].Details.Should().HaveCount(3);
        validationError.FieldErrors[0].Details.Should().BeEquivalentTo(["Too short.", "Not complex.", "Make it complex."]);

        var errorSting = validationError.ToString();
        errorSting.Should().Be($"Type: ValidationError, Code: validation.error, Detail: Too short., Instance: N/A{Environment.NewLine}password: Too short., Not complex., Make it complex.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_precondition_failed_error(string? instance)
    {
        // Arrange & Act
        var error = Error.PreconditionFailed("message", "code", instance);

        // Assert
        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<PreconditionFailedError>();
        error.Instance.Should().Be(instance);

        error.ToString().Should().Be($"Type: PreconditionFailedError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_precondition_failed_error_default()
    {
        // Arrange & Act
        var error = Error.PreconditionFailed("Resource has been modified.");

        // Assert
        error.Detail.Should().Be("Resource has been modified.");
        error.Code.Should().Be("precondition.failed.error");
        error.Should().BeOfType<PreconditionFailedError>();
        error.Instance.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_precondition_required_error(string? instance)
    {
        var error = Error.PreconditionRequired("message", "code", instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<PreconditionRequiredError>();
        error.Instance.Should().Be(instance);
        error.ToString().Should().Be($"Type: PreconditionRequiredError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_precondition_required_error_default()
    {
        var error = Error.PreconditionRequired("Precondition required.");

        error.Detail.Should().Be("Precondition required.");
        error.Code.Should().Be("precondition.required.error");
        error.Should().BeOfType<PreconditionRequiredError>();
        error.Instance.Should().BeNull();
    }

    #region Gone Error

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_gone_error(string? instance)
    {
        var error = Error.Gone("message", "code", instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<GoneError>();
        error.Instance.Should().Be(instance);
        error.ToString().Should().Be($"Type: GoneError, Code: code, Detail: message, Instance: {instance ?? "N/A"}");
    }

    [Fact]
    public void Create_gone_error_default()
    {
        var error = Error.Gone("Resource has been permanently removed.");

        error.Detail.Should().Be("Resource has been permanently removed.");
        error.Code.Should().Be("gone.error");
        error.Should().BeOfType<GoneError>();
        error.Instance.Should().BeNull();
    }

    #endregion

    #region Method Not Allowed Error

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_method_not_allowed_error(string? instance)
    {
        var error = Error.MethodNotAllowed("message", ["GET", "POST"], "code", instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<MethodNotAllowedError>();
        error.Instance.Should().Be(instance);
        ((MethodNotAllowedError)error).AllowedMethods.Should().Equal(["GET", "POST"]);
    }

    [Fact]
    public void Create_method_not_allowed_error_default()
    {
        var error = Error.MethodNotAllowed("DELETE is not supported.", ["GET", "PUT"]);

        error.Detail.Should().Be("DELETE is not supported.");
        error.Code.Should().Be("method.not.allowed.error");
        error.Should().BeOfType<MethodNotAllowedError>();
        ((MethodNotAllowedError)error).AllowedMethods.Should().Equal(["GET", "PUT"]);
    }

    #endregion

    #region Not Acceptable Error

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_not_acceptable_error(string? instance)
    {
        var error = Error.NotAcceptable("message", "code", instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<NotAcceptableError>();
        error.Instance.Should().Be(instance);
    }

    [Fact]
    public void Create_not_acceptable_error_default()
    {
        var error = Error.NotAcceptable("No acceptable representation available.");

        error.Detail.Should().Be("No acceptable representation available.");
        error.Code.Should().Be("not.acceptable.error");
        error.Should().BeOfType<NotAcceptableError>();
    }

    #endregion

    #region Unsupported Media Type Error

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_unsupported_media_type_error(string? instance)
    {
        var error = Error.UnsupportedMediaType("message", "code", instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("code");
        error.Should().BeOfType<UnsupportedMediaTypeError>();
        error.Instance.Should().Be(instance);
    }

    [Fact]
    public void Create_unsupported_media_type_error_default()
    {
        var error = Error.UnsupportedMediaType("application/xml is not supported.");

        error.Detail.Should().Be("application/xml is not supported.");
        error.Code.Should().Be("unsupported.media.type.error");
        error.Should().BeOfType<UnsupportedMediaTypeError>();
    }

    #endregion

    #region Content Too Large Error

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_content_too_large_error(string? instance)
    {
        var error = Error.ContentTooLarge("message", instance: instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("content.too.large.error");
        error.Should().BeOfType<ContentTooLargeError>();
        error.Instance.Should().Be(instance);
        ((ContentTooLargeError)error).RetryAfter.Should().BeNull();
    }

    [Fact]
    public void Create_content_too_large_error_with_retry_after()
    {
        var retryAfter = RetryAfterValue.FromSeconds(60);
        var error = Error.ContentTooLarge("Too large.", retryAfter);

        error.Should().BeOfType<ContentTooLargeError>();
        var ctle = (ContentTooLargeError)error;
        ctle.RetryAfter.Should().NotBeNull();
        ctle.RetryAfter!.DelaySeconds.Should().Be(60);
    }

    [Fact]
    public void Create_content_too_large_error_with_custom_code()
    {
        var error = Error.ContentTooLarge("message", "custom.code");

        error.Code.Should().Be("custom.code");
        error.Should().BeOfType<ContentTooLargeError>();
    }

    #endregion

    #region Range Not Satisfiable Error

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void Create_range_not_satisfiable_error(string? instance)
    {
        var error = Error.RangeNotSatisfiable("message", 1024, instance);

        error.Detail.Should().Be("message");
        error.Code.Should().Be("range.not.satisfiable.error");
        error.Should().BeOfType<RangeNotSatisfiableError>();
        error.Instance.Should().Be(instance);
        var rnse = (RangeNotSatisfiableError)error;
        rnse.CompleteLength.Should().Be(1024);
        rnse.Unit.Should().Be("bytes");
    }

    [Fact]
    public void Create_range_not_satisfiable_error_with_custom_unit()
    {
        var error = Error.RangeNotSatisfiable("message", 2048, "custom.code", "items");

        error.Should().BeOfType<RangeNotSatisfiableError>();
        var rnse = (RangeNotSatisfiableError)error;
        rnse.CompleteLength.Should().Be(2048);
        rnse.Unit.Should().Be("items");
        rnse.Code.Should().Be("custom.code");
    }

    #endregion

    #region RetryAfterValue

    [Fact]
    public void RetryAfterValue_FromSeconds_creates_delay()
    {
        var value = RetryAfterValue.FromSeconds(120);

        value.IsDelaySeconds.Should().BeTrue();
        value.IsDate.Should().BeFalse();
        value.DelaySeconds.Should().Be(120);
        value.ToHeaderValue().Should().Be("120");
    }

    [Fact]
    public void RetryAfterValue_FromDate_creates_date()
    {
        var date = new DateTimeOffset(2026, 3, 31, 16, 0, 0, TimeSpan.Zero);
        var value = RetryAfterValue.FromDate(date);

        value.IsDate.Should().BeTrue();
        value.IsDelaySeconds.Should().BeFalse();
        value.Date.Should().Be(date);
        value.ToHeaderValue().Should().Be("Tue, 31 Mar 2026 16:00:00 GMT");
    }

    [Fact]
    public void RetryAfterValue_FromSeconds_negative_throws()
    {
        var act = () => RetryAfterValue.FromSeconds(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RetryAfterValue_DelaySeconds_on_date_throws()
    {
        var value = RetryAfterValue.FromDate(DateTimeOffset.UtcNow);
        var act = () => value.DelaySeconds;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RetryAfterValue_Date_on_seconds_throws()
    {
        var value = RetryAfterValue.FromSeconds(60);
        var act = () => value.Date;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RetryAfterValue_equality()
    {
        var a = RetryAfterValue.FromSeconds(60);
        var b = RetryAfterValue.FromSeconds(60);
        var c = RetryAfterValue.FromSeconds(120);

        a.Equals(b).Should().BeTrue();
        a.Equals(c).Should().BeFalse();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void RetryAfterValue_ToString_matches_header_value()
    {
        var value = RetryAfterValue.FromSeconds(30);
        value.ToString().Should().Be(value.ToHeaderValue());
    }

    #endregion

    #region RateLimitError with RetryAfter

    [Fact]
    public void Create_rate_limit_error_with_retry_after()
    {
        var retryAfter = RetryAfterValue.FromSeconds(60);
        var error = Error.RateLimit("Rate limit exceeded.", retryAfter);

        error.Should().BeOfType<RateLimitError>();
        error.Code.Should().Be("rate.limit.error");
        var rle = (RateLimitError)error;
        rle.RetryAfter.Should().NotBeNull();
        rle.RetryAfter!.DelaySeconds.Should().Be(60);
    }

    [Fact]
    public void Create_rate_limit_error_without_retry_after_has_null()
    {
        var error = Error.RateLimit("Rate limit exceeded.");

        var rle = (RateLimitError)error;
        rle.RetryAfter.Should().BeNull();
    }

    #endregion

    #region ServiceUnavailableError with RetryAfter

    [Fact]
    public void Create_service_unavailable_error_with_retry_after()
    {
        var retryAfter = RetryAfterValue.FromSeconds(300);
        var error = Error.ServiceUnavailable("Under maintenance.", retryAfter);

        error.Should().BeOfType<ServiceUnavailableError>();
        error.Code.Should().Be("service.unavailable.error");
        var sue = (ServiceUnavailableError)error;
        sue.RetryAfter.Should().NotBeNull();
        sue.RetryAfter!.DelaySeconds.Should().Be(300);
    }

    [Fact]
    public void Create_service_unavailable_error_without_retry_after_has_null()
    {
        var error = Error.ServiceUnavailable("Temporarily unavailable.");

        var sue = (ServiceUnavailableError)error;
        sue.RetryAfter.Should().BeNull();
    }

    #endregion
}