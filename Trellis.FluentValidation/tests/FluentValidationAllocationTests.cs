namespace Trellis.FluentValidation.Tests;

using System.Linq;
using global::FluentValidation;
using global::FluentValidation.Results;
using Trellis.Testing;

public class FluentValidationAllocationTests
{
    [Theory]
    [InlineData("URLValue", "/uRLValue")]
    [InlineData("\u00C9mail", "/\u00E9mail")]
    public void ToJsonPointer_SimpleName_SingleSegment_AllocatesOnlyThePointer(string name, string expected)
    {
        var (stringBytes, _) = Measure(() => new string('x', expected.Length));
        var (bytes, pointer) = Measure(() => JsonPointerNormalizer.ToJsonPointer(name));

        pointer.Should().Be(expected);
        bytes.Should().BeLessThan(stringBytes + 16, "only the resulting pointer should be allocated");
    }

    [Fact]
    public void ToResult_FailureProjection_RepeatedField_PreservesOrderWithoutAnIntermediateArray()
    {
        const int count = 128;
        var missing = new ValidationFailure("", "Missing") { ErrorCode = "NotNullValidator" };
        var invalid = new ValidationFailure("/name", "Invalid") { ErrorCode = "EmailValidator" };
        var failures = new ValidationResult(Enumerable.Range(0, count).Select(i => i % 2 == 0 ? missing : invalid));

        var (fieldBytes, _) = Measure(static () => new FieldViolation(new InputPointer("/name"), ValidationCodes.ValueNotNull));
        var (bytes, result) = Measure(() => failures.ToResult("name", paramName: "/name"));

        var fields = result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items;
        fields.Should().HaveCount(count).And.OnlyContain(field => field.Field.Path == "/name");
        fields.Select(field => field.ReasonCode).Should()
            .Equal(Enumerable.Range(0, count).Select(i => i % 2 == 0 ? ValidationCodes.ValueNotNull : ValidationCodes.StringEmail));
        bytes.Should().BeLessThan((count * (fieldBytes + IntPtr.Size)) + 256,
            "each violation needs one field object and one array slot, plus fixed result overhead");
    }

    [Fact]
    public void ValidateToResult_NullInput_PreformedPointer_ReturnsOneViolationWithinAllocationBudget()
    {
        var validator = new InlineValidator<string?>();
        var options = new ValidationArgsOptions().AllowArgs("NotNullValidator", "MinLength");

        var (bytes, result) = Measure(() => validator.ValidateToResult(null, "/Alias", argsOptions: options));

        var field = result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Should().ContainSingle().Which;
        field.Field.Should().Be(new InputPointer("/Alias"));
        field.ReasonCode.Should().Be(ValidationCodes.ValueNotNull);
        field.Detail.Should().Be("'/Alias' must not be empty.");
        field.Args.Should().BeNull();
        bytes.Should().BeLessThan(400);
    }

    [Fact]
    public void Project_EnumFailure_RepeatedType_ReusesMemberMetadata()
    {
        var failure = new ValidationFailure("Day", "Invalid day", (DayOfWeek)99) { ErrorCode = "EnumValidator" };

        var (bytes, args) = Measure(() => ValidationArgsProjection.Project(failure));

        args.Should().Equal(ValidationArgs.Allowed(Enum.GetNames<DayOfWeek>()));
        bytes.Should().BeLessThan(64);
    }

    private static (long BytesPerCall, T Result) Measure<T>(Func<T> action)
    {
        const int iterations = 256;
        for (var i = 0; i < iterations; i++)
            _ = action();

        T result = action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
            result = action();

        return ((GC.GetAllocatedBytesForCurrentThread() - before) / iterations, result);
    }
}
