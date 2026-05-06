namespace Trellis.Core.Tests.Maybes;

using Trellis;
using Trellis.Testing;

public class OptionalTests
{
    [Fact]
    public void Will_return_Maybe_Value()
    {
        // Arrange
        string? zipCode = "92874";

        // Act
        var result = Maybe.Optional(zipCode, ZipCode.TryCreate);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().BeOfType<Maybe<ZipCode>>();
        result.Unwrap().Value.Zip.Should().Be(zipCode);
    }

    [Fact]
    public void Will_return_Maybe_None()
    {
        // Arrange
        string? zipCode = null;

        // Act
        var result = Maybe.Optional(zipCode, ZipCode.TryCreate);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().BeOfType<Maybe<ZipCode>>();
        result.Unwrap().HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Will_return_Failure()
    {
        // Arrange
        string? zipCode = "Hi";

        // Act
        var result = Maybe.Optional(zipCode, ZipCode.TryCreate);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Should().BeOfType<Error.BadRequest>();
        result.Error!.Detail.Should().Be("Invalid ZipCode.");
    }

    class ZipCode
    {
        public string Zip { get; }

        private ZipCode(string zipCode) => Zip = zipCode;

        public static Result<ZipCode> TryCreate(string zipCode)
        {
            if (string.IsNullOrEmpty(zipCode)) return Result.Fail<ZipCode>(new Error.BadRequest("bad.request") { Detail = "ZipCode is required." });
            if (zipCode.Length != 5) return Result.Fail<ZipCode>(new Error.BadRequest("bad.request") { Detail = "Invalid ZipCode." });

            return Result.Ok(new ZipCode(zipCode));
        }
    }

    [Fact]
    public void Reference_overload_NullFunction_ThrowsArgumentNullException_WithFunctionParamName()
    {
        // N-C-4 (GPT-5.5 meta-review): Maybe.Optional<TIn, TOut>(TIn? value, Func<...> function)
        // for reference TIn must throw ArgumentNullException with paramName "function" rather than
        // a NullReferenceException from inside `function(value)` when the input is non-null.
        string? zipCode = "92874";
        Func<string, Result<ZipCode>> function = null!;

        var act = () => Maybe.Optional(zipCode, function);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("function");
    }

    [Fact]
    public void Value_overload_NullFunction_ThrowsArgumentNullException_WithFunctionParamName()
    {
        // N-C-4 follow-up: same shape for the value-type TIn overload.
        int? quantity = 5;
        Func<int, Result<ZipCode>> function = null!;

        var act = () => Maybe.Optional(quantity, function);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("function");
    }
}