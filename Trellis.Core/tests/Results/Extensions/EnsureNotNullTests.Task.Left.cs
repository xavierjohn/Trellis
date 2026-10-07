namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using System.Threading.Tasks.Sources;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;
using static Trellis.Core.Tests.Results.ResultEnsureNotNullTests;

public class EnsureNotNullAsyncTests
{
    public enum ReceiverKind { Task, ValueTask }

    public static IEnumerable<object[]> Forms
    {
        get
        {
            foreach (var kind in Enum.GetValues<ReceiverKind>())
                foreach (var form in Enum.GetValues<GuardForm>())
                    yield return [kind, form];
        }
    }

    public static IEnumerable<object[]> Cases
    {
        get
        {
            foreach (var form in Forms)
                foreach (var present in new[] { false, true })
                    yield return [form[0], form[1], present];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EnsureNotNullAsync_Reference_CompletedSource_ReturnsValueOrError(
        ReceiverKind kind, GuardForm form, bool present)
    {
        string? value = present ? new string('x', 3) : null;
        var error = Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, "field", detail: "Required.");
        var calls = 0;
        using var tracing = new ActivityTestHelper();

        var result = await Reference(kind, form, Task.FromResult(value), error, () => { calls++; return error; });

        if (present)
            result.Should().BeSuccess().Which.Should().BeSameAs(value);
        else if (form == GuardForm.Required)
            result.Should().BeFailure().Which.Should().Be(error);
        else
            result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Be(form == GuardForm.Factory && !present ? 1 : 0);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNull",
            present ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EnsureNotNullAsync_Struct_CompletedSource_UnwrapsValueOrError(
        ReceiverKind kind, GuardForm form, bool present)
    {
        int? value = present ? 0 : null;
        var error = Error.InvalidInput.ForField(ValidationCodes.ValueNotNull, "field", detail: "Required.");
        var calls = 0;
        using var tracing = new ActivityTestHelper();

        var result = await Scalar(kind, form, Task.FromResult(value), error, () => { calls++; return error; });

        if (present)
            result.Should().BeSuccess().Which.Should().Be(0);
        else if (form == GuardForm.Required)
            result.Should().BeFailure().Which.Should().Be(error);
        else
            result.Should().BeFailure().Which.Should().BeSameAs(error);
        calls.Should().Be(form == GuardForm.Factory && !present ? 1 : 0);
        ((IPersistOnFailure)result).PersistOnFailure.Should().BeFalse();
        tracing.AssertActivityCapturedWithStatus("EnsureNotNull",
            present ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EnsureNotNullAsync_Reference_PendingSource_WaitsBeforeGuard(
        ReceiverKind kind, GuardForm form, bool present)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new Error.Forbidden("guard.denied");
        var calls = 0;
        var pending = Reference(kind, form, completion.Task, error, () => { calls++; return error; });

        pending.IsCompleted.Should().BeFalse();
        calls.Should().Be(0);
        completion.SetResult(present ? "value" : null);

        var result = await pending.WaitAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(present);
        calls.Should().Be(form == GuardForm.Factory && !present ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EnsureNotNullAsync_Struct_PendingSource_WaitsBeforeGuard(
        ReceiverKind kind, GuardForm form, bool present)
    {
        var completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new Error.Forbidden("guard.denied");
        var calls = 0;
        var pending = Scalar(kind, form, completion.Task, error, () => { calls++; return error; });

        pending.IsCompleted.Should().BeFalse();
        calls.Should().Be(0);
        completion.SetResult(present ? 0 : null);

        var result = await pending.WaitAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().Be(present);
        calls.Should().Be(form == GuardForm.Factory && !present ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task EnsureNotNullAsync_Source_Fault_PropagatesWithoutFactory(ReceiverKind kind, GuardForm form)
    {
        var exception = new InvalidOperationException("source failed");
        var error = new Error.Forbidden("guard.denied");
        var calls = 0;
        var reference = () => Reference(kind, form, Task.FromException<string?>(exception),
            error, () => { calls++; return error; });
        var scalar = () => Scalar(kind, form, Task.FromException<int?>(exception),
            error, () => { calls++; return error; });

        (await reference.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        (await scalar.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public async Task EnsureNotNullAsync_Source_Cancellation_PropagatesWithoutFactory(ReceiverKind kind, GuardForm form)
    {
        var token = new CancellationToken(canceled: true);
        var error = new Error.Forbidden("guard.denied");
        var calls = 0;
        var reference = () => Reference(kind, form, Task.FromCanceled<string?>(token),
            error, () => { calls++; return error; });
        var scalar = () => Scalar(kind, form, Task.FromCanceled<int?>(token),
            error, () => { calls++; return error; });

        (await reference.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(token);
        (await scalar.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(token);
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ReceiverKind.Task)]
    [InlineData(ReceiverKind.ValueTask)]
    public async Task EnsureNotNullAsync_Factory_NullFactory_ThrowsBeforeAwaiting(ReceiverKind kind)
    {
        var reference = Reference(kind, GuardForm.Factory, new TaskCompletionSource<string?>().Task,
            new Error.Forbidden("guard.denied"), null!);
        var scalar = Scalar(kind, GuardForm.Factory, new TaskCompletionSource<int?>().Task,
            new Error.Forbidden("guard.denied"), null!);

        reference.IsCompleted.Should().BeTrue();
        scalar.IsCompleted.Should().BeTrue();
        var referenceAct = () => reference;
        var scalarAct = () => scalar;
        await referenceAct.Should().ThrowAsync<ArgumentNullException>().WithParameterName("errorFactory");
        await scalarAct.Should().ThrowAsync<ArgumentNullException>().WithParameterName("errorFactory");
    }

    [Theory]
    [InlineData(ReceiverKind.Task)]
    [InlineData(ReceiverKind.ValueTask)]
    public async Task EnsureNotNullAsync_Factory_NullProducedError_Throws(ReceiverKind kind)
    {
        var reference = () => Reference(kind, GuardForm.Factory, Task.FromResult<string?>(null),
            new Error.Forbidden("guard.denied"), () => null!);
        var scalar = () => Scalar(kind, GuardForm.Factory, Task.FromResult<int?>(null),
            new Error.Forbidden("guard.denied"), () => null!);

        await reference.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
        await scalar.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
    }

    [Theory]
    [InlineData(ReceiverKind.Task)]
    [InlineData(ReceiverKind.ValueTask)]
    public async Task EnsureNotNullAsync_Factory_Exception_PropagatesOnlyForMissingValue(ReceiverKind kind)
    {
        var exception = new InvalidOperationException("factory failed");
        Func<Error> factory = () => throw exception;
        var error = new Error.Forbidden("guard.denied");

        (await Reference(kind, GuardForm.Factory, Task.FromResult<string?>("value"), error, factory)).Should().BeSuccess();
        (await Scalar(kind, GuardForm.Factory, Task.FromResult<int?>(0), error, factory)).Should().BeSuccess();
        var reference = () => Reference(kind, GuardForm.Factory, Task.FromResult<string?>(null), error, factory);
        var scalar = () => Scalar(kind, GuardForm.Factory, Task.FromResult<int?>(null), error, factory);

        (await reference.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        (await scalar.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
    }

    [Theory]
    [InlineData(GuardForm.Error)]
    [InlineData(GuardForm.Factory)]
    [InlineData(GuardForm.Required)]
    public async Task EnsureNotNullAsync_Task_NullSource_Throws(GuardForm form)
    {
        var error = new Error.Forbidden("guard.denied");
        var calls = 0;
        var reference = () => Reference(ReceiverKind.Task, form, null!, error, () => { calls++; return error; });
        var scalar = () => Scalar(ReceiverKind.Task, form, null!, error, () => { calls++; return error; });

        await reference.Should().ThrowAsync<ArgumentNullException>().WithParameterName("task");
        await scalar.Should().ThrowAsync<ArgumentNullException>().WithParameterName("task");
        calls.Should().Be(0);
    }

    [Fact]
    public async Task EnsureNotNullAsync_Error_NullLiterals_BindToEagerOverload()
    {
        (await Task.FromResult<string?>("value").EnsureNotNullAsync(null!)).Should().BeSuccess();
        (await Task.FromResult<int?>(0).EnsureNotNullAsync(null!)).Should().BeSuccess();
        (await ValueTask.FromResult<string?>("value").EnsureNotNullAsync(null!)).Should().BeSuccess();
        (await ValueTask.FromResult<int?>(0).EnsureNotNullAsync(null!)).Should().BeSuccess();

        var taskReference = () => Task.FromResult<string?>(null).EnsureNotNullAsync(null!);
        var taskScalar = () => Task.FromResult<int?>(null).EnsureNotNullAsync(null!);
        var valueTaskReference = () => ValueTask.FromResult<string?>(null).EnsureNotNullAsync(null!).AsTask();
        var valueTaskScalar = () => ValueTask.FromResult<int?>(null).EnsureNotNullAsync(null!).AsTask();
        await taskReference.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
        await taskScalar.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
        await valueTaskReference.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
        await valueTaskScalar.Should().ThrowAsync<ArgumentNullException>().WithParameterName("error");
    }

    [Theory]
    [InlineData(ReceiverKind.Task)]
    [InlineData(ReceiverKind.ValueTask)]
    public async Task EnsureNotNullAsync_Required_PresentValue_DoesNotNormalizeField(ReceiverKind kind)
    {
        if (kind == ReceiverKind.Task)
        {
            (await Task.FromResult<string?>("").EnsureNotNullAsync("/invalid~2")).Should().BeSuccess();
            (await Task.FromResult<int?>(0).EnsureNotNullAsync("/invalid~2")).Should().BeSuccess();
        }
        else
        {
            (await ValueTask.FromResult<string?>("").EnsureNotNullAsync("/invalid~2")).Should().BeSuccess();
            (await ValueTask.FromResult<int?>(0).EnsureNotNullAsync("/invalid~2")).Should().BeSuccess();
        }
    }

    [Theory]
    [InlineData(GuardForm.Error, true)]
    [InlineData(GuardForm.Error, false)]
    [InlineData(GuardForm.Factory, true)]
    [InlineData(GuardForm.Factory, false)]
    [InlineData(GuardForm.Required, true)]
    [InlineData(GuardForm.Required, false)]
    public async Task EnsureNotNullAsync_ValueTask_SingleUseSource_IsConsumedOnce(GuardForm form, bool present)
    {
        var referenceSource = new SingleUseSource<string?>();
        var scalarSource = new SingleUseSource<int?>();
        var error = new Error.Forbidden("guard.denied");
        var reference = Reference(form, referenceSource.Task, error, () => error).AsTask();
        var scalar = Scalar(form, scalarSource.Task, error, () => error).AsTask();
        reference.IsCompleted.Should().BeFalse();
        scalar.IsCompleted.Should().BeFalse();

        referenceSource.Complete(present ? "value" : null);
        scalarSource.Complete(present ? 0 : null);

        (await reference.WaitAsync(TestContext.Current.CancellationToken)).IsSuccess.Should().Be(present);
        (await scalar.WaitAsync(TestContext.Current.CancellationToken)).IsSuccess.Should().Be(present);
        referenceSource.ResultReads.Should().Be(1);
        scalarSource.ResultReads.Should().Be(1);
    }

    [Fact]
    public async Task EnsureNotNullAsync_ValueTask_DefaultSource_IsMissing()
    {
        (await default(ValueTask<string?>).EnsureNotNullAsync("field"))
            .Should().BeFailureOfType<Error.InvalidInput>();
        (await default(ValueTask<int?>).EnsureNotNullAsync("field"))
            .Should().BeFailureOfType<Error.InvalidInput>();
    }

    private static Task<Result<string>> Reference(
        ReceiverKind kind, GuardForm form, Task<string?> source, Error error, Func<Error> factory) =>
        kind == ReceiverKind.ValueTask ? Reference(form, new ValueTask<string?>(source), error, factory).AsTask()
        : form switch
        {
            GuardForm.Error => source.EnsureNotNullAsync(error),
            GuardForm.Factory => source.EnsureNotNullAsync(factory),
            GuardForm.Required => source.EnsureNotNullAsync("field", "Required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

    private static Task<Result<int>> Scalar(
        ReceiverKind kind, GuardForm form, Task<int?> source, Error error, Func<Error> factory) =>
        kind == ReceiverKind.ValueTask ? Scalar(form, new ValueTask<int?>(source), error, factory).AsTask()
        : form switch
        {
            GuardForm.Error => source.EnsureNotNullAsync(error),
            GuardForm.Factory => source.EnsureNotNullAsync(factory),
            GuardForm.Required => source.EnsureNotNullAsync("field", "Required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

    private static ValueTask<Result<string>> Reference(
        GuardForm form, ValueTask<string?> source, Error error, Func<Error> factory) => form switch
        {
            GuardForm.Error => source.EnsureNotNullAsync(error),
            GuardForm.Factory => source.EnsureNotNullAsync(factory),
            GuardForm.Required => source.EnsureNotNullAsync("field", "Required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

    private static ValueTask<Result<int>> Scalar(
        GuardForm form, ValueTask<int?> source, Error error, Func<Error> factory) => form switch
        {
            GuardForm.Error => source.EnsureNotNullAsync(error),
            GuardForm.Factory => source.EnsureNotNullAsync(factory),
            GuardForm.Required => source.EnsureNotNullAsync("field", "Required."),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

    private sealed class SingleUseSource<T> : IValueTaskSource<T>
    {
        private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
        public int ResultReads { get; private set; }
        public ValueTask<T> Task => new(this, _core.Version);
        public void Complete(T value) => _core.SetResult(value);
        public T GetResult(short token)
        {
            ResultReads++;
            return _core.GetResult(token);
        }
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}
