namespace Trellis.Results.Tests;

public abstract class TestBase
{
    public Error Error1 { get; } = new Error.InternalServerError("test") { Detail = "Error Message one" };
    public Error Error2 { get; } = new Error.InternalServerError("test") { Detail = "Error Message two" };

    protected class T
    {
        public static readonly T Value1 = new();

        public static readonly T Value2 = new();
    }

    protected class K
    {
        public static readonly K Value1 = new();

        public static readonly K Value2 = new();
    }
}