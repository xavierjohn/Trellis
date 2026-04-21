namespace Trellis.Core.Tests.Results.Extensions.WhenTests;

using System.Threading.Tasks;
using Trellis.Testing;

public class WhenTests : TestBase
{
    [Fact]
    public void When_WithNullPredicate_ThrowsArgumentNullException()
    {
        var result = Result.Ok(42);

        var act = () => result.When((Func<int, bool>)null!, x => Result.Ok(x));

        act.Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "predicate");
    }

    [Fact]
    public async Task WhenAsync_WithTaskResult_NullResultTask_ThrowsArgumentNullException()
    {
        Task<Result<int>> resultTask = null!;

        Func<Task<Result<int>>> act = () => resultTask.WhenAsync(
            x => x > 0,
            x => Task.FromResult(Result.Ok(x)));

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "resultTask");
    }

    #region When with Predicate

    [Fact]
    public void When_WithPredicate_TrueCondition_ExecutesOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.When(
            x => x > 40,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(84);
    }

    [Fact]
    public void When_WithPredicate_FalseCondition_SkipsOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.When(
            x => x < 40,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(42); // Original value unchanged
    }

    [Fact]
    public void When_WithPredicate_FailureResult_SkipsOperation()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);
        var operationExecuted = false;

        // Act
        var actual = result.When(
            x => x > 40,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    [Fact]
    public void When_WithPredicate_OperationReturnsFailure_ReturnsFailure()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = result.When(
            x => x > 40,
            x => Result.Fail<int>(Error2));

        // Assert
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error2.Code);
    }

    #endregion

    #region WhenAsync with Task<Result<T>> input

    [Fact]
    public async Task WhenAsync_WithTaskResult_PredicateTrue_ExecutesOperation()
    {
        // Arrange
        Task<Result<int>> resultTask = Task.FromResult(Result.Ok(21));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            x => x == 21,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x * 2));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public async Task WhenAsync_WithTaskResult_ConditionFalse_SkipsOperation()
    {
        // Arrange
        Task<Result<int>> resultTask = Task.FromResult(Result.Ok(21));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            condition: false,
            operation: x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x * 2));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(21);
    }

    [Fact]
    public async Task WhenAsync_WithTaskResult_FailureShortCircuits()
    {
        // Arrange
        Task<Result<int>> resultTask = Task.FromResult(Result.Fail<int>(Error1));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            true,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error1.Code);
    }

    #endregion

    #region UnlessAsync with Task<Result<T>> input

    [Fact]
    public async Task UnlessAsync_WithTaskResult_PredicateFalse_ExecutesOperation()
    {
        // Arrange
        Task<Result<int>> resultTask = Task.FromResult(Result.Ok(10));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            x => x > 50,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x + 5));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(15);
    }

    [Fact]
    public async Task UnlessAsync_WithTaskResult_ConditionTrue_SkipsOperation()
    {
        // Arrange
        Task<Result<int>> resultTask = Task.FromResult(Result.Ok(10));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            condition: true,
            operation: x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x + 5));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(10);
    }

    [Fact]
    public async Task UnlessAsync_WithTaskResult_FailureShortCircuits()
    {
        // Arrange
        Task<Result<int>> resultTask = Task.FromResult(Result.Fail<int>(Error2));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            false,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error2.Code);
    }

    #endregion

    #region WhenAsync with ValueTask<Result<T>> input

    [Fact]
    public async Task WhenAsync_WithValueTaskResult_PredicateTrue_ExecutesOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(7));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            x => x == 7,
            x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x * 3));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(21);
    }

    [Fact]
    public async Task WhenAsync_WithValueTaskResult_PredicateFalse_SkipsOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(7));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            x => x < 0,
            x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x * 3));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(7);
    }

    [Fact]
    public async Task WhenAsync_WithValueTaskResult_ConditionTrue_ExecutesOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(7));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            condition: true,
            operation: x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x * 3));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(21);
    }

    [Fact]
    public async Task WhenAsync_WithValueTaskResult_ConditionFalse_SkipsOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(7));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.WhenAsync(
            condition: false,
            operation: x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x * 3));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(7);
    }

    #endregion

    #region UnlessAsync with ValueTask<Result<T>> input

    [Fact]
    public async Task UnlessAsync_WithValueTaskResult_PredicateFalse_ExecutesOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(5));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            x => x > 10,
            x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x + 1));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(6);
    }

    [Fact]
    public async Task UnlessAsync_WithValueTaskResult_PredicateTrue_SkipsOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(5));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            x => x < 10,
            x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x + 1));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(5);
    }

    [Fact]
    public async Task UnlessAsync_WithValueTaskResult_ConditionFalse_ExecutesOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(5));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            condition: false,
            operation: x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x + 1));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(6);
    }

    [Fact]
    public async Task UnlessAsync_WithValueTaskResult_ConditionTrue_SkipsOperation()
    {
        // Arrange
        ValueTask<Result<int>> resultTask = ValueTask.FromResult(Result.Ok(5));
        var operationExecuted = false;

        // Act
        var actual = await resultTask.UnlessAsync(
            condition: true,
            operation: x =>
            {
                operationExecuted = true;
                return ValueTask.FromResult(Result.Ok(x + 1));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(5);
    }

    #endregion
    #region When with Boolean Condition

    [Fact]
    public void When_WithBoolean_TrueCondition_ExecutesOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.When(
            true,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(84);
    }

    [Fact]
    public void When_WithBoolean_FalseCondition_SkipsOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.When(
            false,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public void When_WithBoolean_FailureResult_SkipsOperation()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);
        var operationExecuted = false;

        // Act
        var actual = result.When(
            true,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public void When_WithBoolean_OperationReturnsFailure_ReturnsFailure()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = result.When(
            true,
            x => Result.Fail<int>(Error2));

        // Assert
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error2.Code);
    }

    #endregion

    #region Unless with Predicate

    [Fact]
    public void Unless_WithPredicate_FalseCondition_ExecutesOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.Unless(
            x => x > 50,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(84);
    }

    [Fact]
    public void Unless_WithPredicate_TrueCondition_SkipsOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.Unless(
            x => x < 50,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public void Unless_WithPredicate_FailureResult_SkipsOperation()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);
        var operationExecuted = false;

        // Act
        var actual = result.Unless(
            x => x > 50,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public void Unless_WithPredicate_OperationReturnsFailure_ReturnsFailure()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = result.Unless(
            x => x > 50,
            x => Result.Fail<int>(Error2));

        // Assert
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error2.Code);
    }

    #endregion

    #region Unless with Boolean Condition

    [Fact]
    public void Unless_WithBoolean_FalseCondition_ExecutesOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.Unless(
            false,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(84);
    }

    [Fact]
    public void Unless_WithBoolean_TrueCondition_SkipsOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = result.Unless(
            true,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public void Unless_WithBoolean_FailureResult_SkipsOperation()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);
        var operationExecuted = false;

        // Act
        var actual = result.Unless(
            false,
            x => { operationExecuted = true; return Result.Ok(x * 2); });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure();
    }

    [Fact]
    public void Unless_WithBoolean_OperationReturnsFailure_ReturnsFailure()
    {
        // Arrange
        var result = Result.Ok(42);

        // Act
        var actual = result.Unless(
            false,
            x => Result.Fail<int>(Error2));

        // Assert
        actual.Should().BeFailure()
            .Which.Should().HaveCode(Error2.Code);
    }

    #endregion

    #region WhenAsync with Predicate

    [Fact]
    public async Task WhenAsync_WithPredicate_TrueCondition_ExecutesOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = await result.WhenAsync(
            x => x > 40,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x * 2));
            });

        // Assert
        operationExecuted.Should().BeTrue();
        actual.Should().BeSuccess()
            .Which.Should().Be(84);
    }

    [Fact]
    public async Task WhenAsync_WithPredicate_FalseCondition_SkipsOperation()
    {
        // Arrange
        var result = Result.Ok(42);
        var operationExecuted = false;

        // Act
        var actual = await result.WhenAsync(
            x => x < 40,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x * 2));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeSuccess()
            .Which.Should().Be(42);
    }

    [Fact]
    public async Task WhenAsync_WithPredicate_FailureResult_SkipsOperation()
    {
        // Arrange
        var result = Result.Fail<int>(Error1);
        var operationExecuted = false;

        // Act
        var actual = await result.WhenAsync(
            x => x > 40,
            x =>
            {
                operationExecuted = true;
                return Task.FromResult(Result.Ok(x * 2));
            });

        // Assert
        operationExecuted.Should().BeFalse();
        actual.Should().BeFailure();
    }

    #endregion

    #region Real-World Scenarios

    [Fact]
    public void When_ApplyDiscountForPremiumUsers()
    {
        // Arrange
        var order = new Order { Amount = 100, IsPremium = true };
        var result = Result.Ok(order);

        // Act
        var actual = result.When(
            o => o.IsPremium,
            o => Result.Ok(new Order { Amount = o.Amount * 0.9m, IsPremium = o.IsPremium }));

        // Assert
        actual.Should().BeSuccess();
        actual.Value.Amount.Should().Be(90);
    }

    [Fact]
    public async Task WhenAsync_ValidateOnlyIfAmountExceedsThreshold()
    {
        // Arrange
        var transaction = new Transaction { Amount = 15000 };
        var result = Result.Ok(transaction);
        var validationExecuted = false;

        // Act
        var actual = await result.WhenAsync(
            t => t.Amount > 10000,
            t =>
            {
                validationExecuted = true;
                return Task.FromResult(Result.Ok(t));
            });

        // Assert
        validationExecuted.Should().BeTrue();
        actual.Should().BeSuccess();
    }

    [Fact]
    public void Unless_SkipProcessingForInactiveUsers()
    {
        // Arrange
        var user = new User { IsActive = false };
        var result = Result.Ok(user);
        var processingExecuted = false;

        // Act
        var actual = result.Unless(
            u => u.IsActive,
            u => { processingExecuted = true; return Result.Fail<User>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "User is inactive" }); });

        // Assert
        processingExecuted.Should().BeTrue();
        actual.Should().BeFailure();
    }

    private class Order
    {
        public decimal Amount { get; set; }
        public bool IsPremium { get; set; }
    }

    private class Transaction
    {
        public decimal Amount { get; set; }
    }

    private class User
    {
        public bool IsActive { get; set; }
    }

    #endregion
}