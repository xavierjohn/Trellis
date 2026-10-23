namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

internal static class ReasonCodeGuardTestCases
{
    internal const string SingleWriteLocal = """
        var incident = Guid.NewGuid();
        var code = incident.ToString("N");
        Error.Unexpected error = new(Code: code);
        """;

    internal const string MutableCollectionSpread = """
        var parts = new[] { Guid.NewGuid().ToString("N") };
        parts[0] = "order.failed";
        Error.Unexpected error = new(string.Concat((ReadOnlySpan<string?>)[.. parts]));
        """;

    private static readonly GuardCase[] s_cases =
    [
        new("new Error.Unexpected(Guid.NewGuid().ToString(\"N\"))", true),
        new("new(Guid.NewGuid().ToString(\"N\"))", true),
        new("new(Code: Guid.CreateVersion7().ToString(\"N\"))", true),
        new("new(\n Code: Guid.NewGuid().ToString(\"N\"),\n FaultId: \"incident\")", true),
        new("new($\"fault-{Guid.NewGuid():N}\")", true),
        new("new(string.Concat(\"fault-\", \"a\", \"b\", \"c\", Guid.NewGuid().ToString(\"N\")))", true,
            "string.Concat(params System.ReadOnlySpan<string?>)", ArgumentKind.ParamCollection,
            OperationKind.CollectionExpression, true),
        new("new(string.Concat(\"fault-\", \"a\", \"b\", Guid.NewGuid()))", true,
            "string.Concat(params System.ReadOnlySpan<object?>)", ArgumentKind.ParamCollection,
            OperationKind.CollectionExpression, true),
        new("new(string.Concat((ReadOnlySpan<string?>)[\"fault-\", Guid.NewGuid().ToString(\"N\")]))", true,
            "string.Concat(params System.ReadOnlySpan<string?>)", ArgumentKind.Explicit, OperationKind.Conversion, false),
        new("new(string.Concat((ReadOnlySpan<object?>)[\"fault-\", Guid.NewGuid()]))", true,
            "string.Concat(params System.ReadOnlySpan<object?>)", ArgumentKind.Explicit, OperationKind.Conversion, false),
        new("new(string.Concat((ReadOnlySpan<string?>)[.. new[] { \"fault-\", Guid.NewGuid().ToString(\"N\") }]))", true,
            "string.Concat(params System.ReadOnlySpan<string?>)", ArgumentKind.Explicit, OperationKind.Conversion, false, 1, 1),
        new("new(Convert.ToString(new[] { Guid.NewGuid() })!)", false,
            "System.Convert.ToString(object?)", ArgumentKind.Explicit, OperationKind.Conversion, ArrayCount: 1),
        new("new(string.Concat((object)new[] { Guid.NewGuid() }))", false,
            "string.Concat(object?)", ArgumentKind.Explicit, OperationKind.Conversion, ArrayCount: 1),
        new("new(string.Concat(new object[] { new[] { Guid.NewGuid() } }))", false,
            "string.Concat(params object?[])", ArgumentKind.Explicit, OperationKind.ArrayCreation, ArrayCount: 2),
        new("new(string.Concat((ReadOnlySpan<object?>)[.. new object[] { new[] { Guid.NewGuid() } }]))", false,
            "string.Concat(params System.ReadOnlySpan<object?>)", ArgumentKind.Explicit, OperationKind.Conversion, false, 1, 2),
        new("new(Code: \"order.failed\", FaultId: Guid.NewGuid().ToString(\"N\"))", false),
        new("new(Code: \"order.\" + nameof(Probe), FaultId: Guid.NewGuid().ToString(\"N\"))", false)
    ];

    public static TheoryData<string, bool> Expressions()
    {
        var data = new TheoryData<string, bool>();
        foreach (var item in s_cases)
            data.Add(item.Expression, item.Fails);
        return data;
    }

    public static TheoryData<string, bool, string?, ArgumentKind?, OperationKind?, bool?, int, int> Net10Bindings()
    {
        var data = new TheoryData<string, bool, string?, ArgumentKind?, OperationKind?, bool?, int, int>();
        foreach (var item in s_cases)
            data.Add(item.Expression, item.Fails, item.Method, item.ArgumentKind, item.ValueKind,
                item.CollectionIsImplicit, item.SpreadCount, item.ArrayCount);
        return data;
    }

    private sealed record GuardCase(
        string Expression,
        bool Fails,
        string? Method = null,
        ArgumentKind? ArgumentKind = null,
        OperationKind? ValueKind = null,
        bool? CollectionIsImplicit = null,
        int SpreadCount = 0,
        int ArrayCount = 0);
}