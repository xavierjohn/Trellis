namespace Trellis.Results.Tests.Results.Extensions.Map;

using Trellis.Testing;

/// <summary>
/// Tests for Map extension methods on tuple-based Result types (2-9 elements).
/// These tests verify the T4-generated code in MapTs.g.cs.
/// 
/// Since all tuple sizes are generated from the same T4 template pattern,
/// we test the 2-tuple comprehensively and validate other sizes work correctly.
/// </summary>
public class MapTsTests : TestBase
{
    #region 2-Tuple Synchronous Tests (Comprehensive Coverage)

    [Fact]
    public void Map_2Tuple_WithNullFunc_ThrowsArgumentNullException()
    {
        var result = Result.Ok((T.Value1, K.Value1));

        var act = () => result.Map<T, K, string>(null!);

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "func");
    }

    [Fact]
    public async Task MapAsync_2Tuple_TaskResultWithFunc_NullResultTask_ThrowsArgumentNullException()
    {
        var act = async () => await ((Task<Result<(T, K)>>)null!).MapAsync((t, k) => $"{t}-{k}");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    [Fact]
    public void Map_2Tuple_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1));
        var functionCalled = false;

        // Act
        var actual = result.Map((t, k) =>
        {
            functionCalled = true;
            return $"{t}-{k}";
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Contain("-");
    }

    [Fact]
    public void Map_2Tuple_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1);
        var functionCalled = false;

        // Act
        var actual = result.Map((t, k) =>
        {
            functionCalled = true;
            return $"{t}-{k}";
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure()
            .Which.Should().Be(Error1);
    }

    [Fact]
    public void Map_2Tuple_Success_ReturnsTransformedValue()
    {
        // Arrange
        var result = Result.Ok(("Hello", "World"));

        // Act
        var actual = result.Map((first, second) => $"{first} {second}!");

        // Assert
        actual.Should().BeSuccess()
            .Which.Should().Be("Hello World!");
    }

    [Fact]
    public void Map_2Tuple_Success_CanTransformToComplexType()
    {
        // Arrange
        var result = Result.Ok(("John", 42));

        // Act
        var actual = result.Map((name, age) => new Person(name, age));

        // Assert
        actual.Should().BeSuccess();
        actual.Value.Name.Should().Be("John");
        actual.Value.Age.Should().Be(42);
    }

    [Fact]
    public void Map_2Tuple_Failure_PreservesOriginalError()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Test error" }));
        var result = Result.Fail<(string, int)>(error);

        // Act
        var actual = result.Map((name, age) => new Person(name, age));

        // Assert
        actual.Should().BeFailure();
        actual.Error!.Should().Be(error);
    }

    [Fact]
    public void Map_2Tuple_WithNullableTypes_Success()
    {
        // Arrange
        var result = Result.Ok<(string?, int?)>(("test", 123));

        // Act
        var actual = result.Map((s, i) => $"{s ?? "null"}-{i ?? 0}");

        // Assert
        actual.Should().BeSuccess()
            .Which.Should().Be("test-123");
    }

    [Fact]
    public void Map_2Tuple_ChainedOperations_Success()
    {
        // Arrange & Act
        var result = Result.Ok(("Hello", "World"))
            .Map((a, b) => $"{a} {b}")
            .Map(combined => combined.ToUpperInvariant());

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("HELLO WORLD");
    }

    #endregion

    #region 2-Tuple Async Tests - Task with Async Func

    [Fact]
    public async Task MapAsync_2Tuple_ResultWithTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1));
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task MapAsync_2Tuple_ResultWithTaskFunc_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1);
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure()
            .Which.Should().Be(Error1);
    }

    #endregion

    #region 2-Tuple Async Tests - Task Result with Sync Func

    [Fact]
    public async Task MapAsync_2Tuple_TaskResultWithSyncFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return $"{t}-{k}";
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task MapAsync_2Tuple_TaskResultWithSyncFunc_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return $"{t}-{k}";
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    #endregion

    #region 2-Tuple Async Tests - Task Result with Task Func

    [Fact]
    public async Task MapAsync_2Tuple_TaskResultWithTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task MapAsync_2Tuple_TaskResultWithTaskFunc_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return Task.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    #endregion

    #region 2-Tuple Async Tests - ValueTask

    [Fact]
    public async Task MapAsync_2Tuple_ResultWithValueTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1));
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return ValueTask.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task MapAsync_2Tuple_ResultWithValueTaskFunc_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K)>(Error1);
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return ValueTask.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task MapAsync_2Tuple_ValueTaskResultWithSyncFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsValueTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return $"{t}-{k}";
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public async Task MapAsync_2Tuple_ValueTaskResultWithValueTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1)).AsValueTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k) =>
        {
            functionCalled = true;
            return ValueTask.FromResult($"{t}-{k}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    #endregion

    #region 3-Tuple Tests (Validation for Template Generation)

    [Fact]
    public void Map_3Tuple_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1, 42));
        var functionCalled = false;

        // Act
        var actual = result.Map((t, k, num) =>
        {
            functionCalled = true;
            return $"{t}-{k}-{num}";
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Contain("42");
    }

    [Fact]
    public void Map_3Tuple_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(T, K, int)>(Error1);
        var functionCalled = false;

        // Act
        var actual = result.Map((t, k, num) =>
        {
            functionCalled = true;
            return $"{t}-{k}-{num}";
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public async Task MapAsync_3Tuple_TaskResultWithTaskFunc_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((T.Value1, K.Value1, 42)).AsTask();
        var functionCalled = false;

        // Act
        var actual = await result.MapAsync((t, k, num) =>
        {
            functionCalled = true;
            return Task.FromResult($"{t}-{k}-{num}");
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    #endregion

    #region 9-Tuple Tests (Validation for Maximum Tuple Size)

    [Fact]
    public void Map_9Tuple_Success_ExecutesFunction()
    {
        // Arrange
        var result = Result.Ok((1, 2, 3, 4, 5, 6, 7, 8, 9));
        var functionCalled = false;

        // Act
        var actual = result.Map((a, b, c, d, e, f, g, h, i) =>
        {
            functionCalled = true;
            return a + b + c + d + e + f + g + h + i;
        });

        // Assert
        functionCalled.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(45); // Sum of 1-9
    }

    [Fact]
    public void Map_9Tuple_Failure_DoesNotExecuteFunction()
    {
        // Arrange
        var result = Result.Fail<(int, int, int, int, int, int, int, int, int)>(Error1);
        var functionCalled = false;

        // Act
        var actual = result.Map((a, b, c, d, e, f, g, h, i) =>
        {
            functionCalled = true;
            return a + b + c + d + e + f + g + h + i;
        });

        // Assert
        functionCalled.Should().BeFalse();
        actual.Should().BeFailure();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Map_2Tuple_TransformToNull_ReturnsSuccessWithNull()
    {
        // Arrange
        var result = Result.Ok(("test", 123));

        // Act
        var actual = result.Map<string, int, string?>((s, i) => null);

        // Assert
        actual.Should().BeSuccess();
        actual.Value.Should().BeNull();
    }

    [Fact]
    public void Map_2Tuple_TransformThrowsException_PropagatesException()
    {
        // Arrange
        var result = Result.Ok(("test", 123));

        // Act
        Action act = () => result.Map<string, int, string>((s, i) =>
            throw new InvalidOperationException("Test exception"));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Test exception");
    }

    [Fact]
    public async Task MapAsync_2Tuple_TransformThrowsException_PropagatesException()
    {
        // Arrange
        var result = Result.Ok(("test", 123));

        // Act - Use explicit Task.FromResult to disambiguate
        Func<Task> act = async () => await result.MapAsync((string s, int i) =>
            Task.FromException<string>(new InvalidOperationException("Async test exception")));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Async test exception");
    }

    [Fact]
    public void Map_2Tuple_WithRecordTypes_Success()
    {
        // Arrange
        var result = Result.Ok((new TestRecord("A", 1), new TestRecord("B", 2)));

        // Act
        var actual = result.Map((r1, r2) => new TestRecord($"{r1.Name}{r2.Name}", r1.Value + r2.Value));

        // Assert
        actual.Should().BeSuccess();
        actual.Value.Name.Should().Be("AB");
        actual.Value.Value.Should().Be(3);
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void Map_2Tuple_CreateDtoFromValidatedInputs()
    {
        // Simulate combining validated value objects
        var nameResult = Result.Ok("John Doe");
        var emailResult = Result.Ok("john@example.com");

        // Act
        var result = nameResult
            .Combine(emailResult)
            .Map((name, email) => new UserDto(name, email));

        // Assert
        result.Should().BeSuccess();
        result.Value.Name.Should().Be("John Doe");
        result.Value.Email.Should().Be("john@example.com");
    }

    [Fact]
    public void Map_3Tuple_CreateEntityFromMultipleValidations()
    {
        // Simulate combining three validated inputs
        var firstNameResult = Result.Ok("John");
        var lastNameResult = Result.Ok("Doe");
        var ageResult = Result.Ok(30);

        // Act
        var result = firstNameResult
            .Combine(lastNameResult)
            .Combine(ageResult)
            .Map((firstName, lastName, age) => new Person($"{firstName} {lastName}", age));

        // Assert
        result.Should().BeSuccess();
        result.Value.Name.Should().Be("John Doe");
        result.Value.Age.Should().Be(30);
    }

    [Fact]
    public async Task MapAsync_2Tuple_RealWorldAsyncScenario()
    {
        // Simulate async validation and transformation
        var result = await Result.Ok(("user@example.com", "password123"))
            .AsTask()
            .MapAsync((email, password) => new AuthRequest(email, password));

        // Assert
        result.Should().BeSuccess();
        result.Value.Email.Should().Be("user@example.com");
    }

    #endregion

    #region Helper Types

    private record Person(string Name, int Age);
    private record UserDto(string Name, string Email);
    private record AuthRequest(string Email, string Password);
    private record TestRecord(string Name, int Value);

    #endregion
}