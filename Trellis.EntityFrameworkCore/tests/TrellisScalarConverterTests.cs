namespace Trellis.EntityFrameworkCore.Tests;

public class TrellisScalarConverterTests
{
    [Fact]
    public void Constructor_MissingValueProperty_ThrowsActionableInvalidOperationException()
    {
        var act = () => new TrellisScalarConverter<MissingValuePropertyScalar, int>();

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain($"Type '{typeof(MissingValuePropertyScalar).FullName}'");
        ex.Message.Should().Contain("TrellisScalarConverter<MissingValuePropertyScalar, Int32>");
        ex.Message.Should().Contain("IScalarValue<MissingValuePropertyScalar, Int32>");
        ex.Message.Should().Contain("public System.Int32 Value { get; }");
        ex.Message.Should().Contain("missing public 'Value' property");
    }

    [Fact]
    public void Constructor_WrongValuePropertyType_ThrowsActionableInvalidOperationException()
    {
        var act = () => new TrellisScalarConverter<WrongValuePropertyTypeScalar, int>();

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain($"Type '{typeof(WrongValuePropertyTypeScalar).FullName}'");
        ex.Message.Should().Contain("TrellisScalarConverter<WrongValuePropertyTypeScalar, Int32>");
        ex.Message.Should().Contain("IScalarValue<WrongValuePropertyTypeScalar, Int32>");
        ex.Message.Should().Contain("public System.Int32 Value { get; }");
        ex.Message.Should().Contain("public 'Value' property has type 'System.String'");
        ex.Message.Should().Contain("expected 'System.Int32'");
    }

    private sealed class MissingValuePropertyScalar : IScalarValue<MissingValuePropertyScalar, int>
    {
        int IScalarValue<MissingValuePropertyScalar, int>.Value => 0;

        public static Result<MissingValuePropertyScalar> TryCreate(int value, string? fieldName = null) =>
            Result.Ok(new MissingValuePropertyScalar());

        public static Result<MissingValuePropertyScalar> TryCreate(string? value, string? fieldName = null) =>
            Result.Ok(new MissingValuePropertyScalar());
    }

    private sealed class WrongValuePropertyTypeScalar : IScalarValue<WrongValuePropertyTypeScalar, int>
    {
        private readonly string _value = "not an int";

        int IScalarValue<WrongValuePropertyTypeScalar, int>.Value => 0;

        public string Value => _value;

        public static Result<WrongValuePropertyTypeScalar> TryCreate(int value, string? fieldName = null) =>
            Result.Ok(new WrongValuePropertyTypeScalar());

        public static Result<WrongValuePropertyTypeScalar> TryCreate(string? value, string? fieldName = null) =>
            Result.Ok(new WrongValuePropertyTypeScalar());
    }
}