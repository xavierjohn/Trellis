namespace Trellis.Results.Tests;

using System;
using Trellis.Testing;

public class NullableExtensionTests
{
    [Fact]
    public void Convert_nullable_struct_to_result_pass()
    {
        // Arrange
        DateTime? date = DateTime.Now;

        // Act
        var result = date.ToResult(Error.Validation("Date not set."));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(date.Value);
    }

    [Fact]
    public void Convert_nullable_struct_to_result_fail()
    {
        // Arrange
        DateTime? date = default;

        // Act
        var result = date.ToResult(Error.Validation("Date not set."));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation("Date not set."));

    }

    [Fact]
    public void Convert_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass? myClass = new();

        // Act
        var result = myClass.ToResult(Error.Validation("MyClass is not set."));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(myClass);
    }

    [Fact]
    public void Convert_nullable_class_to_result_fail()
    {
        // Arrange
        MyClass? myClass = default;

        // Act
        var result = myClass.ToResult(Error.Validation("MyClass is not set."));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation("MyClass is not set."));
    }

    // async class
    [Fact]
    public async Task Convert_task_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass? my = new();
        var myClassTask = Task.FromResult((MyClass?)my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("MyClass is not set."));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(my);
    }

    [Fact]
    public async Task Convert_task_nullable_class_to_result_fail()
    {
        // Arrange
        MyClass? my = default;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("MyClass is not set."));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation("MyClass is not set."));
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass? my = new();

        var myClassTask = ValueTask.FromResult((MyClass?)my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("MyClass is not set."));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(my);
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_fail()
    {
        // Arrange
        MyClass? my = null;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("MyClass is not set."));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation("MyClass is not set."));
    }

    // async struct
    [Fact]
    public async Task Convert_task_nullable_struct_to_result_pass()
    {
        // Arrange
        DateTime? my = DateTime.Now;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("Date is not set."));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(my);
    }

    [Fact]
    public async Task Convert_task_nullable_struct_to_result_fail()
    {
        // Arrange
        DateTime? my = default;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("Date is not set."));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation("Date is not set."));
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_pass()
    {
        // Arrange
        DateTime? my = DateTime.Now;

        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("Date is not set."));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(my);
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_fail()
    {
        // Arrange
        DateTime? my = null;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(Error.Validation("Date is not set."));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Validation("Date is not set."));
    }

    #region ToResult with Error Factory

    [Fact]
    public void ToResult_WithErrorFactory_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        DateTime? date = DateTime.Now;
        var factoryInvoked = false;

        // Act
        var result = date.ToResult(() =>
        {
            factoryInvoked = true;
            return Error.Validation("Date not set.");
        });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
        factoryInvoked.Should().BeFalse("error factory should not be invoked for success");
    }

    [Fact]
    public void ToResult_WithErrorFactory_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        DateTime? date = default;
        var factoryInvoked = false;

        // Act
        var result = date.ToResult(() =>
        {
            factoryInvoked = true;
            return Error.Validation("Date not set.");
        });

        // Assert
        result.Should().BeFailure().Which.Should().Be(Error.Validation("Date not set."));
        factoryInvoked.Should().BeTrue("error factory should be invoked for failure");
    }

    [Fact]
    public void ToResult_WithErrorFactory_Class_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        MyClass? myClass = new();
        var factoryInvoked = false;

        // Act
        var result = myClass.ToResult(() =>
        {
            factoryInvoked = true;
            return Error.NotFound("MyClass not found");
        });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
        factoryInvoked.Should().BeFalse("error factory should not be invoked for success");
    }

    [Fact]
    public void ToResult_WithErrorFactory_Class_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        MyClass? myClass = default;
        var factoryInvoked = false;

        // Act
        var result = myClass.ToResult(() =>
        {
            factoryInvoked = true;
            return Error.NotFound("MyClass not found");
        });

        // Assert
        result.Should().BeFailureOfType<NotFoundError>().Which.Detail.Should().Be("MyClass not found");
        factoryInvoked.Should().BeTrue("error factory should be invoked for failure");
    }

    #endregion

    #region ToResultAsync with Error Factory

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        MyClass? myClass = new();
        var nullableTask = Task.FromResult((MyClass?)myClass);
        var factoryInvoked = false;

        // Act
        var result = await nullableTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return Error.Validation("MyClass is not set.");
        });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
        factoryInvoked.Should().BeFalse("error factory should not be invoked for success");
    }

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        DateTime? date = default;
        var nullableTask = Task.FromResult(date);
        var factoryInvoked = false;

        // Act
        var result = await nullableTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return Error.Validation("Date is not set.");
        });

        // Assert
        result.Should().BeFailure().Which.Should().Be(Error.Validation("Date is not set."));
        factoryInvoked.Should().BeTrue("error factory should be invoked for failure");
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_HasValue_ReturnsSuccessWithoutInvokingFactory()
    {
        // Arrange
        DateTime? date = DateTime.Now;
        var nullableTask = ValueTask.FromResult(date);
        var factoryInvoked = false;

        // Act
        var result = await nullableTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return Error.Validation("Date is not set.");
        });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
        factoryInvoked.Should().BeFalse("error factory should not be invoked for success");
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_HasNoValue_ReturnsFailureAndInvokesFactory()
    {
        // Arrange
        MyClass? myClass = default;
        var nullableTask = ValueTask.FromResult((MyClass?)myClass);
        var factoryInvoked = false;

        // Act
        var result = await nullableTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return Error.Conflict("MyClass already exists.");
        });

        // Assert
        result.Should().BeFailureOfType<ConflictError>();
        factoryInvoked.Should().BeTrue("error factory should be invoked for failure");
    }

    #endregion
}

internal class MyClass
{
}