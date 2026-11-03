namespace Trellis.Testing.Tests.Assertions;

using System.Threading.Tasks.Sources;

public class ResultAssertionsAsyncTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task BoundedAssertions_WrongOutcome_ReportsAssertionFailure(int kind, bool valueTask)
    {
        var result = kind == 0 ? Result.Fail<int>(new Error.Forbidden("test.denied")) : Result.Ok(42);
        Func<Task> act = async () =>
        {
            if (kind == 0)
            {
                if (valueTask)
                    await ValueTask.FromResult(result).BeSuccessAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                else
                    await Task.FromResult(result).BeSuccessAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
            else if (kind == 1)
            {
                if (valueTask)
                    await ValueTask.FromResult(result).BeFailureAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                else
                    await Task.FromResult(result).BeFailureAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
            else if (valueTask)
                await ValueTask.FromResult(result).BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            else
                await Task.FromResult(result).BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        };

        await act.Should().ThrowAsync<Exception>().WithMessage("*Expected*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeSuccessAsync_BoundedWait_ReturnsOriginalValue(bool valueTask)
    {
        var expected = new object();
        var result = Result.Ok(expected);

        var constraint = valueTask
            ? await ValueTask.FromResult(result).BeSuccessAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
            : await Task.FromResult(result).BeSuccessAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        constraint.Which.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeFailureAsync_BoundedWait_ReturnsOriginalError(bool valueTask)
    {
        var expected = new Error.Forbidden("test.denied");
        var result = Result.Fail<int>(expected);

        var constraint = valueTask
            ? await ValueTask.FromResult(result).BeFailureAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
            : await Task.FromResult(result).BeFailureAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        constraint.Which.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeFailureOfTypeAsync_BoundedWait_ReturnsOriginalTypedError(bool valueTask)
    {
        var expected = new Error.Forbidden("test.denied");
        var result = Result.Fail<int>(expected);

        var constraint = valueTask
            ? await ValueTask.FromResult(result).BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
            : await Task.FromResult(result).BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        constraint.Which.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeSuccessAsync_Timeout_DoesNotCompleteOrCancelSource(bool valueTask)
    {
        var source = new ControlledAsync<Result<int>>();
        Func<Task> act = async () =>
        {
            if (valueTask)
                await source.ValueTask.BeSuccessAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
            else
                await source.Task.BeSuccessAsync(TimeSpan.Zero, TestContext.Current.CancellationToken);
        };

        try
        {
            await act.Should().ThrowAsync<TimeoutException>();
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(Result.Ok(42));
        }

        (await source.Task.BeSuccessAsync()).Which.Should().Be(42);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeFailureAsync_CanceledWait_PreservesTokenWithoutCancelingSource(bool valueTask)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new ControlledAsync<Result<int>>();
        Func<Task> act = async () =>
        {
            if (valueTask)
                await source.ValueTask.BeFailureAsync(TimeSpan.FromSeconds(30), cancellation.Token);
            else
                await source.Task.BeFailureAsync(TimeSpan.FromSeconds(30), cancellation.Token);
        };

        try
        {
            var failure = await act.Should().ThrowAsync<OperationCanceledException>();
            failure.Which.CancellationToken.Should().Be(cancellation.Token);
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(Result.Fail<int>(new Error.Forbidden("test.denied")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeFailureOfTypeAsync_FaultedSource_PreservesException(bool valueTask)
    {
        var exception = new InvalidOperationException("test.source");
        var source = Task.FromException<Result<int>>(exception);
        Func<Task> act = async () =>
        {
            if (valueTask)
                await new ValueTask<Result<int>>(source).BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            else
                await source.BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        };

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task BoundedAssertions_NullTask_RejectBeforeWaiting()
    {
        Task<Result<int>> source = null!;
        Func<Task>[] acts = [
            async () => await source.BeSuccessAsync(TimeSpan.Zero, CancellationToken.None),
            async () => await source.BeFailureAsync(TimeSpan.Zero, CancellationToken.None),
            async () => await source.BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.Zero, CancellationToken.None)
        ];

        foreach (var act in acts)
            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("resultTask");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BoundedAssertions_ValueTaskSource_ConsumesExactlyOnce(int kind)
    {
        var expected = new Error.Forbidden("test.denied");
        var result = kind == 0 ? Result.Ok(42) : Result.Fail<int>(expected);
        var source = new SingleUseSource(result);
        var valueTask = new ValueTask<Result<int>>(source, source.Version);

        if (kind == 0)
            (await valueTask.BeSuccessAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).Which.Should().Be(42);
        else if (kind == 1)
            (await valueTask.BeFailureAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).Which.Should().BeSameAs(expected);
        else
            (await valueTask.BeFailureOfTypeAsync<int, Error.Forbidden>(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).Which.Should().BeSameAs(expected);

        source.Consumptions.Should().Be(1);
    }

    private sealed class SingleUseSource : IValueTaskSource<Result<int>>
    {
        private ManualResetValueTaskSourceCore<Result<int>> _source;

        public SingleUseSource(Result<int> result) => _source.SetResult(result);

        public int Consumptions { get; private set; }
        public short Version => _source.Version;

        public Result<int> GetResult(short token)
        {
            if (++Consumptions != 1)
                throw new InvalidOperationException("The source was consumed more than once.");
            return _source.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
    }

    [Fact]
    public async Task BeSuccessAsync_Should_Pass_When_Success()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));

        // Act & Assert
        var constraint = await resultTask.BeSuccessAsync();
        constraint.Which.Should().Be(42);
    }

    [Fact]
    public async Task BeSuccessAsync_Should_Fail_When_Failure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var act = async () => await resultTask.BeSuccessAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BeFailureAsync_Should_Pass_When_Failure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureAsync();
        constraint.Which.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task BeFailureAsync_Should_Fail_When_Success()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Ok(42));

        // Act
        var act = async () => await resultTask.BeFailureAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_Should_Pass_When_Type_Matches()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureOfTypeAsync<int, Error.NotFound>();
        constraint.Which.Detail.Should().Be("Not found");
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_Should_Fail_When_Type_Different()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var act = async () => await resultTask.BeFailureOfTypeAsync<int, Error.InvalidInput>();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    #region ValueTask Tests

    [Fact]
    public async Task BeSuccessAsync_ValueTask_Should_Pass_When_Success()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Ok(42));

        // Act & Assert
        var constraint = await resultTask.BeSuccessAsync();
        constraint.Which.Should().Be(42);
    }

    [Fact]
    public async Task BeSuccessAsync_ValueTask_Should_Fail_When_Failure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act
        var act = async () => await resultTask.BeSuccessAsync();

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BeFailureAsync_ValueTask_Should_Pass_When_Failure()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureAsync();
        constraint.Which.Should().BeOfType<Error.NotFound>();
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_ValueTask_Should_Pass_When_Type_Matches()
    {
        // Arrange
        var resultTask = ValueTask.FromResult(Result.Fail<int>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Act & Assert
        var constraint = await resultTask.BeFailureOfTypeAsync<int, Error.NotFound>();
        constraint.Which.Detail.Should().Be("Not found");
    }

    #endregion

    #region Round-N inspection finding (N-T-4) — Task<Result<T>> async assertion null-receiver guards

    [Fact]
    public async Task BeSuccessAsync_Task_Receiver_Null_Throws_ArgumentNullException()
    {
        // Inspection finding N-T-4: previously the Task<Result<T>> async assertion
        // extensions awaited the receiver Task directly; a null receiver produced an
        // opaque NullReferenceException instead of fail-fast ArgumentNullException
        // with the parameter name. UnwrapAsync(this Task<Result<T>>) already had the
        // guard; the assertion extensions did not. ValueTask overloads are value
        // types and don't need the same guard.
        Task<Result<int>> resultTask = null!;

        var act = async () => await resultTask.BeSuccessAsync();

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("resultTask");
    }

    [Fact]
    public async Task BeFailureAsync_Task_Receiver_Null_Throws_ArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        var act = async () => await resultTask.BeFailureAsync();

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("resultTask");
    }

    [Fact]
    public async Task BeFailureOfTypeAsync_Task_Receiver_Null_Throws_ArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        var act = async () => await resultTask.BeFailureOfTypeAsync<int, Error.NotFound>();

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("resultTask");
    }

    #endregion
}