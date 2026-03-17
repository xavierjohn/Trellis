namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

/// <summary>
/// Tests for async Combine extension methods.
/// Comprehensive tests for the 2-tuple permutation (Task and ValueTask),
/// plus validation tests for larger tuple sizes (T4-generated).
/// </summary>
public class CombineAsyncTests
{
    #region 2-Tuple Task Left-Async (Task<Result<T1>> + Result<T2>)

    [Fact]
    public async Task CombineAsync_Task_Left_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("World"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Result.Success("World"));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("left", ["Bad left"]));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_RightFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad right", "right")));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("right", ["Bad right"]));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_BothFail_CombinesValidationErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad right", "right")));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("left", ["Bad left"]));
        validation.FieldErrors[1].Should().BeEquivalentTo(new ValidationError.FieldError("right", ["Bad right"]));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_DifferentTypes_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("text"))
            .CombineAsync(Result.Success(42));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("text", 42));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_MixedErrorTypes_ReturnsAggregateError()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Invalid", "field")))
            .CombineAsync(Result.Failure<string>(Error.NotFound("Not found")));

        // Assert
        var aggregate = result.Should().BeFailureOfType<AggregateError>().Which;
        aggregate.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task CombineAsync_Task_Left_CanChainWithBind()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("World"))
            .BindAsync((hello, world) => Result.Success($"{hello} {world}"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("Hello World");
    }

    #endregion

    #region 2-Tuple Task Right-Async (Result<T1> + Task<Result<T2>>)

    [Fact]
    public async Task CombineAsync_Task_Right_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Result.Success("Hello")
            .CombineAsync(Task.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_Right_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Result.Failure<string>(Error.Validation("Bad left", "left"))
            .CombineAsync(Task.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_Right_RightFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Result.Success("Hello")
            .CombineAsync(Task.FromResult(Result.Failure<string>(Error.Validation("Bad right", "right"))));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_Right_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Result.Failure<string>(Error.Validation("Bad left", "left"))
            .CombineAsync(Task.FromResult(Result.Failure<string>(Error.Validation("Bad right", "right"))));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple Task Both-Async (Task<Result<T1>> + Task<Result<T2>>)

    [Fact]
    public async Task CombineAsync_Task_Both_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Task.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_Both_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Task.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_Both_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Task.FromResult(Result.Failure<string>(Error.Validation("Bad right", "right"))));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    [Fact]
    public async Task CombineAsync_Task_Both_DifferentTypes_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success(42))
            .CombineAsync(Task.FromResult(Result.Success(true)));

        // Assert
        result.Should().BeSuccess().Which.Should().Be((42, true));
    }

    #endregion

    #region 2-Tuple Task + Unit

    [Fact]
    public async Task CombineAsync_Task_Unit_BothSuccess_ReturnsValue()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success());

        // Assert
        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task CombineAsync_Task_Unit_UnitFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Failure(Error.Validation("Must be valid", "check")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_Unit_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad", "field")))
            .CombineAsync(Result.Success());

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_Unit_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Result.Failure(Error.Validation("Bad right", "right")));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple ValueTask Left-Async (ValueTask<Result<T1>> + Result<T2>)

    [Fact]
    public async Task CombineAsync_ValueTask_Left_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("World"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Result.Success("World"));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad right", "right")));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_DifferentTypes_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("text"))
            .CombineAsync(Result.Success(3.14m));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("text", 3.14m));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_CanChainWithBind()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success(10))
            .CombineAsync(Result.Success(20))
            .BindAsync((a, b) => Result.Success(a + b));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be(30);
    }

    #endregion

    #region 2-Tuple ValueTask Right-Async (Result<T1> + ValueTask<Result<T2>>)

    [Fact]
    public async Task CombineAsync_ValueTask_Right_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Result.Success("Hello")
            .CombineAsync(ValueTask.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Right_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Result.Failure<string>(Error.Validation("Bad left", "left"))
            .CombineAsync(ValueTask.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Right_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Result.Failure<string>(Error.Validation("Bad", "left"))
            .CombineAsync(ValueTask.FromResult(Result.Failure<string>(Error.Validation("Bad", "right"))));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple ValueTask Both-Async (ValueTask<Result<T1>> + ValueTask<Result<T2>>)

    [Fact]
    public async Task CombineAsync_ValueTask_Both_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("Hello"))
            .CombineAsync(ValueTask.FromResult(Result.Success("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Both_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Failure<string>(Error.Validation("Bad left", "left")))
            .CombineAsync(ValueTask.FromResult(Result.Failure<string>(Error.Validation("Bad right", "right"))));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple ValueTask + Unit

    [Fact]
    public async Task CombineAsync_ValueTask_Unit_BothSuccess_ReturnsValue()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success());

        // Assert
        result.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Unit_UnitFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Failure(Error.Validation("Bad", "check")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    #endregion

    #region Tuple Chaining — Task Left-Async (T4-generated, 3-tuple comprehensive)

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_AllSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("a"))
            .CombineAsync(Result.Success("b"))
            .CombineAsync(Result.Success("c"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("a", "b", "c"));
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_MiddleFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("a"))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad b", "b")))
            .CombineAsync(Result.Success("c"));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_LastFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("a"))
            .CombineAsync(Result.Success("b"))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad c", "c")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_MultipleFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad a", "a")))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad b", "b")))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad c", "c")));

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(3);
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_CanBindWithDestructuring()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("Beautiful"))
            .CombineAsync(Result.Success("World"))
            .BindAsync((a, b, c) => Result.Success($"{a} {b} {c}"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("Hello Beautiful World");
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Unit_AllSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("World"))
            .CombineAsync(Result.Success());

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Unit_UnitFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("World"))
            .CombineAsync(Result.Failure(Error.Validation("Gate failed")));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    #endregion

    #region Tuple Chaining — ValueTask Left-Async (T4-generated, 3-tuple validation)

    [Fact]
    public async Task CombineAsync_ValueTask_3Tuple_Chain_AllSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("a"))
            .CombineAsync(Result.Success("b"))
            .CombineAsync(Result.Success("c"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("a", "b", "c"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_3Tuple_Chain_Failure_ReturnsError()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success("a"))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad", "b")))
            .CombineAsync(Result.Success("c"));

        // Assert
        result.Should().BeFailureOfType<ValidationError>();
    }

    #endregion

    #region Larger Tuple Sizes — Validation Tests (T4-generated)

    [Fact]
    public async Task CombineAsync_Task_9Tuple_Chain_AllSuccess()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("1"))
            .CombineAsync(Result.Success("2"))
            .CombineAsync(Result.Success("3"))
            .CombineAsync(Result.Success("4"))
            .CombineAsync(Result.Success("5"))
            .CombineAsync(Result.Success("6"))
            .CombineAsync(Result.Success("7"))
            .CombineAsync(Result.Success("8"))
            .CombineAsync(Result.Success("9"))
            .BindAsync((one, two, three, four, five, six, seven, eight, nine) =>
                Result.Success($"{one}{two}{three}{four}{five}{six}{seven}{eight}{nine}"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("123456789");
    }

    [Fact]
    public async Task CombineAsync_Task_9Tuple_Chain_WithFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success("1"))
            .CombineAsync(Result.Success("2"))
            .CombineAsync(Result.Success("3"))
            .CombineAsync(Result.Success("4"))
            .CombineAsync(Result.Success("5"))
            .CombineAsync(Result.Success("6"))
            .CombineAsync(Result.Success("7"))
            .CombineAsync(Result.Success("8"))
            .CombineAsync(Result.Failure<string>(Error.Validation("Bad 9")));

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public async Task CombineAsync_ValueTask_5Tuple_Chain_AllSuccess()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Success(1))
            .CombineAsync(Result.Success(2))
            .CombineAsync(Result.Success(3))
            .CombineAsync(Result.Success(4))
            .CombineAsync(Result.Success(5));

        // Assert
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5));
    }

    #endregion

    #region Edge Cases and Real-World Scenarios

    [Fact]
    public async Task CombineAsync_Task_Left_WithNullableTypes_Success()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Success<string?>("value"))
            .CombineAsync(Result.Success<int?>(42));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("value", (int?)42));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_WithRecords_Success()
    {
        // Arrange
        var record1 = new TestRecord("Alice", 30);
        var record2 = new TestRecord("Bob", 25);

        // Act
        var result = await Task.FromResult(Result.Success(record1))
            .CombineAsync(Result.Success(record2));

        // Assert
        result.Should().BeSuccess().Which.Should().Be((record1, record2));
    }

    [Fact]
    public async Task CombineAsync_RealWorldScenario_ValidateMultipleFieldsAsync()
    {
        // Simulates: first result comes from an async operation,
        // subsequent validations are sync
        var asyncResult = Task.FromResult(Result.Success("user@example.com"));
        var firstName = Result.Success("John");
        var lastName = Result.Success("Doe");

        // Act
        var result = await asyncResult
            .CombineAsync(firstName)
            .CombineAsync(lastName)
            .BindAsync((email, first, last) => Result.Success($"{first} {last} <{email}>"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("John Doe <user@example.com>");
    }

    [Fact]
    public async Task CombineAsync_RealWorldScenario_CollectsAllValidationErrors()
    {
        // Simulates: async lookup succeeds but sync validations fail
        var asyncResult = Task.FromResult(Result.Success("valid@email.com"));
        var badFirst = Result.Failure<string>(Error.Validation("First name required", "firstName"));
        var badLast = Result.Failure<string>(Error.Validation("Last name required", "lastName"));

        // Act
        var result = await asyncResult
            .CombineAsync(badFirst)
            .CombineAsync(badLast);

        // Assert
        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("firstName", ["First name required"]));
        validation.FieldErrors[1].Should().BeEquivalentTo(new ValidationError.FieldError("lastName", ["Last name required"]));
    }

    [Fact]
    public async Task CombineAsync_FailedPipeline_BindNotCalled()
    {
        // Arrange
        var bindCalled = false;

        // Act
        var result = await Task.FromResult(Result.Failure<string>(Error.Validation("Bad")))
            .CombineAsync(Result.Success("World"))
            .BindAsync((a, b) =>
            {
                bindCalled = true;
                return Result.Success($"{a} {b}");
            });

        // Assert
        bindCalled.Should().BeFalse();
        result.Should().BeFailure();
    }

    #endregion

    private record TestRecord(string Name, int Age);
}