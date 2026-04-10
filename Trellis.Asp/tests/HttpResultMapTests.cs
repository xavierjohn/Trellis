namespace Trellis.Asp.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// Tests for <see cref="HttpResultExtensions.ToHttpResult{TIn, TOut}(Result{TIn}, Func{TIn, TOut}, TrellisAspOptions?)"/>
/// and its async variants.
/// </summary>
[Collection("TrellisAspOptionsState")]
public class HttpResultMapTests : IDisposable
{
    public HttpResultMapTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    private record UserDto(string Name);

    #region Sync

    [Fact]
    public void ToHttpResult_Success_ReturnsMappedOk()
    {
        var result = Result.Success("alice");

        var response = result.ToHttpResult(s => new UserDto(s.ToUpperInvariant()));

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<UserDto>>();
        var ok = response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<UserDto>>();
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().Be(new UserDto("ALICE"));
    }

    [Fact]
    public void ToHttpResult_Failure_ReturnsError()
    {
        var result = Result.Failure<string>(Error.NotFound("not found"));

        var response = result.ToHttpResult(s => new UserDto(s));

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        var problem = response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        problem.ProblemDetails.Status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToHttpResult_Failure_DoesNotInvokeMap()
    {
        var invoked = false;
        var result = Result.Failure<string>(Error.BadRequest("bad"));

        result.ToHttpResult(s => { invoked = true; return new UserDto(s); });

        invoked.Should().BeFalse();
    }

    [Fact]
    public void ToHttpResult_WithCustomOptions_UsesCustomStatusCode()
    {
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);
        var result = Result.Failure<string>(Error.Domain("oops"));

        var response = result.ToHttpResult(s => new UserDto(s), options);

        response.As<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>()
            .ProblemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    #endregion

    #region Async Task

    [Fact]
    public async Task ToHttpResultAsync_Task_Success_ReturnsMappedOk()
    {
        var resultTask = Task.FromResult(Result.Success("bob"));

        var response = await resultTask.ToHttpResultAsync(s => new UserDto(s.ToUpperInvariant()));

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<UserDto>>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<UserDto>>().Value.Should().Be(new UserDto("BOB"));
    }

    [Fact]
    public async Task ToHttpResultAsync_Task_Failure_ReturnsError()
    {
        var resultTask = Task.FromResult(Result.Failure<string>(Error.NotFound("gone")));

        var response = await resultTask.ToHttpResultAsync(s => new UserDto(s));

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }

    #endregion

    #region Async ValueTask

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_Success_ReturnsMappedOk()
    {
        var resultTask = new ValueTask<Result<string>>(Result.Success("carol"));

        var response = await resultTask.ToHttpResultAsync(s => new UserDto(s.ToUpperInvariant()));

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<UserDto>>();
        response.As<Microsoft.AspNetCore.Http.HttpResults.Ok<UserDto>>().Value.Should().Be(new UserDto("CAROL"));
    }

    [Fact]
    public async Task ToHttpResultAsync_ValueTask_Failure_ReturnsError()
    {
        var resultTask = new ValueTask<Result<string>>(Result.Failure<string>(Error.Conflict("exists")));

        var response = await resultTask.ToHttpResultAsync(s => new UserDto(s));

        response.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }

    #endregion
}
