namespace Trellis.Testing.Tests.Assertions;

using System;

/// <summary>
/// Tests for <see cref="IResultAssertions"/> — the non-generic <see cref="IResult"/>
/// assertion entry point. Motivating use case: assertions against the return value of
/// <c>IAuthorizeResource&lt;T&gt;.Authorize</c> / <c>IAuthorizeResourceVia&lt;TOwner&gt;.Authorize</c>,
/// which both return <see cref="IResult"/> by interface contract.
/// </summary>
public class IResultAssertionsTests
{
    // BeSuccess

    [Fact]
    public void BeSuccess_passes_when_result_is_success()
    {
        IResult result = Result.Ok();
        result.Should().BeSuccess();
    }

    [Fact]
    public void BeSuccess_fails_when_result_is_failure()
    {
        IResult result = Result.Fail(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" });
        var act = () => result.Should().BeSuccess();
        act.Should().Throw<Exception>()
            .WithMessage("*to be success*failed with error*");
    }

    // BeFailure

    [Fact]
    public void BeFailure_passes_when_result_is_failure()
    {
        IResult result = Result.Fail(new Error.Forbidden("documents.edit"));
        result.Should().BeFailure();
    }

    [Fact]
    public void BeFailure_fails_when_result_is_success()
    {
        IResult result = Result.Ok();
        var act = () => result.Should().BeFailure();
        act.Should().Throw<Exception>()
            .WithMessage("*to be failure*succeeded*");
    }

    [Fact]
    public void BeFailure_exposes_error_via_Which()
    {
        var err = new Error.Forbidden("documents.edit");
        IResult result = Result.Fail(err);

        result.Should().BeFailure().Which.Should().BeSameAs(err);
    }

    // BeFailureOfType<TError>

    [Fact]
    public void BeFailureOfType_passes_when_error_type_matches_and_returns_typed_error()
    {
        var err = new Error.Forbidden("documents.edit");
        IResult result = Result.Fail(err);

        var which = result.Should().BeFailureOfType<Error.Forbidden>().Which;
        which.Should().BeSameAs(err);
        which.PolicyId.Should().Be("documents.edit");
    }

    [Fact]
    public void BeFailureOfType_fails_when_error_type_does_not_match()
    {
        IResult result = Result.Fail(new Error.Forbidden("documents.edit"));

        var act = () => result.Should().BeFailureOfType<Error.NotFound>();
        act.Should().Throw<Exception>()
            .WithMessage("*to be of type*NotFound*found*Forbidden*");
    }

    // HaveErrorCode / HaveErrorDetail / HaveErrorDetailContaining

    [Fact]
    public void HaveErrorCode_passes_when_error_code_matches()
    {
        // Error.Forbidden overrides Code to PolicyId; gives a meaningful per-instance code.
        IResult result = Result.Fail(new Error.Forbidden("documents.edit"));
        result.Should().HaveErrorCode("documents.edit");
    }

    [Fact]
    public void HaveErrorDetail_passes_when_detail_matches()
    {
        IResult result = Result.Fail(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty)
        {
            Detail = "Invalid request"
        });
        result.Should().HaveErrorDetail("Invalid request");
    }

    [Fact]
    public void HaveErrorDetailContaining_passes_when_detail_contains_substring()
    {
        IResult result = Result.Fail(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty)
        {
            Detail = "User input is invalid"
        });
        result.Should().HaveErrorDetailContaining("invalid");
    }

    // Overload-resolution sanity check — concrete Result<T> still resolves to typed assertions

    [Fact]
    public void Concrete_ResultT_resolves_to_typed_assertions_not_IResultAssertions()
    {
        // If the IResult overload were preferred, this would compile but BeSuccess() would
        // return AndConstraint instead of AndWhichConstraint<_, TValue>, so .Which.Should()
        // would not type-check against int.
        var result = Result.Ok(42);
        result.Should().BeSuccess().Which.Should().Be(42);
    }

    // Null-receiver guard — null IResult surfaces a clean assertion failure, not NRE

    [Fact]
    public void BeSuccess_on_null_receiver_fails_assertion_not_NRE()
    {
        IResult? result = null;
        var act = () => result.Should().BeSuccess();
        act.Should().Throw<Exception>()
            .Which.Should().NotBeOfType<NullReferenceException>("null receiver must surface as a clean assertion failure");
    }

    [Fact]
    public void BeFailure_on_null_receiver_fails_assertion_not_NRE()
    {
        IResult? result = null;
        var act = () => result.Should().BeFailure();
        act.Should().Throw<Exception>()
            .Which.Should().NotBeOfType<NullReferenceException>();
    }

    [Fact]
    public void BeFailureOfType_on_null_receiver_fails_assertion_not_NRE()
    {
        IResult? result = null;
        var act = () => result.Should().BeFailureOfType<Error.Forbidden>();
        act.Should().Throw<Exception>()
            .Which.Should().NotBeOfType<NullReferenceException>();
    }
}