namespace Trellis.Results.Tests;

using Trellis.Testing;

/// <summary>
/// TDD tests for the Phase 1a v2 factory rename: Result.Success → Result.Ok, Result.Failure → Result.Fail.
/// See v2-redesign-plan.md §3.1.
/// </summary>
public class ResultOkFailTests
{
    [Fact]
    public void Ok_with_value_returns_success_carrying_value()
    {
        var result = Result.Ok("Hello");

        result.Should().BeSuccess()
            .Which.Should().Be("Hello");
    }

    [Fact]
    public void Ok_with_null_value_returns_success()
    {
        var result = Result.Ok(default(string));

        result.Should().BeSuccess();
    }

    [Fact]
    public void Ok_unit_returns_unit_success()
    {
        var result = Result.Ok();

        result.Should().BeSuccess();
    }

    [Fact]
    public void Fail_with_error_returns_failure_carrying_error()
    {
        var result = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad first name" });

        result.Should().BeFailure()
            .Which.Should().HaveDetail("Bad first name");
    }

    [Fact]
    public void Fail_unit_with_error_returns_unit_failure()
    {
        var result = Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad" });

        result.Should().BeFailure()
            .Which.Should().HaveDetail("Bad");
    }
}
