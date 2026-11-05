namespace Trellis.Core.Tests.Maybes;

using Trellis.Testing;

public class OptionalNonBlankTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00A0")]
    [InlineData("\u2003")]
    public void OptionalNonBlank_BlankInput_ReturnsSuccessfulNoneWithoutInvokingFactory(string? input)
    {
        var calls = new CallbackRecorder<string>();

        var result = Maybe.OptionalNonBlank(input, calls.Wrap(value => Result.Ok(value)));

        result.Should().BeSuccess().Which.HasNoValue.Should().BeTrue();
        calls.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("value")]
    [InlineData(" value ")]
    [InlineData("\tvalue\r\n")]
    [InlineData("\u200B")]
    [InlineData("\0")]
    public void OptionalNonBlank_NonBlankInput_PassesOriginalStringToFactoryExactlyOnce(string input)
    {
        var calls = new CallbackRecorder<string>();

        var result = Maybe.OptionalNonBlank(input, calls.Wrap(value => Result.Ok(value)));

        result.Unwrap().Unwrap().Should().BeSameAs(input);
        calls.CallCount.Should().Be(1);
        calls.Arguments.Should().ContainSingle().Which.Should().BeSameAs(input);
    }

    [Fact]
    public void OptionalNonBlank_NormalizingFactory_PreservesFactoryOutput()
    {
        var result = Maybe.OptionalNonBlank(" value ", value => Result.Ok(value.Trim()));

        result.Unwrap().Unwrap().Should().Be("value");
    }

    [Fact]
    public void OptionalNonBlank_ValueTypeOutput_PreservesZeroAsPresent()
    {
        var result = Maybe.OptionalNonBlank("zero", _ => Result.Ok(0));

        result.Unwrap().Unwrap().Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalNonBlank_FactoryFailure_PreservesErrorAndPersistenceIntent(bool persistOnFailure)
    {
        var error = Error.InvalidInput.ForField(field: "name", code: "name.invalid");
        var calls = new CallbackRecorder<string>();

        var result = Maybe.OptionalNonBlank("invalid", calls.Wrap(_ => persistOnFailure
            ? Result.FailAfterCommit<string>(error)
            : Result.Fail<string>(error)));

        result.Should().BeFailure().Which.Should().BeSameAs(error);
        ((IPersistOnFailure)result).PersistOnFailure.Should().Be(persistOnFailure);
        calls.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("value")]
    public void OptionalNonBlank_NullFactory_ThrowsEvenForAbsentInput(string? input)
    {
        Action act = () => Maybe.OptionalNonBlank<string>(input, null!);

        act.ShouldRejectNull("function");
    }

    [Fact]
    public void OptionalNonBlank_FactoryThrows_PropagatesException()
    {
        var exception = new InvalidOperationException("Factory failed.");

        var act = () => Maybe.OptionalNonBlank<string>("value", _ => throw exception);

        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00A0")]
    public void Optional_BlankString_StillInvokesFactoryAndPreservesPresence(string input)
    {
        var calls = new CallbackRecorder<string>();

        var result = Maybe.Optional(input, calls.Wrap(value => Result.Ok(value)));

        result.Unwrap().Unwrap().Should().BeSameAs(input);
        calls.CallCount.Should().Be(1);
        calls.Arguments.Should().ContainSingle().Which.Should().BeSameAs(input);
    }
}