namespace Trellis.Testing.Tests.Assertions;

[Collection(nameof(ValidationErrorAssertionsConfiguration))]
public class ValidationErrorAssertionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HaveOnlyViolations_GlobalStrictOrdering_ReorderedPayloads_Passes(bool fieldViolations)
    {
        var previous = new FluentAssertions.Equivalency.EquivalencyAssertionOptions();
        AssertionOptions.AssertEquivalencyUsing(options =>
        {
            previous = options;
            return new FluentAssertions.Equivalency.EquivalencyAssertionOptions().WithStrictOrdering();
        });
        try
        {
            if (fieldViolations)
            {
                var first = new FieldViolation(InputPointer.ForBody("/quantity"), "test.minimum");
                var second = new FieldViolation(InputPointer.ForQuery("limit"), "test.maximum");
                var error = new Error.InvalidInput(EquatableArray.Create(first, second));

                error.Should().HaveOnlyFieldViolations([second, first]);
            }
            else
            {
                var first = new RuleViolation("test.range");
                var second = new RuleViolation("test.other");
                var error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty, EquatableArray.Create(first, second));

                error.Should().HaveOnlyRuleViolations([second, first]);
            }
        }
        finally
        {
            AssertionOptions.AssertEquivalencyUsing(_ => previous);
        }
    }

    [Fact]
    public void HaveOnlyViolations_NullExpected_RejectsWithParameterName()
    {
        var error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty);
        var fields = () => error.Should().HaveOnlyFieldViolations(null!);
        var rules = () => error.Should().HaveOnlyRuleViolations(null!);

        fields.Should().Throw<ArgumentNullException>().WithParameterName("expected");
        rules.Should().Throw<ArgumentNullException>().WithParameterName("expected");
    }

    [Fact]
    public void HaveFieldViolation_FullPointerAndCode_ReturnsOriginalViolation()
    {
        var field = InputPointer.ForBody("/items/0/quantity");
        var violation = new FieldViolation(field, "test.minimum", ValidationArgs.Of("min", 2));
        var error = new Error.InvalidInput(EquatableArray.Create(violation));

        error.Should().HaveFieldViolation(field, "test.minimum").Which.Should().BeSameAs(violation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HaveFieldViolation_WrongCodeOrLocation_ReportsMismatch(bool wrongCode)
    {
        var field = InputPointer.ForBody("/quantity");
        var error = Error.InvalidInput.ForField("test.minimum", field);
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveFieldViolation(
            wrongCode ? field : InputPointer.ForQuery("quantity"),
            wrongCode ? "test.maximum" : "test.minimum", "the payload must match");

        scope.Discard().Should().ContainSingle().Which.Should()
            .Contain("test.minimum").And.Contain("the payload must match");
    }

    [Fact]
    public void HaveFieldViolation_DuplicateMatches_ReportsAmbiguity()
    {
        var field = InputPointer.ForBody("/quantity");
        var violation = new FieldViolation(field, "test.minimum");
        var error = new Error.InvalidInput(EquatableArray.Create(violation, violation));
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveFieldViolation(field, "test.minimum");

        scope.Discard().Should().ContainSingle().Which.Should().Contain("exactly one");
    }

    [Fact]
    public void HaveRuleViolation_UniqueCode_ReturnsOriginalViolation()
    {
        var violation = new RuleViolation("test.range",
            EquatableArray.Create(InputPointer.ForBody("/from"), InputPointer.ForBody("/to")),
            ValidationArgs.Of("max", 10));
        var error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty, EquatableArray.Create(violation));

        error.Should().HaveRuleViolation("test.range").Which.Should().BeSameAs(violation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HaveRuleViolation_MissingOrDuplicateCode_ReportsMismatch(int count)
    {
        var violation = new RuleViolation("test.range");
        var error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty,
            EquatableArray.From(Enumerable.Repeat(violation, count)));
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveRuleViolation("test.range");

        scope.Discard().Should().ContainSingle().Which.Should().Contain("exactly one");
    }

    [Fact]
    public void HaveViolationCounts_DuplicateFields_PreservesDistinctFieldCount()
    {
        var field = InputPointer.ForBody("/quantity");
        var error = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(field, "test.minimum"), new FieldViolation(field, "test.maximum")),
            EquatableArray.Create(new RuleViolation("test.range")));

        error.Should().HaveFieldCount(1).And.HaveFieldViolationCount(2).And.HaveRuleViolationCount(1);
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveFieldViolationCount(1).And.HaveRuleViolationCount(0);

        scope.Discard().Should().HaveCount(2);
    }

    [Fact]
    public void HaveOnlyFieldViolations_ReorderedEqualPayloads_Passes()
    {
        var first = new FieldViolation(InputPointer.ForBody("/quantity"), "test.minimum", ValidationArgs.Of("min", 2));
        var second = new FieldViolation(InputPointer.ForQuery("limit"), "test.maximum");
        var error = new Error.InvalidInput(EquatableArray.Create(first, second));

        error.Should().HaveOnlyFieldViolations([
            second with { },
            new FieldViolation(first.Field, first.ReasonCode, ValidationArgs.Of("min", 2))
        ]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void HaveOnlyFieldViolations_ChangedPayloadOrMultiplicity_ReportsMismatch(int variant)
    {
        var expected = new FieldViolation(InputPointer.ForBody("/quantity"), "test.minimum", ValidationArgs.Of("min", 2));
        var actual = variant switch
        {
            0 => expected with { Field = InputPointer.ForQuery("quantity") },
            1 => expected with { ReasonCode = "test.maximum" },
            2 => expected with { Args = ValidationArgs.Of("min", "2") },
            3 => expected with { Detail = "different" },
            _ => expected
        };
        var fields = variant == 4 ? EquatableArray.Create(actual, actual)
            : variant == 5 ? EquatableArray<FieldViolation>.Empty : EquatableArray.Create(actual);
        var error = new Error.InvalidInput(fields);
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveOnlyFieldViolations([expected]);

        scope.Discard().Should().NotBeEmpty();
    }

    [Fact]
    public void HaveOnlyRuleViolations_ValuePayloads_PreservesRelatedFieldOrderAndMultiplicity()
    {
        var first = new RuleViolation("test.range",
            EquatableArray.Create(InputPointer.ForBody("/from"), InputPointer.ForBody("/to")),
            ValidationArgs.Of("max", 10));
        var second = new RuleViolation("test.other");
        var error = new Error.InvalidInput(EquatableArray<FieldViolation>.Empty, EquatableArray.Create(first, second));

        error.Should().HaveOnlyRuleViolations([
            second with { },
            new RuleViolation(first.ReasonCode, first.Fields, ValidationArgs.Of("max", 10))
        ]);
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveOnlyRuleViolations([first with { Fields = EquatableArray.From(first.Fields.Items.Reverse()) }, second]);
        error.Should().HaveOnlyRuleViolations([first with { Args = ValidationArgs.Of("max", "10") }, second]);
        error.Should().HaveOnlyRuleViolations([first, second, second]);

        scope.Discard().Should().HaveCount(3);
    }

    [Fact]
    public void StructuredAssertions_NullSubject_CollectFailuresWithoutDereferencing()
    {
        Error.InvalidInput error = null!;
        using var scope = new FluentAssertions.Execution.AssertionScope();

        error.Should().HaveFieldViolation(InputPointer.Root, "test.required");
        error.Should().HaveRuleViolation("test.required");
        error.Should().HaveFieldViolationCount(0).And.HaveRuleViolationCount(0);
        error.Should().HaveOnlyFieldViolations([]).And.HaveOnlyRuleViolations([]);

        scope.Discard().Should().HaveCount(6);
    }

    [Fact]
    public void HaveFieldError_Should_Pass_When_Field_Error_Exists()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" }));

        // Act & Assert
        error.Should().HaveFieldError("email");
    }

    [Fact]
    public void HaveFieldError_Should_Fail_When_Field_Error_Missing()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" }));

        // Act & Assert
        var act = () => error.Should().HaveFieldError("password");

        act.Should().Throw<Exception>()
            .WithMessage("*to contain field*password*");
    }

    [Fact]
    public void HaveFieldErrorWithDetail_Should_Pass_When_Detail_Matches()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" }));

        // Act & Assert
        error.Should().HaveFieldErrorWithDetail("email", "Email is required");
    }

    [Fact]
    public void HaveFieldErrorWithDetail_Should_Fail_When_Detail_Different()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" }));

        // Act & Assert
        var act = () => error.Should().HaveFieldErrorWithDetail("email", "Invalid format");

        act.Should().Throw<Exception>()
            .WithMessage("*to have detail*Invalid format*");
    }

    [Fact]
    public void HaveFieldCount_Should_Pass_When_Count_Matches()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" },
            new FieldViolation(InputPointer.ForProperty("password"), ValidationCodes.Unspecified) { Detail = "Password is required" },
            new FieldViolation(InputPointer.ForProperty("age"), ValidationCodes.Unspecified) { Detail = "Invalid age" }));

        // Act & Assert
        error.Should().HaveFieldCount(3);
    }

    [Fact]
    public void HaveFieldCount_Should_Fail_When_Count_Different()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" }));

        // Act & Assert
        var act = () => error.Should().HaveFieldCount(3);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Should_Support_Chained_Assertions()
    {
        // Arrange
        var error = new Error.InvalidInput(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("email"), ValidationCodes.Unspecified) { Detail = "Email is required" },
            new FieldViolation(InputPointer.ForProperty("password"), ValidationCodes.Unspecified) { Detail = "Password is required" }));

        // Act & Assert
        error.Should()
            .HaveFieldCount(2)
            .And.HaveFieldError("email")
            .And.HaveFieldError("password")
            .And.HaveFieldErrorWithDetail("email", "Email is required");
    }
}

[CollectionDefinition(nameof(ValidationErrorAssertionsConfiguration), DisableParallelization = true)]
public sealed class ValidationErrorAssertionsConfiguration
{
}