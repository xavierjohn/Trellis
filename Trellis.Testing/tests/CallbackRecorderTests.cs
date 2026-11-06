namespace Trellis.Testing.Tests;

public class CallbackRecorderTests
{
    [Fact]
    public void Wrap_ParameterlessCallbacks_RecordsAtEntryAndPreservesReturnIdentity()
    {
        var recorder = new CallbackRecorder();
        var expected = new object();
        var callback = recorder.Wrap(() =>
        {
            recorder.CallCount.Should().Be(1);
            return expected;
        });

        recorder.CallCount.Should().Be(0);
        callback().Should().BeSameAs(expected);
        recorder.Wrap(() => { })();
        recorder.CallCount.Should().Be(2);
    }

    [Fact]
    public void Wrap_ArgumentCallbacks_RecordsOrderBeforeInvocation()
    {
        var recorder = new CallbackRecorder<int>();
        var callback = recorder.Wrap((int value) =>
        {
            recorder.Arguments[^1].Should().Be(value);
            return value * 2;
        });

        callback(3).Should().Be(6);
        callback(1).Should().Be(2);
        recorder.Wrap((int _) => { })(3);

        recorder.CallCount.Should().Be(3);
        recorder.Arguments.Should().Equal([3, 1, 3]);
    }

    [Fact]
    public void Wrap_ThrowingCallbacks_RecordsAttemptAndPreservesException()
    {
        var exception = new InvalidOperationException("test.callback");
        var recorder = new CallbackRecorder();
        var arguments = new CallbackRecorder<string>();
        var callback = recorder.Wrap((Action)(() => throw exception));
        var argumentCallback = arguments.Wrap((Func<string, int>)(_ => throw exception));

        callback.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
        var act = () => argumentCallback("input");
        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
        recorder.CallCount.Should().Be(1);
        arguments.Arguments.Should().Equal(["input"]);
    }

    [Fact]
    public async Task Wrap_AsyncCallbacks_PreservesAwaitableWithoutWaiting()
    {
        var recorder = new CallbackRecorder<int>();
        var source = new ControlledAsync<int>();
        var callback = recorder.Wrap((int _) => source.Task);
        var valueCallback = recorder.Wrap((int _) => source.ValueTask);

        try
        {
            callback(1).Should().BeSameAs(source.Task);
            var valueTask = valueCallback(2);
            valueTask.IsCompleted.Should().BeFalse();
            recorder.Arguments.Should().Equal([1, 2]);
            source.TryComplete(42).Should().BeTrue();
            (await valueTask).Should().Be(42);
        }
        finally
        {
            source.TryComplete(42);
        }
    }

    [Fact]
    public void Record_ConcurrentInvocations_PreservesAllCallsAndSnapshot()
    {
        var calls = new CallbackRecorder();
        var arguments = new CallbackRecorder<int>();
        Parallel.For(0, 100, value =>
        {
            calls.Record();
            arguments.Record(value);
        });
        var snapshot = arguments.Arguments;
        arguments.Record(100);

        calls.CallCount.Should().Be(100);
        arguments.CallCount.Should().Be(101);
        snapshot.Should().BeEquivalentTo(Enumerable.Range(0, 100));
        arguments.Arguments.Should().Contain(100);
    }

    [Fact]
    public void Wrap_NullCallbacks_RejectsWithoutRecording()
    {
        var calls = new CallbackRecorder();
        var arguments = new CallbackRecorder<int>();
        Action[] acts = [
            () => calls.Wrap((Action)null!),
            () => calls.Wrap((Func<int>)null!),
            () => arguments.Wrap((Action<int>)null!),
            () => arguments.Wrap((Func<int, int>)null!)
        ];

        foreach (var act in acts)
            act.Should().Throw<ArgumentNullException>().WithParameterName("callback");

        calls.CallCount.Should().Be(0);
        arguments.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Forbidden_Parameterless_RecordsEntryAndThrowsInvalidOperationWithReason()
    {
        var recorder = new CallbackRecorder();
        var callback = recorder.Forbidden("must not run");

        var act = () => callback();

        act.Should().Throw<InvalidOperationException>().WithMessage("must not run");
        recorder.CallCount.Should().Be(1);
    }

    [Fact]
    public void Forbidden_ParameterlessWithResult_RecordsEntryAndThrowsInvalidOperationWithReason()
    {
        var recorder = new CallbackRecorder();
        Func<int> callback = recorder.Forbidden<int>("no value expected");

        var act = () => callback();

        act.Should().Throw<InvalidOperationException>().WithMessage("no value expected");
        recorder.CallCount.Should().Be(1);
    }

    [Fact]
    public void Forbidden_Argument_RecordsArgumentAndThrowsInvalidOperationWithReason()
    {
        var recorder = new CallbackRecorder<string>();
        Action<string> action = recorder.Forbidden("action forbidden");
        Func<string, int> function = recorder.Forbidden<int>("function forbidden");

        var actionAct = () => action("first");
        var functionAct = () => function("second");

        actionAct.Should().Throw<InvalidOperationException>().WithMessage("action forbidden");
        functionAct.Should().Throw<InvalidOperationException>().WithMessage("function forbidden");
        recorder.Arguments.Should().Equal(["first", "second"]);
    }

    [Fact]
    public void Forbidden_ExceptionSwallowedByCaller_StillCountsTheCall()
    {
        var recorder = new CallbackRecorder();
        var callback = recorder.Forbidden("swallowed");

        try
        {
            callback();
        }
        catch (InvalidOperationException)
        {
        }

        recorder.Should().HaveBeenCalledOnce();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Forbidden_BlankReason_ThrowsArgumentExceptionBeforeAnyCall(string? reason)
    {
        var recorder = new CallbackRecorder();
        var argumentRecorder = new CallbackRecorder<int>();

        var parameterless = () => recorder.Forbidden(reason!);
        var parameterlessResult = () => recorder.Forbidden<int>(reason!);
        var argument = () => argumentRecorder.Forbidden(reason!);
        var argumentResult = () => argumentRecorder.Forbidden<int>(reason!);

        parameterless.Should().Throw<ArgumentException>().WithParameterName("because");
        parameterlessResult.Should().Throw<ArgumentException>().WithParameterName("because");
        argument.Should().Throw<ArgumentException>().WithParameterName("because");
        argumentResult.Should().Throw<ArgumentException>().WithParameterName("because");
        recorder.CallCount.Should().Be(0);
        argumentRecorder.CallCount.Should().Be(0);
    }
}
