namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class ArgumentNullAssertionsTests
{
    [Fact]
    public void ShouldRejectNull_MatchingParamName_Passes()
    {
        Action act = () => throw new ArgumentNullException("selector");

        act.ShouldRejectNull("selector");
    }

    [Fact]
    public void ShouldRejectNull_WrongParamName_Fails()
    {
        Action act = () => throw new ArgumentNullException("other");

        var assertion = () => act.ShouldRejectNull("selector");

        assertion.Should().Throw<XunitException>().WithMessage("*selector*");
    }

    [Fact]
    public void ShouldRejectNull_NothingThrown_Fails()
    {
        var assertion = () => ((Action)(() => { })).ShouldRejectNull("selector");

        assertion.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRejectNull_ValueReturningCall_MatchingParamName_Passes() =>
        ArgumentNullAssertionsExtensions.ShouldRejectNull(
            () => FailWith<int>(new ArgumentNullException("selector")),
            "selector");

    [Fact]
    public void ShouldRejectNull_ValueReturningCall_WrongParamName_Fails()
    {
        var assertion = () => ArgumentNullAssertionsExtensions.ShouldRejectNull(
            () => FailWith<int>(new ArgumentNullException("other")),
            "selector");

        assertion.Should().Throw<XunitException>().WithMessage("*selector*");
    }

    [Fact]
    public void ShouldRejectNull_ValueReturningCall_NothingThrown_Fails()
    {
        var assertion = () => ArgumentNullAssertionsExtensions.ShouldRejectNull(() => 42, "selector");

        assertion.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRejectNull_ValueReturningCall_NullArguments_Throw()
    {
        var nullCall = () => ((Func<int>)null!).ShouldRejectNull("x");
        var nullName = () => ((Func<int>)(() => 42)).ShouldRejectNull(null!);

        nullCall.Should().Throw<ArgumentNullException>().WithParameterName("act");
        nullName.Should().Throw<ArgumentNullException>().WithParameterName("paramName");
    }

    [Fact]
    public async Task ShouldRejectNullAsync_FaultedTaskWithMatchingParamName_Passes()
    {
        Func<Task> act = () => Task.FromException(new ArgumentNullException("factory"));

        await act.ShouldRejectNullAsync("factory");
    }

    [Fact]
    public async Task ShouldRejectNullAsync_WrongParamName_Fails()
    {
        Func<Task> act = () => Task.FromException(new ArgumentNullException("other"));
        Func<Task> assertion = () => act.ShouldRejectNullAsync("factory");

        await assertion.Should().ThrowAsync<XunitException>().WithMessage("*factory*");
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_AlreadyFaulted_PassesWhileSourceStaysPending()
    {
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> start = async () =>
            {
                ArgumentNullException.ThrowIfNull((object?)null, "factory");
                await source.Task;
            };

            await start.ShouldRejectNullBeforeAwaitAsync("factory");

            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_SynchronousThrow_Passes()
    {
        Func<Task> start = () => throw new ArgumentNullException("factory");

        await start.ShouldRejectNullBeforeAwaitAsync("factory");
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_StillPending_FailsWithoutHanging()
    {
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> start = () => source.Task;
            Func<Task> assertion = () => start.ShouldRejectNullBeforeAwaitAsync("factory");

            await assertion.Should().ThrowAsync<XunitException>().WithMessage("*before awaiting*");
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_WrongParamName_Fails()
    {
        Func<Task> start = () => Task.FromException(new ArgumentNullException("other"));
        Func<Task> assertion = () => start.ShouldRejectNullBeforeAwaitAsync("factory");

        await assertion.Should().ThrowAsync<XunitException>().WithMessage("*factory*");
    }

    [Fact]
    public void ShouldRejectNull_NullArguments_Throw()
    {
        var nullAction = () => ((Action)null!).ShouldRejectNull("x");
        var nullName = () => ((Action)(() => { })).ShouldRejectNull(null!);

        nullAction.Should().Throw<ArgumentNullException>().WithParameterName("act");
        nullName.Should().Throw<ArgumentNullException>().WithParameterName("paramName");
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_StartedAndAlreadyFaulted_PassesWhileSourceStaysPending()
    {
        var source = new ControlledAsync<int>();
        try
        {
            var started = StartGuardedBeforeAwait(source.Task);

            await started.ShouldRejectNullBeforeAwaitAsync("factory");

            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_StartedButPending_FailsWithoutHanging()
    {
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> assertion = () => source.Task.ShouldRejectNullBeforeAwaitAsync("factory");

            await assertion.Should().ThrowAsync<XunitException>().WithMessage("*before awaiting*");
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_StartedWithWrongParamName_Fails()
    {
        var started = Task.FromException(new ArgumentNullException("other"));
        Func<Task> assertion = () => started.ShouldRejectNullBeforeAwaitAsync("factory");

        await assertion.Should().ThrowAsync<XunitException>().WithMessage("*factory*");
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_StartedAndCompletedSuccessfully_Fails()
    {
        Func<Task> assertion = () => Task.CompletedTask.ShouldRejectNullBeforeAwaitAsync("factory");

        await assertion.Should().ThrowAsync<XunitException>();
    }

    [Fact]
    public void ShouldRejectNullBeforeAwaitAsync_SynchronousThrowWhileStarting_EscapesInsteadOfPassing()
    {
        Func<Task> start = () => throw new ArgumentNullException("factory");

        Action act = () => _ = start().ShouldRejectNullBeforeAwaitAsync("factory");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ShouldRejectNullBeforeAwaitAsync_NullStartedTask_Throws()
    {
        Func<Task> act = () => ((Task)null!).ShouldRejectNullBeforeAwaitAsync("factory");

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");
    }

    [Fact]
    public void ShouldRejectArgument_MatchingParamName_Passes()
    {
        Action act = () => throw new ArgumentException("bad", "values");

        act.ShouldRejectArgument("values");
    }

    [Fact]
    public void ShouldRejectArgument_DerivedArgumentException_Passes()
    {
        Action act = () => throw new ArgumentOutOfRangeException("index");

        act.ShouldRejectArgument("index");
    }

    [Fact]
    public void ShouldRejectArgument_WrongParamName_Fails()
    {
        Action act = () => throw new ArgumentException("bad", "other");

        var assertion = () => act.ShouldRejectArgument("values");

        assertion.Should().Throw<XunitException>().WithMessage("*values*");
    }

    [Fact]
    public void ShouldRejectArgument_UnrelatedExceptionType_Fails()
    {
        Action act = () => throw new InvalidOperationException("values");

        var assertion = () => act.ShouldRejectArgument("values");

        assertion.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRejectArgument_NothingThrown_Fails()
    {
        var assertion = () => ((Action)(() => { })).ShouldRejectArgument("values");

        assertion.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRejectArgument_ReturnsAssertionsSoMessageCanBeChained()
    {
        Action act = () => throw new ArgumentException("entry [1] is null", "values");

        act.ShouldRejectArgument("values").WithMessage("*[1]*");
    }

    [Fact]
    public void ShouldRejectArgument_ChainedMessageMismatch_Fails()
    {
        Action act = () => throw new ArgumentException("entry [1] is null", "values");

        var assertion = () => act.ShouldRejectArgument("values").WithMessage("*[2]*");

        assertion.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRejectArgument_ValueReturningCall_MatchingParamName_Passes() =>
        ArgumentNullAssertionsExtensions.ShouldRejectArgument(
            () => FailWith<int>(new ArgumentException("bad", "values")),
            "values");

    [Fact]
    public void ShouldRejectArgument_ValueReturningCall_NothingThrown_Fails()
    {
        var assertion = () => ArgumentNullAssertionsExtensions.ShouldRejectArgument(() => 42, "values");

        assertion.Should().Throw<XunitException>();
    }

    [Fact]
    public void ShouldRejectArgument_NullArguments_Throw()
    {
        var nullAction = () => ((Action)null!).ShouldRejectArgument("x");
        var nullFunc = () => ((Func<int>)null!).ShouldRejectArgument("x");
        var nullName = () => ((Action)(() => { })).ShouldRejectArgument(null!);

        nullAction.Should().Throw<ArgumentNullException>().WithParameterName("act");
        nullFunc.Should().Throw<ArgumentNullException>().WithParameterName("act");
        nullName.Should().Throw<ArgumentNullException>().WithParameterName("paramName");
    }

    private static T FailWith<T>(Exception exception) => throw exception;

    private static async Task StartGuardedBeforeAwait(Task pending)
    {
        ArgumentNullException.ThrowIfNull((object?)null, "factory");
        await pending;
    }
}