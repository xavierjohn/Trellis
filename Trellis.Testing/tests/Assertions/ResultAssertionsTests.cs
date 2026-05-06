namespace Trellis.Testing.Tests.Assertions;

public class ResultAssertionsTests
{
    [Fact]
    public void BeSuccess_Should_Pass_When_Result_Is_Success()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should().BeSuccess();
    }

    [Fact]
    public void BeSuccess_Should_Fail_When_Result_Is_Failure()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act & Assert
        var act = () => result.Should().BeSuccess();

        act.Should().Throw<Exception>()
            .WithMessage("*to be success*failed with error*");
    }

    [Fact]
    public void BeSuccess_Should_Return_Value_In_Which()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should()
            .BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public void BeFailure_Should_Pass_When_Result_Is_Failure()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act & Assert
        result.Should().BeFailure();
    }

    [Fact]
    public void BeFailure_Should_Fail_When_Result_Is_Success()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        var act = () => result.Should().BeFailure();

        act.Should().Throw<Exception>()
            .WithMessage("*to be failure*succeeded with value*");
    }

    [Fact]
    public void BeFailure_Should_Return_Error_In_Which()
    {
        // Arrange
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" };
        var result = Result.Fail<int>(error);

        // Act & Assert
        result.Should()
            .BeFailure()
            .Which.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public void BeFailureOfType_Should_Pass_When_Error_Matches_Type()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act & Assert
        result.Should().BeFailureOfType<Error.NotFound>();
    }

    [Fact]
    public void BeFailureOfType_Should_Fail_When_Error_Type_Different()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Invalid" });

        // Act & Assert
        var act = () => result.Should().BeFailureOfType<Error.NotFound>();

        act.Should().Throw<Exception>()
            .WithMessage("*to be of type*Error.NotFound*found*Error.UnprocessableContent*");
    }

    [Fact]
    public void BeFailureOfType_Should_Return_Typed_Error_In_Which()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act & Assert
        result.Should()
            .BeFailureOfType<Error.NotFound>()
            .Which.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public void HaveValue_Should_Pass_When_Value_Matches()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should().HaveValue(42);
    }

    [Fact]
    public void HaveValue_Should_Fail_When_Value_Different()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        var act = () => result.Should().HaveValue(99);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void HaveValueMatching_Should_Pass_When_Predicate_Satisfied()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        result.Should().HaveValueMatching(x => x > 40);
    }

    [Fact]
    public void HaveValueMatching_Should_Fail_When_Predicate_Not_Satisfied()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act & Assert
        var act = () => result.Should().HaveValueMatching(x => x > 50);

        act.Should().Throw<Exception>()
            .WithMessage("*value to match predicate*");
    }

    #region HaveValueEquivalentTo Tests

    [Fact]
    public void HaveValueEquivalentTo_Should_Pass_When_Equivalent()
    {
        // Arrange
        var result = Result.Ok(new { Name = "John", Age = 30 });

        // Act & Assert
        result.Should().HaveValueEquivalentTo(new { Name = "John", Age = 30 });
    }

    [Fact]
    public void HaveValueEquivalentTo_Should_Fail_When_Not_Equivalent()
    {
        // Arrange
        var result = Result.Ok(new { Name = "John", Age = 30 });

        // Act
        var act = () => result.Should().HaveValueEquivalentTo(new { Name = "Jane", Age = 25 });

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region HaveErrorCode Tests

    [Fact]
    public void HaveErrorCode_Should_Pass_When_Code_Matches()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act & Assert
        result.Should().HaveErrorCode("not-found");
    }

    [Fact]
    public void HaveErrorCode_Should_Fail_When_Code_Different()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });

        // Act
        var act = () => result.Should().HaveErrorCode("wrong.code");

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region HaveErrorDetail Tests

    [Fact]
    public void HaveErrorDetail_Should_Pass_When_Detail_Matches()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });

        // Act & Assert
        result.Should().HaveErrorDetail("Resource not found");
    }

    [Fact]
    public void HaveErrorDetail_Should_Fail_When_Detail_Different()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Resource not found" });

        // Act
        var act = () => result.Should().HaveErrorDetail("Wrong detail");

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region HaveErrorDetailContaining Tests

    [Fact]
    public void HaveErrorDetailContaining_Should_Pass_When_Contains()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "User with ID 123 not found" });

        // Act & Assert
        result.Should().HaveErrorDetailContaining("123");
    }

    [Fact]
    public void HaveErrorDetailContaining_Should_Fail_When_Not_Contains()
    {
        // Arrange
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "User not found" });

        // Act
        var act = () => result.Should().HaveErrorDetailContaining("456");

        // Assert
        act.Should().Throw<Exception>();
    }

    #endregion

    #region Round-N inspection finding (N-T-3) — BeFailureOfType under AssertionScope

    [Fact]
    public void BeFailureOfType_Wrong_Type_Inside_AssertionScope_Reports_Assertion_Failure_Without_InvalidCastException()
    {
        // Inspection finding N-T-3: previously BeFailureOfType<TError> recorded the
        // type mismatch via Execute.Assertion.ForCondition(...).FailWith(...) but then
        // unconditionally cast `(TError)error!`. Without an active AssertionScope,
        // FailWith aborts before the cast — so the bug never fires. WITH an active
        // FluentAssertions.Execution.AssertionScope, FailWith only RECORDS the failure
        // and execution continues, so the wrong-type cast throws InvalidCastException
        // and masks the intended assertion error.
        //
        // The fix: when the type-check fails, return default(TError)! instead of
        // performing the wrong-type cast — matching the guarded pattern already used
        // in ErrorAssertions.BeOfType<TError>.
        var result = Result.Fail<int>(new Error.NotFound(ResourceRef.For("Order", "1")));

        var act = () =>
        {
            using var scope = new FluentAssertions.Execution.AssertionScope();
            result.Should().BeFailureOfType<Error.Conflict>();
        };

        // Pre-fix: act throws InvalidCastException (Error.NotFound -> Error.Conflict).
        // Post-fix: act throws Xunit.Sdk.XunitException with the assertion message
        // (the AssertionScope flushes the recorded "expected Error.Conflict, but found
        // Error.NotFound" failure when it's disposed).
        var ex = act.Should().Throw<Exception>().Which;
        ex.Should().NotBeOfType<InvalidCastException>(
            "the assertion should produce a clean assertion-failure message, not an InvalidCastException that masks it");
    }

    [Fact]
    public void BeFailureOfType_Wrong_Type_With_Which_Chain_Inside_AssertionScope_Still_Surfaces_Recorded_Assertion_Failure()
    {
        // Pre-commit GPT-5.5 review raised a concern: returning default(TError)! on
        // wrong-type avoids the immediate InvalidCastException, but chaining
        // `.Which.Foo` (or any member access) on the null typed error could throw
        // NullReferenceException inside the using block. NRE WOULD mask the recorded
        // failure if it escaped the scope before scope.Dispose() ran.
        //
        // In practice it doesn't: when the NRE is thrown inside the `using var scope`
        // block, scope.Dispose() runs in its finally and raises an XunitException
        // carrying the AccumulatedFailures collected during the scope. .NET's
        // exception model says when Dispose throws while an exception is in flight,
        // the new exception MASKS the original — so the test sees the XunitException
        // with the recorded "expected Conflict, found NotFound" message, and the NRE
        // is suppressed. Net effect: the assertion failure is surfaced cleanly.
        //
        // This test pins that observed behavior. If a future FluentAssertions or
        // AssertionScope change inverts the masking order, this test will catch it.
        var result = Result.Fail<int>(new Error.NotFound(ResourceRef.For("Order", "1")));

        var act = () =>
        {
            using var scope = new FluentAssertions.Execution.AssertionScope();
            _ = result.Should().BeFailureOfType<Error.Conflict>().Which.Resource;
        };

        var ex = act.Should().Throw<Exception>().Which;
        ex.Should().NotBeOfType<NullReferenceException>(
            "the AssertionScope.Dispose() flush masks the chained-.Which NRE with the recorded assertion failure");
        ex.Message.Should().Contain("Conflict",
            "the recorded \"expected Error.Conflict\" assertion failure must be surfaced");
    }

    #endregion
}