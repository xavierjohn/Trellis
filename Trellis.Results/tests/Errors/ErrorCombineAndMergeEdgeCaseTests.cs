namespace Trellis.Results.Tests.Errors;

using Xunit;
using static Trellis.ValidationError;

/// <summary>
/// Tests for error combining, merging, and edge case scenarios.
/// </summary>
public class ErrorCombineAndMergeEdgeCaseTests
{
    #region ValidationError.Merge Edge Cases

    [Fact]
    public void Merge_WithNull_ShouldReturnSameError()
    {
        // Arrange
        var error = ValidationError.For("email", "Email is required");

        // Act
        var merged = error.Merge(null!);

        // Assert
        merged.Should().BeSameAs(error);
    }

    [Fact]
    public void Merge_WithSelf_ShouldReturnSameError()
    {
        // Arrange
        var error = ValidationError.For("email", "Email is required");

        // Act
        var merged = error.Merge(error);

        // Assert
        merged.Should().BeSameAs(error);
    }

    [Fact]
    public void Merge_TwoErrorsForSameField_ShouldCombineDetailsWithoutDuplicates()
    {
        // Arrange
        var error1 = ValidationError.For("password", "Too short");
        var error2 = ValidationError.For("password", "Must contain number");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.FieldErrors.Should().HaveCount(1);
        merged.FieldErrors[0].FieldName.Should().Be("password");
        merged.FieldErrors[0].Details.Should().HaveCount(2);
        merged.FieldErrors[0].Details.Should().Contain("Too short");
        merged.FieldErrors[0].Details.Should().Contain("Must contain number");
    }

    [Fact]
    public void Merge_WithDuplicateDetails_ShouldNotDuplicateMessages()
    {
        // Arrange
        var error1 = ValidationError.For("password", "Too short");
        var error2 = ValidationError.For("password", "Too short"); // Same message

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.FieldErrors.Should().HaveCount(1);
        merged.FieldErrors[0].Details.Should().HaveCount(1); // No duplicate
        merged.FieldErrors[0].Details[0].Should().Be("Too short");
    }

    [Fact]
    public void Merge_MultipleFields_ShouldPreserveFieldOrder()
    {
        // Arrange
        var error1 = ValidationError.For("email", "Email required")
            .And("password", "Password required");
        var error2 = ValidationError.For("username", "Username required");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.FieldErrors.Should().HaveCount(3);
        merged.FieldErrors[0].FieldName.Should().Be("email");
        merged.FieldErrors[1].FieldName.Should().Be("password");
        merged.FieldErrors[2].FieldName.Should().Be("username");
    }

    [Fact]
    public void Merge_SameCodeAndDetail_ShouldNotDuplicateCodeOrDetail()
    {
        // Arrange
        var error1 = ValidationError.For("field1", "Message 1");
        var error2 = ValidationError.For("field2", "Message 2");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.Code.Should().Be("validation.error"); // Code is not duplicated when same
        merged.Detail.Should().Contain("|"); // Details are combined with | separator
        merged.Detail.Should().Contain("Message 1");
        merged.Detail.Should().Contain("Message 2");
    }

    [Fact]
    public void Merge_DifferentCodes_ShouldCombineCodes()
    {
        // Arrange
        var error1 = new ValidationError(
            [new FieldError("field1", ["Message 1"])],
            "code1",
            "Detail 1");
        var error2 = new ValidationError(
            [new FieldError("field2", ["Message 2"])],
            "code2",
            "Detail 2");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.Code.Should().Contain("code1");
        merged.Code.Should().Contain("code2");
    }

    [Fact]
    public void Merge_DifferentDetails_ShouldCombineDetails()
    {
        // Arrange
        var error1 = new ValidationError(
            [new FieldError("field1", ["Message 1"])],
            "code",
            "Detail 1");
        var error2 = new ValidationError(
            [new FieldError("field2", ["Message 2"])],
            "code",
            "Detail 2");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.Detail.Should().Contain("Detail 1");
        merged.Detail.Should().Contain("Detail 2");
        merged.Detail.Should().Contain("|");
    }

    [Fact]
    public void Merge_WithInstance_ShouldPreferFirstInstance()
    {
        // Arrange
        var error1 = new ValidationError("Message 1", "field1", "code", null, "instance1");
        var error2 = new ValidationError("Message 2", "field2", "code", null, "instance2");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.Instance.Should().Be("instance1");
    }

    [Fact]
    public void Merge_FirstHasNoInstance_ShouldUseSecondInstance()
    {
        // Arrange
        var error1 = new ValidationError("Message 1", "field1", "code", null, null);
        var error2 = new ValidationError("Message 2", "field2", "code", null, "instance2");

        // Act
        var merged = error1.Merge(error2);

        // Assert
        merged.Instance.Should().Be("instance2");
    }

    #endregion

    #region ValidationError.And Edge Cases

    [Fact]
    public void And_SingleMessage_ShouldAddFieldError()
    {
        // Arrange
        var error = ValidationError.For("email", "Email required");

        // Act
        var updated = error.And("password", "Password required");

        // Assert
        updated.FieldErrors.Should().HaveCount(2);
        updated.FieldErrors[1].FieldName.Should().Be("password");
        updated.FieldErrors[1].Details.Should().HaveCount(1);
    }

    [Fact]
    public void And_MultipleMessages_ShouldAddAllMessages()
    {
        // Arrange
        var error = ValidationError.For("email", "Email required");

        // Act
        var updated = error.And("password", "Too short", "Must contain number", "Must contain special char");

        // Assert
        updated.FieldErrors.Should().HaveCount(2);
        updated.FieldErrors[1].FieldName.Should().Be("password");
        updated.FieldErrors[1].Details.Should().HaveCount(3);
        updated.FieldErrors[1].Details.Should().Contain("Too short");
        updated.FieldErrors[1].Details.Should().Contain("Must contain number");
        updated.FieldErrors[1].Details.Should().Contain("Must contain special char");
    }

    [Fact]
    public void And_ChainedCalls_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var error = ValidationError.For("email", "Email required")
            .And("password", "Password required")
            .And("username", "Username required");

        // Assert
        error.FieldErrors.Should().HaveCount(3);
        error.FieldErrors[0].FieldName.Should().Be("email");
        error.FieldErrors[0].Details[0].Should().Be("Email required");
        error.FieldErrors[1].FieldName.Should().Be("password");
        error.FieldErrors[1].Details[0].Should().Be("Password required");
        error.FieldErrors[2].FieldName.Should().Be("username");
        error.FieldErrors[2].Details[0].Should().Be("Username required");
    }

    #endregion

    #region FieldError Constructor Edge Cases

    [Fact]
    public void FieldError_WithEmptyDetailsArray_ShouldThrow()
    {
        // Act
        Action act = () => _ = new FieldError("field", Array.Empty<string>());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one detail message*");
    }

    [Fact]
    public void FieldError_WithNullDetails_ShouldThrow()
    {
        // Act
        Action act = () => { var _ = new FieldError("field", (IEnumerable<string>)null!); };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*items*"); // Actual error message from ToImmutableArray()
    }

    [Fact]
    public void FieldError_WithSingleDetail_ShouldWork()
    {
        // Act
        var fieldError = new FieldError("field", ["Message"]);

        // Assert
        fieldError.FieldName.Should().Be("field");
        fieldError.Details.Should().HaveCount(1);
        fieldError.Details[0].Should().Be("Message");
    }

    [Fact]
    public void FieldError_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var fieldError = new FieldError("email", ["Required", "Invalid format"]);

        // Act
        string str = fieldError.ToString();

        // Assert
        str.Should().Be("email: Required, Invalid format");
    }

    #endregion

    #region ValidationError Constructor Edge Cases

    [Fact]
    public void ValidationError_WithEmptyFieldDetail_ShouldThrow()
    {
        // Act
        Action act = () => { var _ = new ValidationError("", "field", "code"); };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Field detail cannot be null/empty*");
    }

    [Fact]
    public void ValidationError_WithWhitespaceFieldDetail_ShouldThrow()
    {
        // Act
        Action act = () => { var _ = new ValidationError("   ", "field", "code"); };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Field detail cannot be null/empty*");
    }

    [Fact]
    public void ValidationError_WithEmptyFieldErrors_ShouldThrow()
    {
        // Act
        Action act = () =>
        {
            var _ = new ValidationError(
            Array.Empty<FieldError>(),
            "code",
            "detail");
        };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one field error must be supplied*");
    }

    [Fact]
    public void ValidationError_WithNullFieldErrors_ShouldThrow()
    {
        // Act
        Action act = () =>
        {
            var _ = new ValidationError(
            (IEnumerable<FieldError>)null!,
            "code",
            "detail");
        };

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one field error must be supplied*");
    }

    #endregion

    #region Error.Combine Extension Edge Cases

    [Fact]
    public void Combine_TwoValidationErrors_ShouldMerge()
    {
        // Arrange
        var error1 = Error.Validation("Email required", "email");
        var error2 = Error.Validation("Password required", "password");

        // Act
        var combined = error1.Combine(error2);

        // Assert
        combined.Should().BeOfType<ValidationError>();
        var validationError = (ValidationError)combined;
        validationError.FieldErrors.Should().HaveCount(2);
    }

    [Fact]
    public void Combine_ValidationErrorWithNonValidationError_ShouldCreateAggregateError()
    {
        // Arrange
        var error1 = Error.Validation("Email required", "email");
        var error2 = Error.NotFound("User not found");

        // Act
        var combined = error1.Combine(error2);

        // Assert
        combined.Should().BeOfType<AggregateError>();
        var aggregateError = (AggregateError)combined;
        aggregateError.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Combine_TwoNonValidationErrors_ShouldCreateAggregateError()
    {
        // Arrange
        var error1 = Error.NotFound("Not found");
        var error2 = Error.Unauthorized("Unauthorized");

        // Act
        var combined = error1.Combine(error2);

        // Assert
        combined.Should().BeOfType<AggregateError>();
        var aggregateError = (AggregateError)combined;
        aggregateError.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Combine_WithNullCurrentAndNullOther_ThrowsArgumentNullException()
    {
        // Arrange
        Error? error = null;

        // Act
        Action act = () => error.Combine(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AggregateError_SourceListMutation_DoesNotAffectErrors()
    {
        // Arrange
        var sourceErrors = new List<Error>
        {
            Error.NotFound("Not found"),
            Error.Unauthorized("Unauthorized")
        };

        var aggregateError = new AggregateError(sourceErrors);

        // Act
        sourceErrors.Clear();
        sourceErrors.Add(Error.Conflict("Conflict"));

        // Assert
        aggregateError.Errors.Should().HaveCount(2);
        aggregateError.Errors.Should().ContainSingle(error => error is NotFoundError);
        aggregateError.Errors.Should().ContainSingle(error => error is UnauthorizedError);
    }

    [Fact]
    public void Combine_WithAggregateError_ShouldFlattenErrors()
    {
        // Arrange
        var error1 = Error.NotFound("Not found");
        var error2 = Error.Unauthorized("Unauthorized");
        var aggregate = error1.Combine(error2);
        var error3 = Error.Conflict("Conflict");

        // Act
        var combined = aggregate.Combine(error3);

        // Assert
        combined.Should().BeOfType<AggregateError>();
        var aggregateError = (AggregateError)combined;
        aggregateError.Errors.Should().HaveCount(3);
    }

    #endregion

    #region ValidationError Equality Edge Cases

    [Fact]
    public void Equals_TwoValidationErrorsWithSameFieldsAndMessages_ShouldBeEqual()
    {
        // Arrange
        var error1 = ValidationError.For("email", "Required");
        var error2 = ValidationError.For("email", "Required");

        // Act & Assert
        error1.Should().Be(error2);
        error1.Equals(error2).Should().BeTrue();
    }

    [Fact]
    public void Equals_ValidationErrorsWithDifferentFieldOrder_ShouldNotBeEqual()
    {
        // Arrange
        var error1 = ValidationError.For("email", "Required")
            .And("password", "Required");
        var error2 = ValidationError.For("password", "Required")
            .And("email", "Required");

        // Act & Assert
        error1.Should().NotBe(error2);
    }

    [Fact]
    public void Equals_ValidationErrorsWithDifferentMessageOrder_ShouldNotBeEqual()
    {
        // Arrange
        var error1 = ValidationError.For("password", "Too short")
            .And("password", "No number");
        var error2 = ValidationError.For("password", "No number")
            .And("password", "Too short");

        // Act & Assert
        error1.Should().NotBe(error2);
    }

    [Fact]
    public void Equals_ComparedToNull_ShouldReturnFalse()
    {
        // Arrange
        var error = ValidationError.For("email", "Required");

        // Act & Assert
        error.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_ComparedToDifferentType_ShouldReturnFalse()
    {
        // Arrange
        var error = ValidationError.For("email", "Required");
        object other = Error.NotFound("Not found");

        // Act & Assert
        error.Equals(other).Should().BeFalse();
    }

    #endregion

    #region ValidationError HashCode Edge Cases

    [Fact]
    public void GetHashCode_TwoEqualValidationErrors_ShouldHaveSameHashCode()
    {
        // Arrange
        var error1 = ValidationError.For("email", "Required");
        var error2 = ValidationError.For("email", "Required");

        // Act
        int hash1 = error1.GetHashCode();
        int hash2 = error2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void GetHashCode_IsConsistent()
    {
        // Arrange
        var error = ValidationError.For("email", "Required")
            .And("password", "Required");

        // Act
        int hash1 = error.GetHashCode();
        int hash2 = error.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    #endregion

    #region ValidationError ToString Edge Cases

    [Fact]
    public void ToString_SingleFieldError_ShouldFormatCorrectly()
    {
        // Arrange
        var error = ValidationError.For("email", "Required");

        // Act
        string str = error.ToString();

        // Assert
        str.Should().Contain("ValidationError");
        str.Should().Contain("validation.error");
        str.Should().Contain("email: Required");
    }

    [Fact]
    public void ToString_MultipleFieldErrors_ShouldFormatCorrectly()
    {
        // Arrange
        var error = ValidationError.For("email", "Required")
            .And("password", "Too short");

        // Act
        string str = error.ToString();

        // Assert
        str.Should().Contain("email: Required");
        str.Should().Contain("password: Too short");
    }

    [Fact]
    public void ToString_MultipleMessagesForSameField_ShouldFormatCorrectly()
    {
        // Arrange
        var error = ValidationError.For("password", "Too short")
            .And("password", "No number", "No special char");

        // Act
        string str = error.ToString();

        // Assert
        str.Should().Contain("password: Too short, No number, No special char");
    }

    #endregion
}