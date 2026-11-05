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
        var result = date.ToResult(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date.Value);
    }

    [Fact]
    public void Convert_nullable_struct_to_result_fail()
    {
        // Arrange
        Maybe<DateTime> date = default;

        // Act
        var result = date.ToResult(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

    }

    [Fact]
    public void Convert_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass my = new();
        Maybe<MyClass> maybeMy = my;

        // Act
        var result = maybeMy.ToResult(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
    }

    [Fact]
    public void Convert_nullable_class_to_result_fail()
    {
        // Arrange
        Maybe<MyClass> myClass = default;

        // Act
        var result = myClass.ToResult(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
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
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

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
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
    }

    [Fact]
    public async Task Convert_valuetask_nullable_class_to_result_pass()
    {
        // Arrange
        MyClass my = new();
        Maybe<MyClass> maybeMy = my;

        var myClassTask = ValueTask.FromResult(maybeMy);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

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
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass is not set." });
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
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

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
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
    }

    [Fact]
    public async Task Convert_valuetask_nullable_struct_to_result_pass()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> my = date;
        var myClassTask = ValueTask.FromResult(my);

        // Act
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

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
        var result = await myClassTask.ToResultAsync(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date is not set." });
    }

    #region ToResult with Error Factory

    [Fact]
    public void ToResult_WithErrorFactory_HasValue_ReturnsSuccess()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> maybe = date;
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = maybe.ToResult(factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." }));

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
        factoryCalls.CallCount.Should().Be(0, "error factory should not be invoked for success");
    }

    [Fact]
    public void ToResult_WithErrorFactory_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<DateTime> maybe = default;
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = maybe.ToResult(factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." }));

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });
        factoryCalls.CallCount.Should().BeGreaterThan(0, "error factory should be invoked for failure");
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
        result.Should().HaveErrorDetail("MyClass not found").And.BeFailureOfType<Error.NotFound>();
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
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await maybeTask.ToResultAsync(factoryCalls.Wrap(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "MyClass not set." }));

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
        factoryCalls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<MyClass> maybe = default;
        var maybeTask = Task.FromResult(maybe);
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await maybeTask.ToResultAsync(factoryCalls.Wrap(() => new Error.Conflict(Resource: null, Code: "conflict") { Detail = "MyClass already exists." }));

        // Assert
        result.Should().BeFailureOfType<Error.Conflict>();
        factoryCalls.CallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ToResultAsync_Task_WithErrorFactory_Struct_HasValue_ReturnsSuccess()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> maybe = date;
        var maybeTask = Task.FromResult(maybe);

        // Act
        var result = await maybeTask.ToResultAsync(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

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
        var result = await maybeTask.ToResultAsync(() => new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });

        // Assert
        result.Should().BeFailure().Which.Should().Be(new Error.InvalidInput(EquatableArray<FieldViolation>.Empty) { Detail = "Date not set." });
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
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await maybeTask.ToResultAsync(factoryCalls.Wrap(() => new Error.AuthenticationRequired() { Detail = "Not authorized." }));

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(my);
        factoryCalls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_HasNoValue_ReturnsFailure()
    {
        // Arrange
        Maybe<MyClass> maybe = default;
        var maybeTask = ValueTask.FromResult(maybe);
        var factoryCalls = new CallbackRecorder();

        // Act
        var result = await maybeTask.ToResultAsync(factoryCalls.Wrap(() => new Error.Forbidden("authorization.forbidden") { Detail = "Access denied." }));

        // Assert
        result.Should().BeFailureOfType<Error.Forbidden>();
        factoryCalls.CallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WithErrorFactory_Struct_HasValue_ReturnsSuccess()
    {
        // Arrange
        var date = DateTime.Now;
        Maybe<DateTime> maybe = date;
        var maybeTask = ValueTask.FromResult(maybe);

        // Act
        var result = await maybeTask.ToResultAsync(() => new Error.Conflict(Resource: null, Code: "domain.violation") { Detail = "Business rule violated." });

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
        var result = await maybeTask.ToResultAsync(() => new Error.Unavailable() { Detail = "Service down." });

        // Assert
        result.Should().BeFailureOfType<Error.Unavailable>();
    }

    #endregion

    #region Explicit success wrapping

    [Fact]
    public void Ok_OnValue_ReturnsSuccessResult()
    {
        // Arrange
        var value = "hello";

        // Act
        var result = Result.Ok(value);

        // Assert
        result.Should().BeSuccess().Which.Should().Be("hello");
    }

    [Fact]
    public void Ok_OnComplexType_ReturnsSuccessResult()
    {
        // Arrange
        var myClass = new MyClass();

        // Act
        var result = Result.Ok(myClass);

        // Assert
        result.Should().BeSuccess().Which.Should().BeSameAs(myClass);
    }

    [Fact]
    public void Ok_OnStruct_ReturnsSuccessResult()
    {
        // Arrange
        var date = DateTime.Now;

        // Act
        var result = Result.Ok(date);

        // Assert
        result.Should().BeSuccess().Which.Should().Be(date);
    }

    #endregion
}