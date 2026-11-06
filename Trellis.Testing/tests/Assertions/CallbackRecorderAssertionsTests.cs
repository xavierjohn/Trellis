namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class CallbackRecorderAssertionsTests
{
    private static CallbackRecorder RecorderCalled(int times)
    {
        var recorder = new CallbackRecorder();
        for (var i = 0; i < times; i++)
            recorder.Record();

        return recorder;
    }

    private static CallbackRecorder<string> ArgumentRecorderCalledWith(params string[] arguments)
    {
        var recorder = new CallbackRecorder<string>();
        foreach (var argument in arguments)
            recorder.Record(argument);

        return recorder;
    }

    [Fact]
    public void NotHaveBeenCalled_NeverCalled_Passes() =>
        RecorderCalled(0).Should().NotHaveBeenCalled();

    [Fact]
    public void NotHaveBeenCalled_CalledTwice_FailsWithObservedCount()
    {
        var act = () => RecorderCalled(2).Should().NotHaveBeenCalled();

        act.Should().Throw<XunitException>().WithMessage("*not*called*2*");
    }

    [Fact]
    public void HaveBeenCalledOnce_CalledOnce_Passes() =>
        RecorderCalled(1).Should().HaveBeenCalledOnce();

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HaveBeenCalledOnce_NotExactlyOnce_FailsWithObservedCount(int times)
    {
        var act = () => RecorderCalled(times).Should().HaveBeenCalledOnce();

        act.Should().Throw<XunitException>().WithMessage($"*once*{times}*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void HaveBeenCalledAtLeastOnce_CalledAtLeastOnce_Passes(int times) =>
        RecorderCalled(times).Should().HaveBeenCalledAtLeastOnce();

    [Fact]
    public void HaveBeenCalledAtLeastOnce_NeverCalled_Fails()
    {
        var act = () => RecorderCalled(0).Should().HaveBeenCalledAtLeastOnce();

        act.Should().Throw<XunitException>().WithMessage("*at least once*0*");
    }

    [Fact]
    public void HaveBeenCalledTimes_ExactCount_Passes() =>
        RecorderCalled(3).Should().HaveBeenCalledTimes(3);

    [Fact]
    public void HaveBeenCalledTimes_DifferentCount_FailsWithExpectedAndObserved()
    {
        var act = () => RecorderCalled(3).Should().HaveBeenCalledTimes(2);

        act.Should().Throw<XunitException>().WithMessage("*2*3*");
    }

    [Fact]
    public void HaveBeenCalledTimes_NegativeExpected_ThrowsArgumentOutOfRange()
    {
        var act = () => RecorderCalled(0).Should().HaveBeenCalledTimes(-1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("expected");
    }

    [Fact]
    public void Assertions_Failure_IncludesBecauseReason()
    {
        var act = () => RecorderCalled(0).Should().HaveBeenCalledOnce("the factory is needed for {0}", "startup");

        act.Should().Throw<XunitException>().WithMessage("*because the factory is needed for startup*");
    }

    [Fact]
    public void Assertions_Chain_ThroughAnd() =>
        RecorderCalled(1).Should().HaveBeenCalledOnce().And.HaveBeenCalledAtLeastOnce().And.HaveBeenCalledTimes(1);

    [Fact]
    public void Assertions_NullRecorder_Fails()
    {
        var act = () => ((CallbackRecorder)null!).Should().NotHaveBeenCalled();

        act.Should().Throw<XunitException>().WithMessage("*to not be <null>*");
    }

    [Fact]
    public void Generic_NotHaveBeenCalled_NeverCalled_Passes() =>
        ArgumentRecorderCalledWith().Should().NotHaveBeenCalled();

    [Fact]
    public void Generic_HaveBeenCalledOnce_CalledOnce_Passes() =>
        ArgumentRecorderCalledWith("a").Should().HaveBeenCalledOnce();

    [Fact]
    public void Generic_HaveBeenCalledOnce_CalledTwice_FailsAndListsRecordedArguments()
    {
        var act = () => ArgumentRecorderCalledWith("first", "second").Should().HaveBeenCalledOnce();

        act.Should().Throw<XunitException>().WithMessage("*once*2*first*second*");
    }

    [Fact]
    public void Generic_HaveBeenCalledAtLeastOnce_NeverCalled_Fails()
    {
        var act = () => ArgumentRecorderCalledWith().Should().HaveBeenCalledAtLeastOnce();

        act.Should().Throw<XunitException>().WithMessage("*at least once*0*");
    }

    [Fact]
    public void Generic_HaveBeenCalledTimes_ExactCount_Passes() =>
        ArgumentRecorderCalledWith("a", "b").Should().HaveBeenCalledTimes(2);

    [Fact]
    public void Generic_HaveBeenCalledTimes_NegativeExpected_ThrowsArgumentOutOfRange()
    {
        var act = () => ArgumentRecorderCalledWith().Should().HaveBeenCalledTimes(-1);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("expected");
    }

    [Fact]
    public void Generic_Assertions_Chain_ThroughAnd() =>
        ArgumentRecorderCalledWith("a").Should().HaveBeenCalledOnce().And.HaveBeenCalledAtLeastOnce().And.HaveBeenCalledTimes(1);

    [Fact]
    public void Generic_Assertions_NullRecorder_Fails()
    {
        var act = () => ((CallbackRecorder<string>)null!).Should().NotHaveBeenCalled();

        act.Should().Throw<XunitException>().WithMessage("*to not be <null>*");
    }
}
