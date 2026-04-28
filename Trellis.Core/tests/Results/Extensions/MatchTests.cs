namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class MatchTests : TestBase
{
    [Fact]
    public void Match_WithNullOnSuccess_ThrowsArgumentNullException()
    {
        var result = Result.Ok(42);

        var act = () => result.Match<int, string>((Func<int, string>)null!, _ => "error");

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "onSuccess");
    }

    [Fact]
    public async Task MatchAsync_TaskResult_WithNullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        Func<Task<string>> act = () => resultTask.MatchAsync(
            onSuccess: _ => "success",
            onFailure: _ => "error");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    [Fact]
    public void Match_Success_InvokesSuccessHandler()
    {
        var result = Result.Ok(42);

        var output = result.Match(
            onSuccess: value => $"success:{value}",
            onFailure: error => $"failure:{error.Code}");

        output.Should().Be("success:42");
    }

    [Fact]
    public void Match_Failure_InvokesFailureHandler()
    {
        var result = Result.Fail<int>(Error1);

        var output = result.Match(
            onSuccess: value => $"success:{value}",
            onFailure: error => $"failure:{error.Code}");

        output.Should().Be($"failure:{Error1.Code}");
    }

    [Fact]
    public void Switch_Success_InvokesOnlySuccessHandler()
    {
        var result = Result.Ok(42);
        var successValue = 0;
        Error? failureError = null;

        result.Switch(
            onSuccess: value => successValue = value,
            onFailure: error => failureError = error);

        successValue.Should().Be(42);
        ReferenceEquals(failureError, null).Should().BeTrue();
    }

    [Fact]
    public void Switch_Failure_InvokesOnlyFailureHandler()
    {
        var result = Result.Fail<int>(Error1);
        var successCalled = false;
        Error? failureError = null;

        result.Switch(
            onSuccess: _ => successCalled = true,
            onFailure: error => failureError = error);

        successCalled.Should().BeFalse();
        ReferenceEquals(failureError, null).Should().BeFalse();
        ReferenceEquals(failureError, Error1).Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_Result_WithCancellationToken_InvokesSuccessHandlerWithToken()
    {
        var result = Result.Ok(42);
        using var cts = new CancellationTokenSource();

        var output = await result.MatchAsync(
            onSuccess: (value, ct) => Task.FromResult(ct == cts.Token ? $"success:{value}" : "wrong-token"),
            onFailure: (error, ct) => Task.FromResult($"failure:{error.Code}"),
            cancellationToken: cts.Token);

        output.Should().Be("success:42");
    }

    [Fact]
    public async Task MatchAsync_Result_WhenSuccessHandlerFaults_TracesErrorStatus()
    {
        using var activityTest = new ActivityTestHelper();
        var result = Result.Ok(42);

        Func<Task> act = async () => await result.MatchAsync(
            onSuccess: _ => Task.FromException<string>(new InvalidOperationException("boom")),
            onFailure: _ => Task.FromResult("error"));

        await act.Should().ThrowAsync<InvalidOperationException>();

        activityTest.AssertActivityCaptured(1);
        activityTest.CapturedActivities.Should().ContainSingle();
        activityTest.CapturedActivities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task SwitchAsync_TaskResult_Failure_InvokesFailureHandler()
    {
        var resultTask = Task.FromResult(Result.Fail<int>(Error1));
        var successCalled = false;
        Error? failureError = null;

        await resultTask.SwitchAsync(
            onSuccess: value =>
            {
                successCalled = true;
                return Task.CompletedTask;
            },
            onFailure: error =>
            {
                failureError = error;
                return Task.CompletedTask;
            });

        successCalled.Should().BeFalse();
        ReferenceEquals(failureError, null).Should().BeFalse();
        ReferenceEquals(failureError, Error1).Should().BeTrue();
    }
}