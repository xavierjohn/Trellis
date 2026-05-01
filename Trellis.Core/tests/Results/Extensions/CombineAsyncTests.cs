namespace Trellis.Core.Tests.Results.Extensions;

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
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok("World"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Result.Ok("World"));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().ContainSingle();
        validation.Fields.Items[0].Should().BeEquivalentTo(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" });
    }

    [Fact]
    public async Task CombineAsync_Task_Left_RightFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" }))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().ContainSingle();
        validation.Fields.Items[0].Should().BeEquivalentTo(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" });
    }

    [Fact]
    public async Task CombineAsync_Task_Left_BothFail_CombinesValidationErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" }))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
        validation.Fields.Items[0].Should().BeEquivalentTo(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" });
        validation.Fields.Items[1].Should().BeEquivalentTo(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" });
    }

    [Fact]
    public async Task CombineAsync_Task_Left_DifferentTypes_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("text"))
            .CombineAsync(Result.Ok(42));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("text", 42));
    }

    [Fact]
    public async Task CombineAsync_Task_Left_MixedErrorTypes_ReturnsAggregateError()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Invalid" }))))
            .CombineAsync(Result.Fail<string>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Not found" }));

        // Assert
        var aggregate = result.Should().BeFailureOfType<Error.Aggregate>().Which;
        aggregate.Errors.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task CombineAsync_Task_Left_CanChainWithBind()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok("World"))
            .BindAsync((hello, world) => Result.Ok($"{hello} {world}"));

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
        var result = await Result.Ok("Hello")
            .CombineAsync(Task.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_Right_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" })))
            .CombineAsync(Task.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_Right_RightFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Result.Ok("Hello")
            .CombineAsync(Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" })))));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_Right_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" })))
            .CombineAsync(Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" })))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple Task Both-Async (Task<Result<T1>> + Task<Result<T2>>)

    [Fact]
    public async Task CombineAsync_Task_Both_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Task.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_Task_Both_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Task.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_Both_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" })))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task CombineAsync_Task_Both_DifferentTypes_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok(42))
            .CombineAsync(Task.FromResult(Result.Ok(true)));

        // Assert
        result.Should().BeSuccess().Which.Should().Be((42, true));
    }

    #endregion

    #region 2-Tuple Task + Unit

    [Fact]
    public async Task CombineAsync_Task_Unit_BothSuccess_ReturnsValue()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok());

        // Assert
        result.Should().BeSuccess().Which.Item1.Should().Be("Hello");
    }

    [Fact]
    public async Task CombineAsync_Task_Unit_UnitFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("check"), "validation.error") { Detail = "Must be valid" }))));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_Unit_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Bad" }))))
            .CombineAsync(Result.Ok());

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_Unit_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" }))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple ValueTask Left-Async (ValueTask<Result<T1>> + Result<T2>)

    [Fact]
    public async Task CombineAsync_ValueTask_Left_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok("World"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Result.Ok("World"));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" }))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_DifferentTypes_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("text"))
            .CombineAsync(Result.Ok(3.14m));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("text", 3.14m));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Left_CanChainWithBind()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok(10))
            .CombineAsync(Result.Ok(20))
            .BindAsync((a, b) => Result.Ok(a + b));

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
        var result = await Result.Ok("Hello")
            .CombineAsync(ValueTask.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Right_LeftFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" })))
            .CombineAsync(ValueTask.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Right_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad" })))
            .CombineAsync(ValueTask.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad" })))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple ValueTask Both-Async (ValueTask<Result<T1>> + ValueTask<Result<T2>>)

    [Fact]
    public async Task CombineAsync_ValueTask_Both_BothSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("Hello"))
            .CombineAsync(ValueTask.FromResult(Result.Ok("World")));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Both_BothFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("left"), "validation.error") { Detail = "Bad left" }))))
            .CombineAsync(ValueTask.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("right"), "validation.error") { Detail = "Bad right" })))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
    }

    #endregion

    #region 2-Tuple ValueTask + Unit

    [Fact]
    public async Task CombineAsync_ValueTask_Unit_BothSuccess_ReturnsValue()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok());

        // Assert
        result.Should().BeSuccess().Which.Item1.Should().Be("Hello");
    }

    [Fact]
    public async Task CombineAsync_ValueTask_Unit_UnitFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("check"), "validation.error") { Detail = "Bad" }))));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    #endregion

    #region Tuple Chaining — Task Left-Async (T4-generated, 3-tuple comprehensive)

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_AllSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("a"))
            .CombineAsync(Result.Ok("b"))
            .CombineAsync(Result.Ok("c"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("a", "b", "c"));
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_MiddleFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("a"))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("b"), "validation.error") { Detail = "Bad b" }))))
            .CombineAsync(Result.Ok("c"));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_LastFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("a"))
            .CombineAsync(Result.Ok("b"))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("c"), "validation.error") { Detail = "Bad c" }))));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_MultipleFail_CombinesErrors()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("a"), "validation.error") { Detail = "Bad a" }))))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("b"), "validation.error") { Detail = "Bad b" }))))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("c"), "validation.error") { Detail = "Bad c" }))));

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Chain_CanBindWithDestructuring()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok("Beautiful"))
            .CombineAsync(Result.Ok("World"))
            .BindAsync((a, b, c) => Result.Ok($"{a} {b} {c}"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("Hello Beautiful World");
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Unit_AllSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok("World"))
            .CombineAsync(Result.Ok());

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("Hello", "World", Unit.Default));
    }

    [Fact]
    public async Task CombineAsync_Task_3Tuple_Unit_UnitFails_ReturnsFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("Hello"))
            .CombineAsync(Result.Ok("World"))
            .CombineAsync(Result.Fail(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Gate failed" }));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    #endregion

    #region Tuple Chaining — ValueTask Left-Async (T4-generated, 3-tuple validation)

    [Fact]
    public async Task CombineAsync_ValueTask_3Tuple_Chain_AllSuccess_ReturnsTuple()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("a"))
            .CombineAsync(Result.Ok("b"))
            .CombineAsync(Result.Ok("c"));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(("a", "b", "c"));
    }

    [Fact]
    public async Task CombineAsync_ValueTask_3Tuple_Chain_Failure_ReturnsError()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok("a"))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("b"), "validation.error") { Detail = "Bad" }))))
            .CombineAsync(Result.Ok("c"));

        // Assert
        result.Should().BeFailureOfType<Error.UnprocessableContent>();
    }

    #endregion

    #region Larger Tuple Sizes — Validation Tests (T4-generated)

    [Fact]
    public async Task CombineAsync_Task_9Tuple_Chain_AllSuccess()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("1"))
            .CombineAsync(Result.Ok("2"))
            .CombineAsync(Result.Ok("3"))
            .CombineAsync(Result.Ok("4"))
            .CombineAsync(Result.Ok("5"))
            .CombineAsync(Result.Ok("6"))
            .CombineAsync(Result.Ok("7"))
            .CombineAsync(Result.Ok("8"))
            .CombineAsync(Result.Ok("9"))
            .BindAsync((one, two, three, four, five, six, seven, eight, nine) =>
                Result.Ok($"{one}{two}{three}{four}{five}{six}{seven}{eight}{nine}"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("123456789");
    }

    [Fact]
    public async Task CombineAsync_Task_9Tuple_Chain_WithFailure()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok("1"))
            .CombineAsync(Result.Ok("2"))
            .CombineAsync(Result.Ok("3"))
            .CombineAsync(Result.Ok("4"))
            .CombineAsync(Result.Ok("5"))
            .CombineAsync(Result.Ok("6"))
            .CombineAsync(Result.Ok("7"))
            .CombineAsync(Result.Ok("8"))
            .CombineAsync(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad 9" }));

        // Assert
        result.Should().BeFailure();
    }

    [Fact]
    public async Task CombineAsync_ValueTask_5Tuple_Chain_AllSuccess()
    {
        // Arrange & Act
        var result = await ValueTask.FromResult(Result.Ok(1))
            .CombineAsync(Result.Ok(2))
            .CombineAsync(Result.Ok(3))
            .CombineAsync(Result.Ok(4))
            .CombineAsync(Result.Ok(5));

        // Assert
        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5));
    }

    #endregion

    #region Edge Cases and Real-World Scenarios

    [Fact]
    public async Task CombineAsync_Task_Left_WithNullableTypes_Success()
    {
        // Arrange & Act
        var result = await Task.FromResult(Result.Ok<string?>("value"))
            .CombineAsync(Result.Ok<int?>(42));

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
        var result = await Task.FromResult(Result.Ok(record1))
            .CombineAsync(Result.Ok(record2));

        // Assert
        result.Should().BeSuccess().Which.Should().Be((record1, record2));
    }

    [Fact]
    public async Task CombineAsync_RealWorldScenario_ValidateMultipleFieldsAsync()
    {
        // Simulates: first result comes from an async operation,
        // subsequent validations are sync
        var asyncResult = Task.FromResult(Result.Ok("user@example.com"));
        var firstName = Result.Ok("John");
        var lastName = Result.Ok("Doe");

        // Act
        var result = await asyncResult
            .CombineAsync(firstName)
            .CombineAsync(lastName)
            .BindAsync((email, first, last) => Result.Ok($"{first} {last} <{email}>"));

        // Assert
        result.Should().BeSuccess()
            .Which.Should().Be("John Doe <user@example.com>");
    }

    [Fact]
    public async Task CombineAsync_RealWorldScenario_CollectsAllValidationErrors()
    {
        // Simulates: async lookup succeeds but sync validations fail
        var asyncResult = Task.FromResult(Result.Ok("valid@email.com"));
        var badFirst = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("firstName"), "validation.error") { Detail = "First name required" })));
        var badLast = Result.Fail<string>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("lastName"), "validation.error") { Detail = "Last name required" })));

        // Act
        var result = await asyncResult
            .CombineAsync(badFirst)
            .CombineAsync(badLast);

        // Assert
        var validation = result.Should().BeFailureOfType<Error.UnprocessableContent>().Which;
        validation.Fields.Items.Should().HaveCount(2);
        validation.Fields.Items[0].Should().BeEquivalentTo(new FieldViolation(InputPointer.ForProperty("firstName"), "validation.error") { Detail = "First name required" });
        validation.Fields.Items[1].Should().BeEquivalentTo(new FieldViolation(InputPointer.ForProperty("lastName"), "validation.error") { Detail = "Last name required" });
    }

    [Fact]
    public async Task CombineAsync_FailedPipeline_BindNotCalled()
    {
        // Arrange
        var bindCalled = false;

        // Act
        var result = await Task.FromResult(Result.Fail<string>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Bad" }))
            .CombineAsync(Result.Ok("World"))
            .BindAsync((a, b) =>
            {
                bindCalled = true;
                return Result.Ok($"{a} {b}");
            });

        // Assert
        bindCalled.Should().BeFalse();
        result.Should().BeFailure();
    }

    #endregion

    private record TestRecord(string Name, int Age);
}