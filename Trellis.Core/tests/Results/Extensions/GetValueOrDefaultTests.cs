namespace Trellis.Core.Tests.Results.Extensions;

using Trellis.Testing;

public class GetValueOrDefaultTests
{
    #region GetValueOrDefault with value

    [Fact]
    public void GetValueOrDefault_Success_ReturnsValue()
    {
        var sut = Result.Ok(42);

        var value = sut.GetValueOrDefault(0);

        value.Should().Be(42);
    }

    [Fact]
    public void GetValueOrDefault_Failure_ReturnsDefault()
    {
        var sut = Result.Fail<int>(new Error.InternalServerError("test") { Detail = "some error" });

        var value = sut.GetValueOrDefault(99);

        value.Should().Be(99);
    }

    #endregion

    #region GetValueOrDefault with Func

    [Fact]
    public void GetValueOrDefault_Func_Success_ReturnsValue_FactoryNotCalled()
    {
        var factoryCalled = false;
        var sut = Result.Ok(42);

        var value = sut.GetValueOrDefault(() => { factoryCalled = true; return 0; });

        value.Should().Be(42);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void GetValueOrDefault_Func_Failure_ReturnsFactoryResult()
    {
        var sut = Result.Fail<int>(new Error.InternalServerError("test") { Detail = "some error" });

        var value = sut.GetValueOrDefault(() => 99);

        value.Should().Be(99);
    }

    [Fact]
    public void GetValueOrDefault_NullFactory_ThrowsArgumentNullException()
    {
        var sut = Result.Ok(42);

        var act = () => sut.GetValueOrDefault((Func<int>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "defaultFactory");
    }

    #endregion

    #region GetValueOrDefault with Func<Error, T>

    [Fact]
    public void GetValueOrDefault_ErrorFunc_Success_ReturnsValue_FactoryNotCalled()
    {
        var factoryCalled = false;
        var sut = Result.Ok(42);

        var value = sut.GetValueOrDefault(error => { factoryCalled = true; return 0; });

        value.Should().Be(42);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void GetValueOrDefault_ErrorFunc_Failure_ReturnsFactoryResult_ReceivesError()
    {
        var expectedError = new Error.InternalServerError("test") { Detail = "some error" };
        var sut = Result.Fail<string>(expectedError);

        var value = sut.GetValueOrDefault(error => $"fallback: {error.Code}");

        value.Should().Be($"fallback: {expectedError.Code}");
    }

    [Fact]
    public void GetValueOrDefault_ErrorFunc_NullFactory_ThrowsArgumentNullException()
    {
        var sut = Result.Ok(42);

        var act = () => sut.GetValueOrDefault((Func<Error, int>)null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "defaultFactory");
    }

    #endregion
}