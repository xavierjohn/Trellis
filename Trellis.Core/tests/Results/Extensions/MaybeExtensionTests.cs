namespace Trellis.Core.Tests;

using System;
using Trellis.Testing;

public class MaybeExtensionTests
{
    [Fact]
    public void Convert_nullable_struct_to_result_pass()
    {
        // Arrange
        Maybe<DateTime> date = DateTime.Now;

        // Act
        var result = date.ToResult(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
    }

    [Fact]
    public void Convert_nullable_struct_to_result_fail()
    {
        // Arrange
        Maybe<DateTime> date = default;

        // Act
        var result = date.ToResult(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

    }

    [Fact]
    public void Convert_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass my = new();
        Maybe<MyClass> maybeMy = my;

        // Act
        var result = maybeMy.ToResult(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public void Convert_nullable_class_to_result_fail()
    {
        // Arrange
        Maybe<MyClass> myClass = default;

        // Act
        var result = myClass.ToResult(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    // async class
    [Fact]
    public async Task Convert_task_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass my = new();
        Maybe<MyClass> maybeMy = my;
        var myClassTask = Task.FromResult(maybeMy);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public async Task Convert_task_nullable_class_to_result_fail()
    {
        // Arrange
        Maybe<MyClass> my = default;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass my = new();
        Maybe<MyClass> maybeMy = my;

        var myClassTask = ValueTask.FromResult(maybeMy);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_fail()
    {
        // Arrange
        Maybe<MyClass> my = default;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    // async struct
    [Fact]
    public async Task Convert_task_nullable_struct_to_result_pass()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> my = date;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
    }

    [Fact]
    public async Task Convert_task_nullable_struct_to_result_fail()
    {
        // Arrange
        Maybe<DateTime> my = default;
        var myClassTask = Task.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_pass()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> my = date;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_fail()
    {
        // Arrange
        Maybe<DateTime> my = default;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
    }

    #region ToResult with Error Factory

    [Fact]
    public void ToResult_WithErrorFactory_HasValue_ReturnsSuccess()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> maybe = date;
        var factoryInvoked = false;

        // Act
        var result = maybe.ToResult(() =>
        {
            factoryInvoked = true;
            return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." };
        });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
        factoryInvoked.Should().BeFalse("error factory should not be invoked for success");
    }

    [Fact]
    public void ToResult_WithErrorFactory_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<DateTime> maybe = default;
        var factoryInvoked = false;

        // Act
        var result = maybe.ToResult(() =>
        {
            factoryInvoked = true;
            return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." };
        });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });
        factoryInvoked.Should().BeTrue("error factory should be invoked for failure");
    }

    [Fact]
    public void ToResult_WithErrorFactory_Class_HasValue_ReturnsSuccess()
    {
        // Arrange
        var my = new MyClass();
        Maybe<MyClass> maybe = my;

        // Act
        var result = maybe.ToResult(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "MyClass not found" });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public void ToResult_WithErrorFactory_Class_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<MyClass> maybe = default;

        // Act
        var result = maybe.ToResult(() => new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "MyClass not found" });

        // Assert
        result.Should().BeFailureOfType<Error.NotFound>().Which.Detail.Should().Be("MyClass not found");
    }

    #endregion

    #region ToResultAsync with Error Factory (Task)

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_HasValue_ReturnsSuccess()
    {
        // Arrange
        var my = new MyClass();
        Maybe<MyClass> maybe = my;
        var maybeTask = Task.FromResult(maybe);
        var factoryInvoked = false;

        // Act
        var result = await maybeTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass not set." };
        });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
        factoryInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<MyClass> maybe = default;
        var maybeTask = Task.FromResult(maybe);
        var factoryInvoked = false;

        // Act
        var result = await maybeTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return new Error.Conflict(null, "conflict") { Detail = "MyClass already exists." };
        });

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
        factoryInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_Struct_HasValue_ReturnsSuccess()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> maybe = date;
        var maybeTask = Task.FromResult(maybe);

        // Act
        var result = await maybeTask.ToResultAsync(() => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
    }

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_Struct_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<DateTime> maybe = default;
        var maybeTask = Task.FromResult(maybe);

        // Act
        var result = await maybeTask.ToResultAsync(() => new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });
    }

    #endregion

    #region ToResultAsync with Error Factory (ValueTask)

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_HasValue_ReturnsSuccess()
    {
        // Arrange
        var my = new MyClass();
        Maybe<MyClass> maybe = my;
        var maybeTask = ValueTask.FromResult(maybe);
        var factoryInvoked = false;

        // Act
        var result = await maybeTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return new Error.Unauthorized() { Detail = "Not authorized." };
        });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
        factoryInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<MyClass> maybe = default;
        var maybeTask = ValueTask.FromResult(maybe);
        var factoryInvoked = false;

        // Act
        var result = await maybeTask.ToResultAsync(() =>
        {
            factoryInvoked = true;
            return new Error.Forbidden("authorization.forbidden") { Detail = "Access denied." };
        });

        // Assert
        result.Should().BeFailureOfType<Error.Forbidden>();
        factoryInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_Struct_HasValue_ReturnsSuccess()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> maybe = date;
        var maybeTask = ValueTask.FromResult(maybe);

        // Act
        var result = await maybeTask.ToResultAsync(() => new Error.Conflict(null, "domain.violation") { Detail = "Business rule violated." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_Struct_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<DateTime> maybe = default;
        var maybeTask = ValueTask.FromResult(maybe);

        // Act
        var result = await maybeTask.ToResultAsync(() => new Error.ServiceUnavailable() { Detail = "Service down." });

        // Assert
        result.Should().BeFailureOfType<Error.ServiceUnavailable>();
    }

    #endregion

    #region ToResult extension on value

    [Fact]
    public void ToResult_OnValue_ReturnsSuccessResult()
    {
        // Arrange
        var value = "hello";

        // Act
        var result = value.ToResult();

        // Assert
        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void ToResult_OnComplexType_ReturnsSuccessResult()
    {
        // Arrange
        var myClass = new MyClass();

        // Act
        var result = myClass.ToResult();

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
    }

    [Fact]
    public void ToResult_OnStruct_ReturnsSuccessResult()
    {
        // Arrange
        var date = DateTime.Now;

        // Act
        var result = date.ToResult();

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
    }

    #endregion
}