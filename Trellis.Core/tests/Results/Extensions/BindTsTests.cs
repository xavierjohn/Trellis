namespace Trellis.Core.Tests.Results.Extensions.Bind;

using Trellis.Testing;

/// <summary>
/// Tests for Bind extension methods on tuple-based Result types (2-9 elements).
/// These tests verify the T4-generated code in BindTs.g.cs.
/// Since all tuple sizes are generated from the same template pattern,
/// we only test representative permutations (2-tuple and 3-tuple).
/// </summary>
public class BindTsTests : TestBase
{
    #region 2-Tuple Synchronous Tests

    [Fact]
    public void Bind_2Tuple_WithNullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Ok((T.Value1, K.Value1));

        var act = () => result.Bind<T, K, string>(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public void Bind_2Tuple_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1));
        var functionCalled = false;

        // Act
        var actual = result.Bind((t, k) =>
        {
            functionCalled = true;
            return Result.Ok($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Contain($"{T.Value1}");
    }

    [Fact]
    public void Bind_2Tuple_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1);
        var functionCalled = false;

        // Act
        var actual = result.Bind((t, k) =>
        {
            functionCalled = true;
            return Result.Ok($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure()
            .Which.Should().Be(Error1);
    }

    #endregion

    #region 2-Tuple Async Tests - Task

    [Fact]
    public async Task BindAsync_2Tuple_ResultWithTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1));
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult(Result.Ok($"{t}-{k}"));
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task BindAsync_2Tuple_ResultWithTaskFunc_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1);
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult(Result.Ok($"{t}-{k}"));
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task BindAsync_2Tuple_ResultWithTaskFunc_NullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Ok((T.Value1, K.Value1));

        var act = async () => await result.BindAsync((Func<T, K, Task<Result<string>>>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public async Task BindAsync_2Tuple_TaskResultWithFunc_NullResultTask_ThrowsArgumentNullException()
    {
        var act = async () => await ((Task<Result<(T, K)>>)null!).BindAsync((t, k) => Result.Ok($"{t}-{k}"));

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    [Fact]
    public async Task BindAsync_2Tuple_TaskResultWithFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return Result.Ok($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task BindAsync_2Tuple_TaskResultWithTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult(Result.Ok($"{t}-{k}"));
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    #endregion

    #region 2-Tuple Async Tests - ValueTask

    [Fact]
    public async Task BindAsync_2Tuple_ResultWithValueTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1));
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return ValueTask.FromResult(Result.Ok($"{t}-{k}"));
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task BindAsync_2Tuple_ValueTaskResultWithFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsValueTask();
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return Result.Ok($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task BindAsync_2Tuple_ValueTaskResultWithValueTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsValueTask();
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k) =>
        {
            functionCalled = true;
            return ValueTask.FromResult(Result.Ok($"{t}-{k}"));
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    #endregion

    #region 3-Tuple Tests (Additional Permutation)

    [Fact]
    public void Bind_3Tuple_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1, 42));
        var functionCalled = false;

        // Act
        var actual = result.Bind((t, k, num) =>
        {
            functionCalled = true;
            return Result.Ok($"{t}-{k}-{num}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Contain("42");
    }

    [Fact]
    public void Bind_3Tuple_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K, int)>(Error1);
        var functionCalled = false;

        // Act
        var actual = result.Bind((t, k, num) =>
        {
            functionCalled = true;
            return Result.Ok($"{t}-{k}-{num}");
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task BindAsync_3Tuple_TaskResultWithTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1, 42)).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.BindAsync((t, k, num) =>
        {
            functionCalled = true;
            return Task.FromResult(Result.Ok($"{t}-{k}-{num}"));
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void Bind_2Tuple_AfterCombine_CreateEntity()
    {
        // Arrange
        var firstNameResult = Result.Ok("John");
        var lastNameResult = Result.Ok("Doe");

        // Act
        var result = firstNameResult
            .Combine(lastNameResult)
            .Bind((first, last) => Result.Ok($"{first} {last}"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("John Doe");
    }

    [Fact]
    public void Bind_2Tuple_AfterCombine_OneFailure_ReturnsFailure()
    {
        // Arrange
        var firstNameResult = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "First name required" });
        var lastNameResult = Result.Ok("Doe");

        // Act
        var result = firstNameResult
            .Combine(lastNameResult)
            .Bind((first, last) => Result.Ok($"{first} {last}"));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task BindAsync_3Tuple_AfterCombine_ChainedOperations()
    {
        // Arrange
        var emailResult = Result.Ok("user@example.com");
        var firstNameResult = Result.Ok("John");
        var lastNameResult = Result.Ok("Doe");

        // Act
        var result = await emailResult
            .Combine(firstNameResult)
            .Combine(lastNameResult)
            .BindAsync((email, first, last) =>
                Task.FromResult(Result.Ok($"{first} {last} <{email}>"))
            );

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("John Doe <user@example.com>");
    }

    #endregion
}