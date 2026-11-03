namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class PropagationAssertionsTests
{
    [Fact]
    public async Task ShouldPropagateExceptionAsync_SameInstance_PassesForEveryReceiver()
    {
        var exception = new InvalidOperationException("original");

        await Task.FromException(exception).ShouldPropagateExceptionAsync(exception, cancellationToken: TestContext.Current.CancellationToken);
        await Task.FromException<int>(exception).ShouldPropagateExceptionAsync(exception, cancellationToken: TestContext.Current.CancellationToken);
        await ValueTask.FromException(exception).ShouldPropagateExceptionAsync(exception, cancellationToken: TestContext.Current.CancellationToken);
        await ValueTask.FromException<int>(exception).ShouldPropagateExceptionAsync(exception, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_EqualButDifferentInstance_Fails()
    {
        Func<Task> act = () => Task.FromException(new InvalidOperationException("same"))
            .ShouldPropagateExceptionAsync(new InvalidOperationException("same"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*original*InvalidOperationException*");
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_DifferentExceptionType_NamesBothTypes()
    {
        Func<Task> act = () => Task.FromException(new ArgumentException("other"))
            .ShouldPropagateExceptionAsync(new InvalidOperationException("expected"), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*InvalidOperationException*ArgumentException*");
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_CompletesSuccessfully_Fails()
    {
        Func<Task> act = () => Task.CompletedTask.ShouldPropagateExceptionAsync(new InvalidOperationException(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*completed successfully*");
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_SameToken_PassesForEveryReceiver()
    {
        using var canceled = CanceledToken.Create();

        await canceled.ToTask().ShouldPropagateCancellationAsync(canceled.Token, cancellationToken: TestContext.Current.CancellationToken);
        await canceled.ToTask<int>().ShouldPropagateCancellationAsync(canceled.Token, cancellationToken: TestContext.Current.CancellationToken);
        await canceled.ToValueTask().ShouldPropagateCancellationAsync(canceled.Token, cancellationToken: TestContext.Current.CancellationToken);
        await canceled.ToValueTask<int>().ShouldPropagateCancellationAsync(canceled.Token, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_DifferentToken_Fails()
    {
        using var actual = CanceledToken.Create();
        using var expected = CanceledToken.Create();
        Func<Task> act = () => actual.ToTask().ShouldPropagateCancellationAsync(expected.Token, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*cancellation token*");
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_Faulted_FailsNamingTheFault()
    {
        using var canceled = CanceledToken.Create();
        Func<Task> act = () => Task.FromException(new InvalidOperationException("fault"))
            .ShouldPropagateCancellationAsync(canceled.Token, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*InvalidOperationException*");
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_CompletesSuccessfully_Fails()
    {
        using var canceled = CanceledToken.Create();
        Func<Task> act = () => Task.CompletedTask.ShouldPropagateCancellationAsync(canceled.Token, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*completed successfully*");
    }

    [Fact]
    public async Task Propagation_NullArguments_Throw()
    {
        Func<Task> nullOperation = () => ((Task)null!).ShouldPropagateExceptionAsync(new InvalidOperationException(), cancellationToken: TestContext.Current.CancellationToken);
        Func<Task> nullException = () => Task.CompletedTask.ShouldPropagateExceptionAsync(null!, cancellationToken: TestContext.Current.CancellationToken);

        await nullOperation.Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");
        await nullException.Should().ThrowAsync<ArgumentNullException>().WithParameterName("expected");
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_PendingBeyondTimeout_FailsInsteadOfHanging()
    {
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> act = () => source.Task.ShouldPropagateExceptionAsync(
                new InvalidOperationException(), TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<XunitException>().WithMessage("*did not complete within*");
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_PendingBeyondTimeout_FailsInsteadOfHanging()
    {
        using var canceled = CanceledToken.Create();
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> act = () => source.ValueTask.ShouldPropagateCancellationAsync(
                canceled.Token, TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<XunitException>().WithMessage("*did not complete within*");
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task Propagation_CompletedWithinBound_StillAssertsNormally()
    {
        var exception = new InvalidOperationException("bounded");
        using var canceled = CanceledToken.Create();
        var wait = TimeSpan.FromSeconds(30);
        var token = TestContext.Current.CancellationToken;

        await Task.FromException<int>(exception).ShouldPropagateExceptionAsync(exception, wait, token);
        await ValueTask.FromException(exception).ShouldPropagateExceptionAsync(exception, wait, token);
        await canceled.ToTask().ShouldPropagateCancellationAsync(canceled.Token, wait, token);
        await canceled.ToValueTask<int>().ShouldPropagateCancellationAsync(canceled.Token, wait, token);
    }

    [Fact]
    public async Task Propagation_WaitTokenCanceled_ThrowsOperationCanceledAndLeavesOperationRunning()
    {
        using var waitCanceled = CanceledToken.Create();
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> act = () => source.Task.ShouldPropagateExceptionAsync(
                new InvalidOperationException(), cancellationToken: waitCanceled.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(0);
        }
    }
}