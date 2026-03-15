using FluentAssertions;
using Trellis;
using Trellis.Testing;
using Xunit;

namespace Trellis.Results.Tests.Results.Extensions.Await;

/// <summary>
/// Tests for T4-generated WhenAllAsync tuple overloads (WhenAllTs.g.tt).
/// These tests ensure at least one tuple permutation is covered to catch T4 generation bugs.
/// </summary>
public class WhenAllAsyncTupleTests : TestBase
{
    #region WhenAllAsync - 2-Tuple

    [Fact]
    public async Task WhenAllAsync_Tuple2_BothSuccess_ReturnsCombinedTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success("two"));

        // Act
        var result = await (task1, task2).WhenAllAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be((1, "two"));
    }

    [Fact]
    public async Task WhenAllAsync_Tuple2_FirstFails_ReturnsFailure()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Failure<int>(Error1));
        var task2 = Task.FromResult(Result.Success("two"));

        // Act
        var result = await (task1, task2).WhenAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error1);
    }

    [Fact]
    public async Task WhenAllAsync_Tuple2_SecondFails_ReturnsFailure()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Failure<string>(Error2));

        // Act
        var result = await (task1, task2).WhenAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error2);
    }

    [Fact]
    public async Task WhenAllAsync_Tuple2_BothFail_ReturnsCombinedErrors()
    {
        // Arrange
        var error1 = Error.Validation("Error 1", "field1");
        var error2 = Error.Validation("Error 2", "field2");
        var task1 = Task.FromResult(Result.Failure<int>(error1));
        var task2 = Task.FromResult(Result.Failure<string>(error2));

        // Act
        var result = await (task1, task2).WhenAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task WhenAllAsync_Tuple2_FaultedTask_SurfacesException()
    {
        // Arrange
        var task1 = Task.FromException<Result<int>>(new InvalidOperationException("boom"));
        var task2 = Task.FromResult(Result.Success("two"));

        // Act
        var act = () => (task1, task2).WhenAllAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    #endregion

    #region WhenAllAsync - 3-Tuple

    [Fact]
    public async Task WhenAllAsync_Tuple3_AllSuccess_ReturnsCombinedTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success("two"));
        var task3 = Task.FromResult(Result.Success(3.0));

        // Act
        var result = await (task1, task2, task3).WhenAllAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be((1, "two", 3.0));
    }

    [Fact]
    public async Task WhenAllAsync_Tuple3_OneFails_ReturnsFailure()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Failure<string>(Error1));
        var task3 = Task.FromResult(Result.Success(3.0));

        // Act
        var result = await (task1, task2, task3).WhenAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error1);
    }

    #endregion

    #region WhenAllAsync - 4-Tuple

    [Fact]
    public async Task WhenAllAsync_Tuple4_AllSuccess_ReturnsCombinedTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));

        // Act
        var result = await (task1, task2, task3, task4).WhenAllAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be((1, 2, 3, 4));
    }

    [Fact]
    public async Task WhenAllAsync_Tuple4_LastFails_ReturnsFailure()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Failure<int>(Error1));

        // Act
        var result = await (task1, task2, task3, task4).WhenAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error1);
    }

    #endregion

    #region WhenAllAsync - 5-Tuple

    [Fact]
    public async Task WhenAllAsync_Tuple5_AllSuccess_ReturnsCombinedTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));
        var task5 = Task.FromResult(Result.Success(5));

        // Act
        var result = await (task1, task2, task3, task4, task5).WhenAllAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be((1, 2, 3, 4, 5));
    }

    #endregion

    #region WhenAllAsync - 9-Tuple (Maximum)

    [Fact]
    public async Task WhenAllAsync_Tuple9_AllSuccess_ReturnsCombinedTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));
        var task5 = Task.FromResult(Result.Success(5));
        var task6 = Task.FromResult(Result.Success(6));
        var task7 = Task.FromResult(Result.Success(7));
        var task8 = Task.FromResult(Result.Success(8));
        var task9 = Task.FromResult(Result.Success(9));

        // Act
        var result = await (task1, task2, task3, task4, task5, task6, task7, task8, task9).WhenAllAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be((1, 2, 3, 4, 5, 6, 7, 8, 9));
    }

    [Fact]
    public async Task WhenAllAsync_Tuple9_OneFails_ReturnsFailure()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(1));
        var task2 = Task.FromResult(Result.Success(2));
        var task3 = Task.FromResult(Result.Success(3));
        var task4 = Task.FromResult(Result.Success(4));
        var task5 = Task.FromResult(Result.Failure<int>(Error1));
        var task6 = Task.FromResult(Result.Success(6));
        var task7 = Task.FromResult(Result.Success(7));
        var task8 = Task.FromResult(Result.Success(8));
        var task9 = Task.FromResult(Result.Success(9));

        // Act
        var result = await (task1, task2, task3, task4, task5, task6, task7, task8, task9).WhenAllAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error1);
    }

    #endregion

    #region WhenAllAsync - Chained with Bind

    [Fact]
    public async Task WhenAllAsync_Tuple3_ChainedWithBind_ProcessesTuple()
    {
        // Arrange
        var task1 = Task.FromResult(Result.Success(10));
        var task2 = Task.FromResult(Result.Success(20));
        var task3 = Task.FromResult(Result.Success(30));

        // Act
        var result = await (task1, task2, task3)
            .WhenAllAsync()
            .BindAsync((a, b, c) => Result.Success(a + b + c));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(60);
    }

    #endregion
}