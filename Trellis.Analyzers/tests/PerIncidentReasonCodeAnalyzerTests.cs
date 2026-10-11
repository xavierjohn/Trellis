namespace Trellis.Analyzers.Tests;

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class PerIncidentReasonCodeAnalyzerTests
{
    [Theory]
    [InlineData("new Error.Unexpected({|#0:Guid.NewGuid().ToString()|})")]
    [InlineData("new Error.Unexpected({|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected(Code: {|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected(FaultId: \"incident\", Code: {|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected({|#0:global::System.Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected({|#0:((Guid.NewGuid())).ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected({|#0:$\"{Guid.NewGuid():N}\"|})")]
    [InlineData("new Error.Unexpected({|#0:$\"fault-{Guid.NewGuid():N}\"|})")]
    [InlineData("new Error.Unexpected({|#0:\"fault-\" + Guid.NewGuid()|})")]
    [InlineData("new Error.Unexpected({|#0:\"fault-\" + Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected({|#0:Convert.ToString(Guid.NewGuid())|}!)")]
    [InlineData("new Error.Unexpected({|#0:string.Concat(\"fault-\", Guid.NewGuid())|})")]
    [InlineData("new Error.Unexpected({|#0:string.Concat(new[] { Guid.NewGuid().ToString(\"N\") })|})")]
    [InlineData("new Error.Unexpected({|#0:string.Concat(new[] { Guid.NewGuid() })|})")]
    [InlineData("new Error.Unexpected({|#0:string.Concat(new object[] { \"fault-\", Guid.NewGuid() })|})")]
    [InlineData("new Error.Unexpected({|#0:string.Concat(\"fault-\", \"a\", \"b\", \"c\", Guid.NewGuid().ToString(\"N\"))|})")]
    [InlineData("new Error.Unexpected({|#0:string.Concat(true ? new[] { Guid.NewGuid().ToString(\"N\") } : new[] { \"order.failed\" })|})")]
    [InlineData("new Error.Unexpected({|#0:true ? Guid.NewGuid().ToString(\"N\") : \"order.failed\"|})")]
    [InlineData("new Error.Unexpected({|#0:false ? \"order.failed\" : Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Unexpected({|#0:(string?)null ?? Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.Conflict({|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("Error.Conflict.For<object>({|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("Error.Conflict.For<object>(id: \"order\", code: {|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("Error.Conflict.ForReason({|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("Error.InvalidInput.ForField(field: \"name\", code: {|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new Error.NotFound(\"order\") { Code = {|#0:Guid.NewGuid().ToString(\"N\")|} }")]
    [InlineData("new FieldViolation(\"name\", {|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new RuleViolation({|#0:Guid.NewGuid().ToString(\"N\")|})")]
    [InlineData("new RuleViolation(\"order.failed\") with { ReasonCode = {|#0:Guid.NewGuid().ToString(\"N\")|} }")]
    [InlineData("FluentValidation.Codes.WithErrorCode({|#0:Guid.NewGuid().ToString(\"N\")|})")]
    public async Task Analyze_ReasonCodeSlot_FreshGuidExpression_ReportsWarning(string expression) =>
        await VerifyBodyAsync("_ = " + expression + ";", Expect(0));

    [Theory]
    [InlineData("Error.Unexpected error = new({|#0:Guid.NewGuid().ToString(\"N\")|});")]
    [InlineData("Error.Unexpected error = new(Code: {|#0:Guid.NewGuid().ToString(\"N\")|});")]
    [InlineData("RuleViolation violation = new({|#0:Guid.NewGuid().ToString(\"N\")|});")]
    [InlineData("Error.Unexpected error = new(\"order.failed\") { Code = {|#0:Guid.NewGuid().ToString(\"N\")|} };")]
    public async Task Analyze_TargetTypedConstruction_FreshGuidReason_ReportsWarning(string statement) =>
        await VerifyBodyAsync(statement, Expect(0));

    [Theory]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); _ = new Error.Unexpected({|#0:code|});")]
    [InlineData("var id = Guid.NewGuid(); _ = new Error.Unexpected({|#0:id.ToString(\"N\")|});")]
    [InlineData("var id = Guid.NewGuid(); var code = id.ToString(\"N\"); _ = new Error.Unexpected({|#0:code|});")]
    [InlineData("var id = Guid.NewGuid(); var code = $\"fault-{id:N}\"; _ = new Error.Unexpected({|#0:code|});")]
    [InlineData("string code; code = Guid.NewGuid().ToString(\"N\"); _ = new Error.Unexpected({|#0:code|});")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); _ = new Error.NotFound(\"order\") { Code = {|#0:code|} };")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); _ = FluentValidation.Codes.WithErrorCode({|#0:code|});")]
    public async Task Analyze_SingleWriteLocal_FreshGuidReason_ReportsWarning(string statements) =>
        await VerifyBodyAsync(statements, Expect(0));

    [Fact]
    public async Task Analyze_CodeAndFaultId_BothFresh_ReportsOnlyReasonCode() =>
        await VerifyBodyAsync("""
            _ = new Error.Unexpected(
                FaultId: Guid.NewGuid().ToString("N"),
                Code: {|#0:Guid.NewGuid().ToString("N")|});
            """, Expect(0));

    [Fact]
    public async Task Analyze_DirectAssignment_FreshGuidCode_ReportsWarning() =>
        await VerifyBodyAsync("""
            var error = new Foreign.MutableError();
            error.Code = {|#0:Guid.NewGuid().ToString("N")|};
            """, Expect(0));

    [Theory]
    [InlineData("new Error.Unexpected(FaultCodes.UnhandledException, Guid.NewGuid().ToString(\"N\"))")]
    [InlineData("new Error.Unexpected(FaultId: Guid.NewGuid().ToString(\"N\"), Code: FaultCodes.UnhandledException)")]
    [InlineData("Error.Conflict.For<object>(id: Guid.NewGuid().ToString(\"N\"), code: \"order.failed\")")]
    [InlineData("Error.Conflict.For<object>(\"order.failed\", Guid.NewGuid())")]
    [InlineData("new CustomerId(Guid.NewGuid().ToString(\"N\"))")]
    [InlineData("new GuidId(Guid.NewGuid())")]
    [InlineData("new Trellis.Identifier(Guid.NewGuid().ToString(\"N\"))")]
    [InlineData("new Foreign.Error.Unexpected(Guid.NewGuid().ToString(\"N\"))")]
    [InlineData("new Foreign.Coded { Code = Guid.NewGuid().ToString(\"N\") }")]
    [InlineData("Foreign.Codes.WithErrorCode(Guid.NewGuid().ToString(\"N\"))")]
    [InlineData("FluentValidation.TestHelper.Assertions.WithErrorCode(Guid.NewGuid().ToString(\"N\"))")]
    public async Task Analyze_NonReasonCodeSlot_FreshGuid_IsSilent(string expression) =>
        await VerifyBodyAsync("_ = " + expression + ";");

    [Theory]
    [InlineData("CustomerId id = new(Guid.NewGuid().ToString(\"N\"));")]
    [InlineData("GuidId id = new(Guid.NewGuid());")]
    [InlineData("Trellis.Identifier id = new(Guid.NewGuid().ToString(\"N\"));")]
    [InlineData("Error.Unexpected error = new(Code: \"order.failed\", FaultId: Guid.NewGuid().ToString(\"N\"));")]
    public async Task Analyze_TargetTypedNonReasonCode_FreshGuid_IsSilent(string statement) =>
        await VerifyBodyAsync(statement);

    [Theory]
    [InlineData("\"order.failed\"")]
    [InlineData("FaultCodes.UnhandledException")]
    [InlineData("nameof(Scenario)")]
    [InlineData("ConfiguredCode")]
    [InlineData("Lookup(Guid.NewGuid())")]
    [InlineData("Guid.Empty.ToString(\"N\")")]
    [InlineData("Guid.Parse(\"550e8400-e29b-41d4-a716-446655440000\").ToString(\"N\")")]
    [InlineData("false ? Guid.NewGuid().ToString(\"N\") : \"order.failed\"")]
    [InlineData("true ? \"order.failed\" : Guid.NewGuid().ToString(\"N\")")]
    [InlineData("\"order.failed\" ?? Guid.NewGuid().ToString(\"N\")")]
    [InlineData("string.Concat(false ? new[] { Guid.NewGuid().ToString(\"N\") } : new[] { \"order.failed\" })")]
    public async Task Analyze_StableOrOpaqueReasonCode_IsSilent(string expression) =>
        await VerifyBodyAsync("_ = new Error.Unexpected(" + expression + ");");

    [Theory]
    [InlineData("Convert.ToString(new[] { Guid.NewGuid() })!")]
    [InlineData("Convert.ToString(new[] { Guid.NewGuid().ToString(\"N\") })!")]
    [InlineData("((object)new[] { Guid.NewGuid() }).ToString()!")]
    [InlineData("new[] { Guid.NewGuid() }.ToString()!")]
    [InlineData("string.Concat((object)new[] { Guid.NewGuid() })")]
    [InlineData("string.Concat(new object[] { new[] { Guid.NewGuid() } })")]
    [InlineData("string.Concat<Guid[]>(new[] { new[] { Guid.NewGuid() } })")]
    [InlineData("$\"fault-{new[] { Guid.NewGuid() }}\"")]
    [InlineData("\"fault-\" + new[] { Guid.NewGuid() }")]
    public async Task Analyze_WholeObjectStringification_ArrayWithFreshGuid_IsSilent(string expression) =>
        await VerifyBodyAsync("_ = new Error.Unexpected(" + expression + ");");

    [Fact]
    public void Stringification_WholeArray_ReturnsTypeNameInsteadOfFreshValues()
    {
        var values = new[] { Guid.NewGuid() };

        Convert.ToString(values, CultureInfo.InvariantCulture).Should().Be("System.Guid[]");
        string.Concat((object)values).Should().Be("System.Guid[]");
        string.Concat(new object[] { values }).Should().Be("System.Guid[]");
        string.Concat<Guid[]>(new[] { values }).Should().Be("System.Guid[]");
    }

    [Fact]
    public async Task Analyze_CollectionSpread_UserDefinedElementConversion_IsSilent() =>
        await VerifySourceAsync("""
            public readonly struct StableCode
            {
                public static implicit operator StableCode(Guid value) => default;
                public override string ToString() => "order.failed";
            }

            public static class Scenario
            {
                public static void Emit()
                {
                    _ = new Error.Unexpected(string.Concat<StableCode>(
                        (System.Collections.Generic.IEnumerable<StableCode>)[.. new[] { Guid.NewGuid() }]));
                }
            }
            """);

    [Theory]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); code = \"order.failed\"; _ = new Error.Unexpected(code);")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); Replace(ref code); _ = new Error.Unexpected(code);")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); Action replace = () => code = \"order.failed\"; replace(); _ = new Error.Unexpected(code);")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); (code, _) = (\"order.failed\", 0); _ = new Error.Unexpected(code);")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); ref var alias = ref code; alias = \"order.failed\"; _ = new Error.Unexpected(code);")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); var other = \"order.failed\"; ref var alias = ref other; alias = ref code; alias = \"order.failed\"; _ = new Error.Unexpected(code);")]
    [InlineData("var parts = new[] { Guid.NewGuid().ToString(\"N\") }; parts[0] = \"order.failed\"; _ = new Error.Unexpected(string.Concat(parts));")]
    [InlineData("var parts = new[] { Guid.NewGuid().ToString(\"N\") }; Array.Clear(parts, 0, 1); _ = new Error.Unexpected(string.Concat(parts));")]
    [InlineData("object parts = new[] { Guid.NewGuid().ToString(\"N\") }; ((string[])parts)[0] = \"order.failed\"; _ = new Error.Unexpected(string.Concat(parts));")]
    [InlineData("unsafe { var id = Guid.NewGuid(); var pointer = &id; *pointer = Guid.Empty; _ = new Error.Unexpected(id.ToString(\"N\")); }")]
    [InlineData("var code = Guid.NewGuid().ToString(\"N\"); _ = new Error.Unexpected(Lookup(code));")]
    public async Task Analyze_MutableOrEscapedLocal_UnknownReasonCode_IsSilent(string statements) =>
        await VerifyBodyAsync(statements);

    [Fact]
    public async Task Analyze_ForwardedReasonCodeParameter_IsSilent() =>
        await VerifySourceAsync("""
            public static class Scenario
            {
                public static Error.Unexpected Create(string code) => new(code);
            }
            """);

    [Fact]
    public async Task Analyze_GuidNamesakeOutsideSystem_IsSilent() =>
        await VerifySourceAsync("""
            public static class Guid
            {
                public static string NewGuid() => "order.failed";
            }

            public static class Scenario
            {
                public static Error.Unexpected Create() => new(Guid.NewGuid().ToString());
            }
            """);

    [Fact]
    public async Task Analyze_StandardPragmaSuppression_IsHonored() =>
        await VerifyBodyAsync("""
            #pragma warning disable TRLS067
            _ = new Error.Unexpected(Guid.NewGuid().ToString("N"));
            #pragma warning restore TRLS067
            """);

    private static DiagnosticResult Expect(int location) =>
        new DiagnosticResult("TRLS067", DiagnosticSeverity.Warning).WithLocation(location);

    private static Task VerifyBodyAsync(string statements, params DiagnosticResult[] expected) =>
        VerifySourceAsync($$"""
            public static class Scenario
            {
                public static string ConfiguredCode => "order.failed";

                public static string Lookup(object value) => "order.failed";

                public static void Replace(ref string value) => value = "order.failed";

                public static void Emit()
                {
                    {{statements}}
                }
            }
            """, expected);

    private static async Task VerifySourceAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PerIncidentReasonCodeAnalyzer, DefaultVerifier>
        {
            TestCode = "using System;\nusing Trellis;\n" + source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };
        test.TestState.Sources.Add(("ReasonCodeGuardStubs.cs", Stubs));
        test.ExpectedDiagnostics.AddRange(expected);
        test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectCompilationOptions(
            projectId, ((CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!).WithAllowUnsafe(true)));

        await test.RunAsync();
    }

    internal const string Stubs = """
        namespace Trellis
        {
            public static class ValidationCodes
            {
                public const string Unspecified = "error.unspecified";
            }

            public static class FaultCodes
            {
                public const string UnhandledException = "unhandled-exception";
            }

            public abstract class Error
            {
                protected Error(string code = "error.unspecified") => Code = code;
                public string Code { get; init; }

                public sealed class Unexpected(string Code, string? FaultId = null) : Error(Code)
                {
                    public string? FaultId { get; } = FaultId;
                }

                public sealed class Conflict(string Code) : Error(Code)
                {
                    public static Conflict For<T>(string code, object? id = null) => new(code);
                    public static Conflict ForReason(string code) => new(code);
                }

                public sealed class InvalidInput : Error
                {
                    public static InvalidInput ForField(string code, string? field) => new() { Code = code };
                }

                public sealed class NotFound(object resource) : Error
                {
                    public object Resource { get; } = resource;
                }
            }

            public sealed record FieldViolation(string Field, string ReasonCode);
            public sealed record RuleViolation(string ReasonCode);
            public sealed record Identifier(string code);
        }

        public sealed record CustomerId(string Value);
        public sealed record GuidId(System.Guid Value);

        namespace FluentValidation
        {
            public static class Codes
            {
                public static object WithErrorCode(string errorCode) => errorCode;
            }
        }

        namespace FluentValidation.TestHelper
        {
            public static class Assertions
            {
                public static object WithErrorCode(string expectedErrorCode) => expectedErrorCode;
            }
        }

        namespace Foreign
        {
            public class MutableError : Trellis.Error
            {
                public new string Code { get; set; } = "order.failed";
            }

            public sealed class Coded
            {
                public string Code { get; set; } = "";
            }

            public static class Codes
            {
                public static object WithErrorCode(string errorCode) => errorCode;
            }

            public class Error
            {
                public sealed record Unexpected(string Code);
            }
        }
        """;
}