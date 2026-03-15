namespace Trellis.Results.Tests.Results.Extensions;

using Trellis.Testing;

public class CombineTests
{
    [Fact]
    public void Combine_one_result_and_Unit_where_both_are_success()
    {
        // Arrange
        var rHello = Result.Success("Hello")
            .Combine(Result.Success())
            .Bind(hello => Result.Success($"{hello}"));

        // Act

        // Assert
        rHello.Should().BeSuccess().Which.Should().Be("Hello");
    }

    [Fact]
    public void Combine_one_result_and_Unit_where_one_is_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Failure(Error.Validation("Bad World", "key")))
            .Bind(hello => Result.Success($"{hello}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("key", ["Bad World"]));
    }

    [Fact]
    public void Combine_two_results_where_both_are_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Success("World"))
            .Bind((hello, world) => Result.Success($"{hello} {world}"));

        // Act

        // Assert
        rHelloWorld.Should().BeSuccess().Which.Should().Be("Hello World");
    }

    [Fact]
    public void Combine_two_results_where_one_is_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Failure<string>(Error.Validation("Bad World", "key")))
            .Bind((hello, world) => Result.Success($"{hello} {world}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("key", ["Bad World"]));
    }

    [Fact]
    public void Combine_two_results_where_2nd_is_success()
    {
        // Arrange
        var rHelloWorld = Result.Failure<string>(Error.Validation("Bad World", "key"))
            .Combine(Result.Success("World"))
            .Bind((hello, world) => Result.Success($"{hello} {world}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("key", ["Bad World"]));
    }

    [Fact]
    public void Combine_two_result_and_Unit_where_both_are_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Success("World"))
            .Combine(Result.Success())
            .Bind((hello, world) => Result.Success($"{hello} {world}"));

        // Act

        // Assert
        rHelloWorld.Should().BeSuccess().Which.Should().Be("Hello World");
    }

    [Fact]
    public void Combine_two_result_and_Unit_where_one_is_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Success("World"))
            .Combine(Result.Failure(Error.Validation("Bad World", "key")))
            .Bind((hello, world) => Result.Success($"{hello} {world}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("key", ["Bad World"]));
    }

    [Fact]
    public void Combine_three_results_where_all_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Success("First"))
            .Combine(Result.Success("Last"))
            .Bind((hello, first, last) => Result.Success($"{hello} {first} {last}"));

        // Act

        // Assert
        rHelloWorld.Should().BeSuccess().Which.Should().Be("Hello First Last");
    }

    [Fact]
    public void Combine_three_results_where_one_is_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Failure<string>(Error.Validation("Bad First", "First")))
            .Combine(Result.Failure<string>(Error.Validation("Bad Last", "Last")))
            .Bind((hello, first, last) => Result.Success($"{hello} {first} {last}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError("First", ["Bad First"]));
        validation.FieldErrors[1].Should().BeEquivalentTo(new ValidationError.FieldError("Last", ["Bad Last"]));
    }

    [Fact]
    public void Combine_nine_results_where_all_success()
    {
        // Arrange
        var rHelloWorld = Result.Success("1")
            .Combine(Result.Success("2"))
            .Combine(Result.Success("3"))
            .Combine(Result.Success("4"))
            .Combine(Result.Success("5"))
            .Combine(Result.Success("6"))
            .Combine(Result.Success("7"))
            .Combine(Result.Success("8"))
            .Combine(Result.Success("9"))
            .Bind((one, two, three, four, five, six, seven, eight, nine) => Result.Success($"{one}{two}{three}{four}{five}{six}{seven}{eight}{nine}"));

        // Act

        // Assert
        rHelloWorld.Should().BeSuccess().Which.Should().Be("123456789");
    }

    [Fact]
    public void Combine_nine_results_with_one_failure()
    {
        // Arrange
        var rHelloWorld = Result.Success("1")
            .Combine(Result.Success("2"))
            .Combine(Result.Success("3"))
            .Combine(Result.Success("4"))
            .Combine(Result.Success("5"))
            .Combine(Result.Success("6"))
            .Combine(Result.Success("7"))
            .Combine(Result.Success("8"))
            .Combine(Result.Failure<string>(Error.Validation("Bad 9")))
            .Bind((one, two, three, four, five, six, seven, eight, nine) => Result.Success($"{one}{two}{three}{four}{five}{six}{seven}{eight}{nine}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().ContainSingle();
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError(string.Empty, ["Bad 9"]));
    }

    [Fact]
    public void Combine_nine_results_with_two_failure()
    {
        // Arrange
        var rHelloWorld = Result.Success("1")
            .Combine(Result.Success("2"))
            .Combine(Result.Failure<string>(Error.Validation("Bad 3")))
            .Combine(Result.Success("4"))
            .Combine(Result.Success("5"))
            .Combine(Result.Success("6"))
            .Combine(Result.Success("7"))
            .Combine(Result.Success("8"))
            .Combine(Result.Failure<string>(Error.Validation("Bad 9")))
            .Bind((one, two, three, four, five, six, seven, eight, nine) => Result.Success($"{one}{two}{three}{four}{five}{six}{seven}{eight}{nine}"));

        // Act

        // Assert
        var validation = rHelloWorld.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(1);
        validation.FieldErrors[0].Should().BeEquivalentTo(new ValidationError.FieldError(string.Empty, ["Bad 3", "Bad 9"]));
    }

    [Fact]
    public void Combine_validation_and_unexpected_error_will_return_aggregated_error()
    {
        // Arrange
        var called = false;

        // Act
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Failure<string>(Error.Validation("Bad First", "First")))
            .Combine(Result.Failure<string>(Error.Unexpected("Server error")))
            .Bind((hello, first, last) => Result.Success($"{hello} {first} {last}"));

        // Assert
        called.Should().BeFalse();
        var ag = rHelloWorld.Should().BeFailureOfType<AggregateError>().Which;
        ag.Errors.Should().HaveCount(2);
        ag.Errors[0].Should().Be(Error.Validation("Bad First", "First"));
        ag.Errors[1].Should().Be(Error.Unexpected("Server error"));

    }

    [Fact]
    public void Combine_non_validation_error_will_return_aggregated_error()
    {
        // Arrange
        var called = false;

        // Act
        var rHelloWorld = Result.Success("Hello")
            .Combine(Result.Failure<string>(Error.Forbidden("You can't touch this.")))
            .Combine(Result.Failure<string>(Error.Unexpected("Server error")))
            .Bind((hello, first, last) =>
            {
                called = true;
                return Result.Success($"{hello} {first} {last}");
            });

        // Assert
        called.Should().BeFalse();
        var ag = rHelloWorld.Should().BeFailureOfType<AggregateError>().Which;
        ag.Errors.Should().HaveCount(2);
        ag.Errors[0].Should().Be(Error.Forbidden("You can't touch this."));
        ag.Errors[1].Should().Be(Error.Unexpected("Server error"));

    }

    [Fact]
    public async Task Combine_async_task_results_where_both_are_success()
    {
        // Arrange
        var rHelloWorld = await Task.FromResult(Result.Success("Hello"))
            .CombineAsync(Result.Success("World"))
            .BindAsync((hello, world) => Result.Success($"{hello} {world}"));

        // Act

        // Assert
        rHelloWorld.Should().BeSuccess().Which.Should().Be("Hello World");
    }

    [Fact]
    public void Merge_validation_errors_by_field_name_concatenates_distinct_messages_preserving_order()
    {

        // Arrange
        var expectedField1 = new ValidationError.FieldError("Field1", ["Message C", "Message D"]);
        var expectedField2 = new ValidationError.FieldError("Field2", ["Message B", "Message A"]);
        var expectedField3 = new ValidationError.FieldError("Field3", ["Message E", "Message F"]);

        var error1 = Error.Validation("Message B", "Field2");
        var error2 = Error.Validation("Message A", "Field2");
        var error3 = Error.Validation("Message A", "Field2"); // duplicate message
        var error4 = Error.Validation("Message C", "Field1");
        var error5 = Error.Validation("Message D", "Field1");
        var error6 = Error.Validation("Message C", "Field1"); // duplicate message
        var error7 = Error.Validation("Message E", "Field3");
        var error8 = Error.Validation("Message F", "Field3");
        var error9 = Error.Validation("Message E", "Field3"); // duplicate message

        var result1 = Result.Failure<string>(error1);
        var result2 = Result.Failure<string>(error2);
        var result3 = Result.Failure<string>(error3);
        var result4 = Result.Failure<string>(error4);
        var result5 = Result.Failure<string>(error5);
        var result6 = Result.Failure<string>(error6);
        var result7 = Result.Failure<string>(error7);
        var result8 = Result.Failure<string>(error8);
        var result9 = Result.Failure<string>(error9);

        // Act
        var merged = result1
            .Combine(result2)
            .Combine(result3)
            .Combine(result4)
            .Combine(result5)
            .Combine(result6)
            .Combine(result7)
            .Combine(result8)
            .Combine(result9);

        // Assert
        var validation = merged.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(3);

        validation.FieldErrors.Should().BeEquivalentTo(
            [expectedField2, expectedField1, expectedField3],
            options => options.WithStrictOrdering()
        );
    }

    #region Static Result.Combine Tests (2-tuple comprehensive, 3 and 9 validation)

    [Fact]
    public void StaticCombine_2Tuple_AllSuccess_ReturnsTuple()
    {
        var r1 = Result.Success("Hello");
        var r2 = Result.Success("World");

        var result = Result.Combine(r1, r2);

        result.Should().BeSuccess().Which.Should().Be(("Hello", "World"));
    }

    [Fact]
    public void StaticCombine_2Tuple_FirstFails_ReturnsFailure()
    {
        var r1 = Result.Failure<string>(Error.Validation("bad", "f1"));
        var r2 = Result.Success("World");

        var result = Result.Combine(r1, r2);

        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public void StaticCombine_2Tuple_SecondFails_ReturnsFailure()
    {
        var r1 = Result.Success("Hello");
        var r2 = Result.Failure<string>(Error.Validation("bad", "f2"));

        var result = Result.Combine(r1, r2);

        result.Should().BeFailureOfType<ValidationError>();
    }

    [Fact]
    public void StaticCombine_2Tuple_BothFail_AggregatesErrors()
    {
        var r1 = Result.Failure<string>(Error.Validation("e1", "f1"));
        var r2 = Result.Failure<string>(Error.Validation("e2", "f2"));

        var result = Result.Combine(r1, r2);

        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(2);
    }

    [Fact]
    public void StaticCombine_2Tuple_DifferentTypes_ReturnsTypedTuple()
    {
        var r1 = Result.Success(42);
        var r2 = Result.Success("text");

        var result = Result.Combine(r1, r2);

        result.Should().BeSuccess().Which.Should().Be((42, "text"));
    }

    [Fact]
    public void StaticCombine_2Tuple_CanChainBind()
    {
        var r1 = Result.Success("Hello");
        var r2 = Result.Success("World");

        var result = Result.Combine(r1, r2)
            .Bind((hello, world) => Result.Success($"{hello} {world}"));

        result.Should().BeSuccess().Which.Should().Be("Hello World");
    }

    [Fact]
    public void StaticCombine_3Tuple_AllSuccess_ReturnsTuple()
    {
        var r1 = Result.Success("a");
        var r2 = Result.Success("b");
        var r3 = Result.Success("c");

        var result = Result.Combine(r1, r2, r3);

        result.Should().BeSuccess().Which.Should().Be(("a", "b", "c"));
    }

    [Fact]
    public void StaticCombine_3Tuple_OneFails_ReturnsFailure()
    {
        var r1 = Result.Success("a");
        var r2 = Result.Failure<string>(Error.Validation("bad", "f"));
        var r3 = Result.Success("c");

        var result = Result.Combine(r1, r2, r3);

        result.Should().BeFailure();
    }

    [Fact]
    public void StaticCombine_9Tuple_AllSuccess_ReturnsTuple()
    {
        var result = Result.Combine(
            Result.Success(1),
            Result.Success(2),
            Result.Success(3),
            Result.Success(4),
            Result.Success(5),
            Result.Success(6),
            Result.Success(7),
            Result.Success(8),
            Result.Success(9));

        result.Should().BeSuccess().Which.Should().Be((1, 2, 3, 4, 5, 6, 7, 8, 9));
    }

    [Fact]
    public void StaticCombine_9Tuple_MultipleFail_AggregatesErrors()
    {
        var result = Result.Combine(
            Result.Success(1),
            Result.Failure<int>(Error.Validation("e1", "f1")),
            Result.Success(3),
            Result.Success(4),
            Result.Failure<int>(Error.Validation("e2", "f2")),
            Result.Success(6),
            Result.Success(7),
            Result.Success(8),
            Result.Failure<int>(Error.Validation("e3", "f3")));

        var validation = result.Should().BeFailureOfType<ValidationError>().Which;
        validation.FieldErrors.Should().HaveCount(3);
    }

    [Fact]
    public void StaticCombine_CanBeUsedInPipeline()
    {
        var emailResult = Result.Success("user@example.com");
        var nameResult = Result.Success("John");
        var ageResult = Result.Success(30);

        var result = Result.Combine(emailResult, nameResult, ageResult)
            .Map((email, name, age) => $"{name} ({email}), age {age}");

        result.Should().BeSuccess().Which.Should().Be("John (user@example.com), age 30");
    }

    #endregion
}