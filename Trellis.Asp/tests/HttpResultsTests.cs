namespace Trellis.Asp.Tests;

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Xunit;

[Collection("TrellisAspOptionsState")]
public class HttpResultsTests : IDisposable
{
    public HttpResultsTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Will_return_Ok_Result()
    {
        // Arrange
        var result = Result.Ok("Test");

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        var okResult = response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>();
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be("Test");
    }

    [Fact]
    public void Will_return_BadRequest_Result()
    {
        // Arrange
        var error = new Error.BadRequest("bad.request") { Detail = "Test" };
        var result = Result.Fail<string>(error);
        var expected = new ProblemDetails
        {
            Title = "Bad Request",
            Detail = "Test",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Status = StatusCodes.Status400BadRequest
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_return_BadRequst_for_validation_failure()
    {
        // Arrange
        var field1 = new FieldViolation(InputPointer.ForProperty("MyField1"), "validation.error") { Detail = "Detail 1" };
        var field2a = new FieldViolation(InputPointer.ForProperty("MyField2"), "validation.error") { Detail = "Detail 2" };
        var field2b = new FieldViolation(InputPointer.ForProperty("MyField2"), "validation.error") { Detail = "More Detail 2" };
        Error errors = new Error.UnprocessableContent(EquatableArray.Create(field1, field2a, field2b)) { Detail = "Some validation failed." };
        var result = Result.Fail(errors);
        var expected = new HttpValidationProblemDetails
        {
            Title = "One or more validation errors occurred.",
            Detail = "Some validation failed.",
            Type = "https://tools.ietf.org/html/rfc4918#section-11.2",
            Status = StatusCodes.Status422UnprocessableEntity,
            Instance = (string?)null,
            Errors = new Dictionary<string, string[]>
            {
                ["MyField1"] = ["Detail 1"],
                ["MyField2"] = ["Detail 2", "More Detail 2"]
            }
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>();
        HttpValidationProblemDetails actualProblemDetails = problemResult.ProblemDetails.As<HttpValidationProblemDetails>();
        actualProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));

    }

    [Fact]
    public void Will_retun_NotFound()
    {
        // Arrange
        var result = Result.Fail(new Error.NotFound(new ResourceRef("Resource", "Chris"?.ToString())) { Detail = "User not found" });
        var expected = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "User not found",
            Instance = (string?)null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Status = StatusCodes.Status404NotFound
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_retun_Conflict()
    {
        // Arrange
        var result = Result.Fail(new Error.Conflict(null, "Jon") { Detail = "Record has changed." });
        var expected = new ProblemDetails
        {
            Title = "Conflict",
            Detail = "Record has changed.",
            Instance = (string?)null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Status = StatusCodes.Status409Conflict
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_retun_Unauthorized()
    {
        // Arrange
        var result = Result.Fail(new Error.Unauthorized() { Detail = "You do not have access." });
        var expected = new ProblemDetails
        {
            Title = "Unauthorized",
            Detail = "You do not have access.",
            Instance = (string?)null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            Status = StatusCodes.Status401Unauthorized
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_return_Forbidden()
    {
        // Arrange
        var result = Result.Fail(new Error.Forbidden("Alice") { Detail = "Access is forbidden." });
        var expected = new ProblemDetails
        {
            Title = "Forbidden",
            Detail = "Access is forbidden.",
            Instance = (string?)null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            Status = StatusCodes.Status403Forbidden
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_return_InternalServerError()
    {
        // Arrange
        var result = Result.Fail(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "An unexpected error occurred." });
        var expected = new ProblemDetails
        {
            Title = "An error occurred while processing your request.",
            Detail = "An internal error occurred.",
            Instance = (string?)null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Status = StatusCodes.Status500InternalServerError
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_return_NoContent_for_Unit_success()
    {
        // Arrange
        var result = Result.Ok();

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        var noContentResult = response.As<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
        noContentResult.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public void Will_return_NotFound_for_Unit_failure()
    {
        // Arrange
        var result = Result.Fail(new Error.NotFound(new ResourceRef("Resource", "UnitResource"?.ToString())) { Detail = "Resource not found" });
        var expected = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "Resource not found",
            Instance = (string?)null,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Status = StatusCodes.Status404NotFound
        };

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Should().BeEquivalentTo(expected, o => o.Excluding(p => p.Extensions));
    }

    [Fact]
    public void Will_return_Conflict_for_Domain_error()
    {
        // Arrange
        var result = Result.Fail(new Error.Conflict(null, "account-123") { Detail = "Cannot withdraw more than account balance" });

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Title.Should().Be("Conflict");
        problemResult.ProblemDetails.Detail.Should().Be("Cannot withdraw more than account balance");
        problemResult.ProblemDetails.Instance.Should().BeNull();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void Will_return_TooManyRequests_for_RateLimit_error()
    {
        // Arrange
        var result = Result.Fail(new Error.TooManyRequests() { Detail = "API rate limit exceeded. Please try again in 60 seconds" });

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Detail.Should().Be("API rate limit exceeded. Please try again in 60 seconds");
        problemResult.ProblemDetails.Instance.Should().BeNull();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void Will_return_ServiceUnavailable_for_ServiceUnavailable_error()
    {
        // Arrange
        var result = Result.Fail(new Error.ServiceUnavailable() { Detail = "Service is under maintenance. Please try again later" });

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problemResult = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problemResult.ContentType.Should().Be("application/problem+json");
        problemResult.ProblemDetails.Detail.Should().Be("An internal error occurred.");
        problemResult.ProblemDetails.Instance.Should().BeNull();
        problemResult.ProblemDetails.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    #region Custom Options

    [Fact]
    public void ToHttpResult_with_custom_options_uses_overridden_mapping()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);
        var result = Result.Fail<string>(new Error.Conflict(null, "domain.violation") { Detail = "Business rule" });

        // Act
        var response = result.ToHttpResult(options);

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToHttpResult_without_options_uses_defaults()
    {
        // Arrange
        var result = Result.Fail<string>(new Error.Conflict(null, "domain.violation") { Detail = "Business rule" });

        // Act
        var response = result.ToHttpResult();

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void ToHttpResult_Error_with_custom_options_uses_overridden_mapping()
    {
        // Arrange
        var options = new TrellisAspOptions();
        options.MapError<Error.Conflict>(StatusCodes.Status422UnprocessableEntity);
        var error = new Error.Conflict(null, "conflict") { Detail = "Already exists" };

        // Act
        var response = error.ToHttpResult(options);

        // Assert
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void ToHttpResult_custom_options_do_not_affect_unmapped_errors()
    {
        // Arrange — override Error.Conflict only
        var options = new TrellisAspOptions();
        options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);
        var result = Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Missing" });

        // Act
        var response = result.ToHttpResult(options);

        // Assert — NotFound still uses default 404
        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    #endregion

    #region AddTrellisAsp integration

    [Fact]
    public void ToHttpResult_without_explicit_options_uses_AddTrellisAsp_configuration()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));

        var result = Result.Fail<string>(new Error.Conflict(null, "domain.violation") { Detail = "Business rule" });

        var response = result.ToHttpResult();

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest,
                "AddTrellisAsp configured Error.Conflict → 400, but ToHttpResult ignores it without explicit options");
    }

    [Fact]
    public async Task ToHttpResult_without_explicit_options_preserves_AddTrellisAsp_configuration_across_execution_context_boundaries()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));

        var result = Result.Fail<string>(new Error.Conflict(null, "domain.violation") { Detail = "Business rule" });

        Task<Microsoft.AspNetCore.Http.IResult> responseTask;
        using (ExecutionContext.SuppressFlow())
            responseTask = Task.Run(() => result.ToHttpResult());

        var response = await responseTask;

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest,
                "AddTrellisAsp configuration should apply even when ToHttpResult runs in a different execution context");
    }

    #endregion

    #region 5xx detail redaction

    [Fact]
    public void ToHttpResult_5xx_error_should_not_leak_internal_detail()
    {
        var result = Result.Fail<string>(
            new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "NullReferenceException at MyService.GetUser line 45" });

        var response = result.ToHttpResult();

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.ProblemDetails.Detail.Should().NotContain("NullReferenceException",
            "5xx responses must not leak internal error details to clients");
    }

    #endregion

    #region ProblemDetails extensions (code/kind/faultId/rules)

    [Fact]
    public void ToHttpResult_populates_extensions_with_code_and_kind()
    {
        var result = Result.Fail(new Error.NotFound(new ResourceRef("User", "42")) { Detail = "User not found" });

        var response = result.ToHttpResult();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        problem.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("not-found");
        problem.ProblemDetails.Extensions.Should().ContainKey("kind").WhoseValue.Should().Be("not-found");
    }

    [Fact]
    public void ToHttpResult_for_InternalServerError_exposes_faultId_in_extensions()
    {
        var result = Result.Fail(new Error.InternalServerError("fault-abc-123") { Detail = "boom" });

        var response = result.ToHttpResult();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        problem.ProblemDetails.Extensions.Should().ContainKey("faultId").WhoseValue.Should().Be("fault-abc-123");
        // Detail is redacted on 5xx, but FaultId remains discoverable via extensions.
        problem.ProblemDetails.Detail.Should().NotContain("boom");
    }

    [Fact]
    public void ToHttpResult_UnprocessableContent_with_only_wrapper_Detail_returns_Problem_with_Detail()
    {
        var result = Result.Fail(
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Cannot withdraw from closed account" });

        var response = result.ToHttpResult();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problem.ProblemDetails.Detail.Should().Be("Cannot withdraw from closed account");
        problem.ProblemDetails.Should().NotBeOfType<HttpValidationProblemDetails>(
            "with no field violations there is nothing to populate Errors with");
    }

    [Fact]
    public void ToHttpResult_UnprocessableContent_with_Rules_populates_extensions_rules_and_uses_ValidationProblem()
    {
        var rule = new RuleViolation("passwords_must_match", Detail: "Passwords don't match");
        var result = Result.Fail(
            new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty, EquatableArray.Create(rule)));

        var response = result.ToHttpResult();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();

        problem.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>();
        problem.ProblemDetails.Extensions.Should().ContainKey("rules");
    }

    #endregion
}