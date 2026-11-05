namespace Trellis.Core.Tests;

using System;
using Trellis.Testing;

public class EnsureNotNullMigrationTests
{
    [Fact]
    public void Convert_nullable_struct_to_result_pass()
    {
        // Arrange
        DateTime? date = DateTime.Now;

        // Act
        var result = Result.EnsureNotNull(date, new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
    }

    [Fact]
    public void Convert_nullable_struct_to_result_fail()
    {
        // Arrange
        DateTime? date = default;

        // Act
        var result = Result.EnsureNotNull(date, new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

    }

    [Fact]
    public void Convert_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass? myClass = new();

        // Act
        var result = Result.EnsureNotNull(myClass, new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
    }

    [Fact]
    public void Convert_nullable_class_to_result_fail()
    {
        // Arrange
        MyClass? myClass = default;

        // Act
        var result = Result.EnsureNotNull(myClass, new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    // async class
    [Fact]
    public async Task Convert_task_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass? my = new();
        var myClassTask = Task.FromResult((MyClass?)my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public async Task Convert_task_nullable_class_to_result_fail()
    {
        // Arrange
        MyClass? my = default;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass? my = new();

        var myClassTask = ValueTask.FromResult((MyClass?)my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_fail()
    {
        // Arrange
        MyClass? my = null;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    // async struct
    [Fact]
    public async Task Convert_task_nullable_struct_to_result_pass()
    {
        // Arrange
        DateTime? my = DateTime.Now;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(my);
    }

    [Fact]
    public async Task Convert_task_nullable_struct_to_result_fail()
    {
        // Arrange
        DateTime? my = default;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_pass()
    {
        // Arrange
        DateTime? my = DateTime.Now;

        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(my);
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_fail()
    {
        // Arrange
        DateTime? my = null;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.EnsureNotNullAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
    }

    #region EnsureNotNull with Error Factory

    [Fact]
    public void EnsureNotNull_WithErrorFactory_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        DateTime? date = DateTime.Now;
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = Result.EnsureNotNull(date, factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." }));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
        factoryCalls.CallCount.Should().Be(0, "error factory should not be invoked for success");
    }

    [Fact]
    public void EnsureNotNull_WithErrorFactory_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        DateTime? date = default;
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = Result.EnsureNotNull(date, factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." }));

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });
        factoryCalls.CallCount.Should().BeGreaterThan(0, "error factory should be invoked for failure");
    }

    [Fact]
    public void EnsureNotNull_WithErrorFactory_Class_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        MyClass? myClass = new();
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = Result.EnsureNotNull(myClass, factoryCalls.Wrap(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "MyClass not found" }));

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
        factoryCalls.CallCount.Should().Be(0, "error factory should not be invoked for success");
    }

    [Fact]
    public void EnsureNotNull_WithErrorFactory_Class_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        MyClass? myClass = default;
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = Result.EnsureNotNull(myClass, factoryCalls.Wrap(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "MyClass not found" }));

        // Assert
        result.Should().HaveErrorDetail("MyClass not found").And.BeFailureOfType<Error.NotFound>();
        factoryCalls.CallCount.Should().BeGreaterThan(0, "error factory should be invoked for failure");
    }

    #endregion

    #region EnsureNotNullAsync with Error Factory

    [Fact]
    public async Task EnsureNotNullAsync_Task_WithErrorFactory_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        MyClass? myClass = new();
        var nullableTask = Task.FromResult((MyClass?)myClass);
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await nullableTask.EnsureNotNullAsync(factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." }));

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
        factoryCalls.CallCount.Should().Be(0, "error factory should not be invoked for success");
    }

    [Fact]
    public async Task EnsureNotNullAsync_Task_WithErrorFactory_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        DateTime? date = default;
        var nullableTask = Task.FromResult(date);
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await nullableTask.EnsureNotNullAsync(factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." }));

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
        factoryCalls.CallCount.Should().BeGreaterThan(0, "error factory should be invoked for failure");
    }

    [Fact]
    public async Task EnsureNotNullAsync_ValueTask_WithErrorFactory_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        DateTime? date = DateTime.Now;
        var nullableTask = ValueTask.FromResult(date);
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await nullableTask.EnsureNotNullAsync(factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." }));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
        factoryCalls.CallCount.Should().Be(0, "error factory should not be invoked for success");
    }

    [Fact]
    public async Task EnsureNotNullAsync_ValueTask_WithErrorFactory_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        MyClass? myClass = default;
        var nullableTask = ValueTask.FromResult((MyClass?)myClass);
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await nullableTask.EnsureNotNullAsync(factoryCalls.Wrap(() => new Error.Conflict(Resource: null, Code: "conflict") { Detail = "MyClass already exists." }));

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
        factoryCalls.CallCount.Should().BeGreaterThan(0, "error factory should be invoked for failure");
    }

    #endregion

    #region Null errorFactory guards (regression — ensure ArgumentNullException is raised rather than NRE)

    [Fact]
    public void EnsureNotNull_struct_with_null_factory_throws_argument_null_exception()
    {
        DateTime? value = null;

        Action act = () => Result.EnsureNotNull(value, (Func<Error>)null!);

        act.ShouldRejectNull("errorFactory");
    }

    [Fact]
    public void EnsureNotNull_struct_with_null_factory_throws_even_when_value_present()
    {
        // Validation runs before the HasValue check so the failure mode is consistent.
        DateTime? value = DateTime.UnixEpoch;

        Action act = () => Result.EnsureNotNull(value, (Func<Error>)null!);

        act.ShouldRejectNull("errorFactory");
    }

    [Fact]
    public void EnsureNotNull_class_with_null_factory_throws_argument_null_exception()
    {
        string? value = null;

        Action act = () => Result.EnsureNotNull(value, (Func<Error>)null!);

        act.ShouldRejectNull("errorFactory");
    }

    [Fact]
    public async Task EnsureNotNullAsync_struct_with_null_factory_throws_argument_null_exception()
    {
        var act = async () => await Task.FromResult<DateTime?>(null).EnsureNotNullAsync((Func<Error>)null!);

        await act.ShouldRejectNullAsync("errorFactory");
    }

    [Fact]
    public async Task EnsureNotNullAsync_class_with_null_factory_throws_argument_null_exception()
    {
        var act = async () => await Task.FromResult<string?>(null).EnsureNotNullAsync((Func<Error>)null!);

        await act.ShouldRejectNullAsync("errorFactory");
    }

    #endregion
}

internal class MyClass
{
}