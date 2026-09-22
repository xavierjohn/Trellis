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
        var calls = 0;

        var result = Maybe.OptionalNonBlank(input, value =>
        {
            calls++;
            return Result.Ok(value);
        });

        result.Should().BeSuccess().Which.HasNoValue.Should().BeTrue();
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData("value")]
    [InlineData(" value ")]
    [InlineData("\tvalue\r\n")]
    [InlineData("\u200B")]
    [InlineData("\0")]
    public void OptionalNonBlank_NonBlankInput_PassesOriginalStringToFactoryExactlyOnce(string input)
    {
        var calls = 0;
        string? received = null;

        var result = Maybe.OptionalNonBlank(input, value =>
        {
            calls++;
            received = value;
            return Result.Ok(value);
        });

        result.Unwrap().Unwrap().Should().BeSameAs(input);
        received.Should().BeSameAs(input);
        calls.Should().Be(1);
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
        var calls = 0;

        var result = Maybe.OptionalNonBlank("invalid", _ =>
        {
            calls++;
            return persistOnFailure
                ? Result.FailAfterCommit<string>(error)
                : Result.Fail<string>(error);
        });

        result.Should().BeFailure().Which.Should().BeSameAs(error);
        ((IPersistOnFailure)result).PersistOnFailure.Should().Be(persistOnFailure);
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("value")]
    public void OptionalNonBlank_NullFactory_ThrowsEvenForAbsentInput(string? input)
    {
        var act = () => Maybe.OptionalNonBlank<string>(input, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("function");
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
        var calls = 0;

        var result = Maybe.Optional(input, value =>
        {
            calls++;
            return Result.Ok(value);
        });

        result.Unwrap().Unwrap().Should().BeSameAs(input);
        calls.Should().Be(1);
    }
}