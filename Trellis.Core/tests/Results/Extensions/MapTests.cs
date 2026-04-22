namespace Trellis.Core.Tests.Results.Extensions.Map;

using Trellis.Testing;

using System.Globalization;
using Xunit;

public class MapTests : TestBase
{
    [Fact]
    public void Map_WithNullFunc_ShouldThrowArgumentNullException()
    {
        var result = Result.Ok(5);

        var act = () => result.Map<int, string>(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public void Map_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5);

        // Act
        var result = i.Map(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public void Map_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1);

        // Act
        var result = i.Map(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }

    // Task

    [Fact]
    public async Task Map_Left_Task_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5).AsTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public async Task Map_Left_Task_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1).AsTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }

    [Fact]
    public async Task Map_Right_Task_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5);

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsTask());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public async Task Map_Right_Task_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1);

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsTask());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }

    [Fact]
    public async Task MapAsync_Right_Task_WithNullFunc_ShouldThrowArgumentNullException()
    {
        var result = Result.Ok(5);

        var act = async () => await result.MapAsync((Func<int, Task<string>>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public async Task Map_Both_Task_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5).AsTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsTask());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public async Task Map_Both_Task_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1).AsTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsTask());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }

    // ValueTask

    [Fact]
    public async Task Map_Left_ValueTask_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5).AsValueTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public async Task Map_Left_ValueTask_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1).AsValueTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }

    [Fact]
    public async Task Map_Right_ValueTask_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5);

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsValueTask());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public async Task Map_Right_ValueTask_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1);

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsValueTask());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }

    [Fact]
    public async Task Map_Both_ValueTask_ShouldReturnResult()
    {
        // Arrange
        var i = Result.Ok(5).AsValueTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsValueTask());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().Be("5");
    }

    [Fact]
    public async Task Map_Both_ValueTask_ShouldReturnFailedResult()
    {
        // Arrange
        var i = Result.Fail<int>(Error1).AsValueTask();

        // Act
        var result = await i.MapAsync(x => x.ToString(CultureInfo.InvariantCulture).AsValueTask());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().Be(Error1);
    }
}