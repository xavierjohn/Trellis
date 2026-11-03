namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class PropagationAssertionsTests
{
    [Fact]
    public async Task ShouldPropagateExceptionAsync_SameInstance_PassesForEveryReceiver()
    {
        var exception = new InvalidOperationException("original");

        await Task.FromException(exception).ShouldPropagateExceptionAsync(exception);
        await Task.FromException<int>(exception).ShouldPropagateExceptionAsync(exception);
        await ValueTask.FromException(exception).ShouldPropagateExceptionAsync(exception);
        await ValueTask.FromException<int>(exception).ShouldPropagateExceptionAsync(exception);
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_EqualButDifferentInstance_Fails()
    {
        Func<Task> act = () => Task.FromException(new InvalidOperationException("same"))
            .ShouldPropagateExceptionAsync(new InvalidOperationException("same"));

        await act.Should().ThrowAsync<XunitException>().WithMessage("*original*InvalidOperationException*");
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_DifferentExceptionType_NamesBothTypes()
    {
        Func<Task> act = () => Task.FromException(new ArgumentException("other"))
            .ShouldPropagateExceptionAsync(new InvalidOperationException("expected"));

        await act.Should().ThrowAsync<XunitException>().WithMessage("*InvalidOperationException*ArgumentException*");
    }

    [Fact]
    public async Task ShouldPropagateExceptionAsync_CompletesSuccessfully_Fails()
    {
        Func<Task> act = () => Task.CompletedTask.ShouldPropagateExceptionAsync(new InvalidOperationException());

        await act.Should().ThrowAsync<XunitException>().WithMessage("*completed successfully*");
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_SameToken_PassesForEveryReceiver()
    {
        using var canceled = CanceledToken.Create();

        await canceled.ToTask().ShouldPropagateCancellationAsync(canceled.Token);
        await canceled.ToTask<int>().ShouldPropagateCancellationAsync(canceled.Token);
        await canceled.ToValueTask().ShouldPropagateCancellationAsync(canceled.Token);
        await canceled.ToValueTask<int>().ShouldPropagateCancellationAsync(canceled.Token);
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_DifferentToken_Fails()
    {
        using var actual = CanceledToken.Create();
        using var expected = CanceledToken.Create();
        Func<Task> act = () => actual.ToTask().ShouldPropagateCancellationAsync(expected.Token);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*cancellation token*");
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_Faulted_FailsNamingTheFault()
    {
        using var canceled = CanceledToken.Create();
        Func<Task> act = () => Task.FromException(new InvalidOperationException("fault"))
            .ShouldPropagateCancellationAsync(canceled.Token);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*InvalidOperationException*");
    }

    [Fact]
    public async Task ShouldPropagateCancellationAsync_CompletesSuccessfully_Fails()
    {
        using var canceled = CanceledToken.Create();
        Func<Task> act = () => Task.CompletedTask.ShouldPropagateCancellationAsync(canceled.Token);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*completed successfully*");
    }

    [Fact]
    public async Task Propagation_NullArguments_Throw()
    {
        Func<Task> nullOperation = () => ((Task)null!).ShouldPropagateExceptionAsync(new InvalidOperationException());
        Func<Task> nullException = () => Task.CompletedTask.ShouldPropagateExceptionAsync(null!);

        await nullOperation.Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");
        await nullException.Should().ThrowAsync<ArgumentNullException>().WithParameterName("expected");
    }
}