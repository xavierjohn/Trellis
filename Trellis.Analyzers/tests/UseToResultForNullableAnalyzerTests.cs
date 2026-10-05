namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="UseToResultForNullableAnalyzer"/> and <see cref="UseToResultForNullableCodeFixProvider"/> (TRLS066).
/// </summary>
public class UseToResultForNullableAnalyzerTests
{
    private const string Header = """
        #nullable enable
        using System;
        using Trellis;

        public record Cmd(string Title, DateTime Due, string? Tag);

        public class Sample
        {
            private static Error Missing(string field) => new(field);
            private string? Name { get; set; }

        """;

    private static string Wrap(string members) => Header + members + "\n}\n";

    private static CSharpAnalyzerTest<UseToResultForNullableAnalyzer, DefaultVerifier> AnalyzerTest(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<UseToResultForNullableAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", UseToResultForNullableTestStubs.Source));
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static CSharpCodeFixTest<UseToResultForNullableAnalyzer, UseToResultForNullableCodeFixProvider, DefaultVerifier> FixTest(
        string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<UseToResultForNullableAnalyzer, UseToResultForNullableCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", UseToResultForNullableTestStubs.Source));
        test.FixedState.Sources.Add(("Stubs.cs", UseToResultForNullableTestStubs.Source));
        return test;
    }

    private static DiagnosticResult Expect(int location) =>
        new DiagnosticResult(DiagnosticDescriptors.UseToResultForNullable).WithLocation(location);

    [Theory]
    [InlineData("title is not null")]
    [InlineData("title != null")]
    [InlineData("null != title")]
    [InlineData("title is { }")]
    [InlineData("(title is not null)")]
    public async Task EnsureOfNullCheck_OnNullableReference_Reports(string condition)
    {
        var source = Wrap($$"""
            public Result<Unit> Check(string? title) => {|#0:Result.Ensure({{condition}}, Missing("title"))|};
            """);

        await AnalyzerTest(source, Expect(0)).RunAsync();
    }

    [Fact]
    public async Task EnsureOfNullCheck_OnNullableValueType_Reports()
    {
        var source = Wrap("""
            public Result<Unit> Check(DateTime? due) => {|#0:Result.Ensure(due is not null, Missing("due"))|};
            """);

        await AnalyzerTest(source, Expect(0)).RunAsync();
    }

    [Fact]
    public async Task EnsureOfNullCheck_WithErrorFactory_Reports()
    {
        var source = Wrap("""
            public Result<Unit> Check(string? title) => {|#0:Result.Ensure(title is not null, () => Missing("title"))|};
            """);

        await AnalyzerTest(source, Expect(0)).RunAsync();
    }

    [Fact]
    public async Task EnsureOfNullCheck_OnProperty_Reports()
    {
        var source = Wrap("""
            public Result<Unit> Check() => {|#0:Result.Ensure(Name is not null, Missing("name"))|};
            """);

        await AnalyzerTest(source, Expect(0)).RunAsync();
    }

    [Theory]
    [InlineData("title is null")]
    [InlineData("title == null")]
    [InlineData("title is not null && title.Length > 0")]
    [InlineData("title is { Length: > 0 }")]
    [InlineData("title is { } t")]
    [InlineData("title is string")]
    public async Task EnsureOfOtherCondition_DoesNotReport(string condition)
    {
        var source = Wrap($$"""
            public Result<Unit> Check(string? title) => Result.Ensure({{condition}}, Missing("title"));
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Theory]
    [InlineData("value is string { }")]
    [InlineData("value is int { }")]
    public async Task EnsureOfTypedRecursivePattern_DoesNotReport(string condition)
    {
        var source = Wrap($$"""
            public Result<int> Check(object? value, string? tag) =>
                Result.Ensure({{condition}}, Missing("value"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Theory]
    [InlineData("value != (string?)null")]
    [InlineData("(string?)null != value")]
    public async Task EnsureOfNullComparison_WithImplicitUserDefinedConversion_DoesNotReport(string condition)
    {
        var source = Wrap($$"""
            public sealed class Token
            {
                public static implicit operator string?(Token? value) => null;
            }

            public Result<int> Check(Token? value, string? tag) =>
                Result.Ensure({{condition}}, Missing("value"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task EnsureWithNamedArguments_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(flag: title is not null, error: Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnsureWithDirectiveSelectedCondition_ReportsOnlyPureNullCheckWithoutOfferingAFix(bool useDebug)
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title
            #if DEBUG
                    is not null
            #else
                    is { Length: > 0 }
            #endif
                    , Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, source);
        string[] symbols = useDebug ? ["DEBUG"] : [];
        test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectParseOptions(
            projectId, CSharpParseOptions.Default.WithPreprocessorSymbols(symbols)));
        if (useDebug)
            test.ExpectedDiagnostics.Add(Expect(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task FixAll_SkipsDirectiveBearingInvocation_AndFixesSafeOperand()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title
            #if DEBUG
                    is not null
            #else
                    is { Length: > 0 }
            #endif
                    , Missing("title"))|}
                    .Combine({|#1:Result.Ensure(tag is not null, Missing("tag"))|})
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title
            #if DEBUG
                    is not null
            #else
                    is { Length: > 0 }
            #endif
                    , Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectParseOptions(
            projectId, CSharpParseOptions.Default.WithPreprocessorSymbols("DEBUG")));
        test.ExpectedDiagnostics.Add(Expect(0));
        test.ExpectedDiagnostics.Add(Expect(1));
        test.FixedState.ExpectedDiagnostics.Add(Expect(0));
        test.NumberOfIncrementalIterations = 1;
        test.NumberOfFixAllIterations = 1;
        await test.RunAsync();
    }

    [Fact]
    public async Task EnsureWithConditionalError_DoesNotReport()
    {
        var source = Wrap("""
            public Result<Unit> Check(string? title, bool strict) =>
                Result.Ensure(title is not null, strict ? Missing("strict") : Missing("title"));
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task EnsureWithCastConditionalError_DoesNotReport()
    {
        var source = Wrap("""
            public Result<Unit> Check(string? title, bool strict) =>
                Result.Ensure(title is not null, (Error)(strict ? Missing("strict") : Missing("title")));
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task EnsureWithSwitchError_DoesNotReport()
    {
        var source = Wrap("""
            public Result<Unit> Check(string? title, bool strict) =>
                Result.Ensure(title is not null, strict switch
                {
                    true => Missing("strict"),
                    false => Missing("title"),
                });
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task EnsureWithCoalescingError_DoesNotReport()
    {
        var source = Wrap("""
            public Result<Unit> Check(string? title, Error? selected) =>
                Result.Ensure(title is not null, selected ?? Missing("title"));
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task EnsureOfNullCheck_OnNonNullableReference_DoesNotReport()
    {
        var source = Wrap("""
            public Result<Unit> Check(string title) => Result.Ensure(title is not null, Missing("title"));
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task EnsureOfNullCheck_WithUserDefinedNotEquals_DoesNotReport()
    {
        var source = Wrap("""
            public Result<Unit> Check(Tracked? tracked) => Result.Ensure(tracked != null, Missing("tracked"));

            public sealed class Tracked
            {
                public static bool operator !=(Tracked? left, Tracked? right) => false;
                public static bool operator ==(Tracked? left, Tracked? right) => true;
                public override bool Equals(object? obj) => true;
                public override int GetHashCode() => 0;
            }
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task ToResult_DoesNotReport()
    {
        var source = Wrap("""
            public Result<string> Check(string? title) => title.ToResult(Missing("title"));
            """);

        await AnalyzerTest(source).RunAsync();
    }

    [Fact]
    public async Task CombineOfNullChecks_FixesBothOperands()
    {
        var source = Wrap("""
            public Result<Cmd> Create(string? title, DateTime? due, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine({|#1:Result.Ensure(due is not null, Missing("due"))|})
                    .Map((_, _) => new Cmd(title!, due!.Value, tag));
            """);
        var fixedSource = Wrap("""
            public Result<Cmd> Create(string? title, DateTime? due, string? tag) =>
                title.ToResult(Missing("title"))
                    .Combine(due.ToResult(Missing("due")))
                    .Map((_, _) => new Cmd(title!, due!.Value, tag));
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        test.ExpectedDiagnostics.Add(Expect(1));
        test.NumberOfIncrementalIterations = 2;
        test.NumberOfFixAllIterations = 1;
        await test.RunAsync();
    }

    [Fact]
    public async Task StaticCombineOperands_AreFixed()
    {
        var source = Wrap("""
            public Result<int> Create(string? title, string? tag) =>
                Result.Combine({|#0:Result.Ensure(title != null, Missing("title"))|}, {|#1:Result.Ensure(tag is { }, Missing("tag"))|})
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Create(string? title, string? tag) =>
                Result.Combine(title.ToResult(Missing("title")), tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        test.ExpectedDiagnostics.Add(Expect(1));
        test.NumberOfIncrementalIterations = 2;
        test.NumberOfFixAllIterations = 1;
        await test.RunAsync();
    }

    [Fact]
    public async Task CombineOperand_WithLowPrecedenceReceiver_IsParenthesized()
    {
        var source = Wrap("""
            public Result<int> Create(string? a, string? b, string? c) =>
                {|#0:Result.Ensure((a ?? b) is not null, Missing("name"))|}
                    .Combine(c.ToResult(Missing("c")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Create(string? a, string? b, string? c) =>
                (a ?? b).ToResult(Missing("name"))
                    .Combine(c.ToResult(Missing("c")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task StandaloneEnsure_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<Unit> Check(string? title) => {|#0:Result.Ensure(title is not null, Missing("title"))|};
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ConstantNullOperand_RewritesTheCheckedValueNotTheSentinel()
    {
        var source = Wrap("""
            private const string? Nil = null;

            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title != Nil, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            private const string? Nil = null;

            public Result<int> Check(string? title, string? tag) =>
                title.ToResult(Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorFactoryOverload_IsRewritten()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, () => Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title.ToResult(() => Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("(unit, _) => 1")]
    [InlineData("(Unit _, string _) => 1")]
    [InlineData("tuple => 1")]
    public async Task CombineOperand_WhoseSlotIsConsumed_ReportsWithoutOfferingAFix(string lambda)
    {
        var source = Wrap($$"""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map({{lambda}});
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task CombineOperand_WithWholeTupleDiscard_IsRewritten()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map(_ => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title.ToResult(Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map(_ => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Map((pair, _) => pair.Item2.ToString())")]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Combine(other.ToResult(Missing(\"other\"))).Map((pair, _, _) => pair.Item2.ToString())")]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Combine(t2: other.ToResult(Missing(\"other\"))).Map((pair, _) => pair.Item1.Item2.ToString())")]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Bind((pair, _) => pair.Item2.ToString().ToResult(Missing(\"value\")))")]
    public async Task CombineOperand_NestedTuple_IsConsumed_ReportsWithoutOfferingAFix(string pipeline)
    {
        var source = Wrap($$"""
            public Result<string> Check(string? first, string? title, string? other) =>
                first.ToResult(Missing("first"))
                    .Combine({|#0:Result.Ensure(title is not null, Missing("title"))|}){{pipeline}};
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Map((_, value) => value.Length)")]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Combine(other.ToResult(Missing(\"other\"))).Map((_, value, _) => value.Length)")]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Combine(t2: other.ToResult(Missing(\"other\"))).Map((_, value) => value.Length)")]
    [InlineData(".Combine(t2: other.ToResult(Missing(\"other\"))).Bind((_, value) => ((int?)value.Length).ToResult(Missing(\"length\")))")]
    public async Task CombineOperand_NestedTuple_IsDiscarded_IsRewritten(string pipeline)
    {
        var source = Wrap($$"""
            public Result<int> Check(string? first, string? title, string? other) =>
                first.ToResult(Missing("first"))
                    .Combine({|#0:Result.Ensure(title is not null, Missing("title"))|}){{pipeline}};
            """);
        var fixedSource = Wrap($$"""
            public Result<int> Check(string? first, string? title, string? other) =>
                first.ToResult(Missing("first"))
                    .Combine(title.ToResult(Missing("title"))){{pipeline}};
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("(int, int)? value", ".Combine(tag.ToResult(Missing(\"tag\"))).Map((_, tag) => tag.Length)")]
    [InlineData("string? value", ".Combine<Unit, string>(tag.ToResult(Missing(\"tag\"))).Map((_, tag) => tag.Length)")]
    [InlineData("string? value", ".Combine(tag.ToResult(Missing(\"tag\"))).Map<Unit, string, int>((_, tag) => tag.Length)")]
    [InlineData("string? value", ".Combine(tag.ToResult(Missing(\"tag\"))).Bind<Unit, string, int>((_, tag) => ((int?)tag.Length).ToResult(Missing(\"length\")))")]
    public async Task CombineOperand_WhoseRewrittenPipelineCannotBind_ReportsWithoutOfferingAFix(string parameter, string pipeline)
    {
        var source = Wrap($$"""
            public Result<int> Check({{parameter}}, string? tag) =>
                {|#0:Result.Ensure(value is not null, Missing("value"))|}{{pipeline}};
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task CombineOperand_WhoseRewriteSelectsApplicationCombine_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(string? value, string? tag) =>
                {|#0:Result.Ensure(value is not null, Missing("value"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """) + """

            public static class AppExtensions
            {
                public static Result<(string, string)> Combine(this Result<string> result, Result<string> other) => default;
            }
            """;

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task CombineOperand_WhoseRewriteSelectsApplicationMap_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(string? value, string? tag) =>
                {|#0:Result.Ensure(value is not null, Missing("value"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """) + """

            public static class AppExtensions
            {
                public static Result<int> Map(this Result<(string, string)> result, Func<string, string, int> map) => default;
            }
            """;

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task FixAll_RechecksPipelineBindingAfterEachReplacement()
    {
        const string applicationExtension = """

            public static class AppExtensions
            {
                public static Result<int> Map(this Result<(string, string)> result, Func<string, string, int> map) => default;
            }
            """;
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine({|#1:Result.Ensure(tag is not null, Missing("tag"))|})
                    .Map((_, _) => 1);
            """) + applicationExtension;
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title.ToResult(Missing("title"))
                    .Combine({|#1:Result.Ensure(tag is not null, Missing("tag"))|})
                    .Map((_, _) => 1);
            """) + applicationExtension;

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        test.ExpectedDiagnostics.Add(Expect(1));
        test.FixedState.ExpectedDiagnostics.Add(Expect(1));
        test.NumberOfIncrementalIterations = 1;
        test.NumberOfFixAllIterations = 1;
        await test.RunAsync();
    }

    [Fact]
    public async Task CombineOperand_WithDiscardingBind_IsRewritten()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Bind((_, tag) => ((int?)tag.Length).ToResult(Missing("length")));
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title.ToResult(Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Bind((_, tag) => ((int?)tag.Length).ToResult(Missing("length")));
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task CombineResult_NotPassedToALambda_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<(Unit, string)> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")));
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Theory]
    [InlineData("DateTime? value")]
    [InlineData("Guid? value")]
    [InlineData("int? value")]
    public async Task NotEqualsNull_OnNullableValueType_Reports(string parameter)
    {
        var source = Wrap($$"""
            public Result<Unit> Check({{parameter}}) => {|#0:Result.Ensure(value != null, Missing("value"))|};
            """);

        await AnalyzerTest(source, Expect(0)).RunAsync();
    }

    [Fact]
    public async Task LoneUnderscoreParameter_ThatIsRead_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map(_ => _.Item1.GetHashCode());
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task TuplePreservingConsumer_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<(Unit, string)> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Tap((_, _) => { });
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ExtensionCombineCalledStatically_WithDiscardedThirdSlot_IsRewritten()
    {
        var source = Wrap("""
            public Result<int> Check(Result<(string, string)> pair, string? title) =>
                CombineExtensions.Combine(pair, {|#0:Result.Ensure(title is not null, Missing("title"))|})
                    .Map((x, y, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(Result<(string, string)> pair, string? title) =>
                CombineExtensions.Combine(pair, title.ToResult(Missing("title")))
                    .Map((x, y, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ExtensionCombineCalledStatically_WithConsumedThirdSlot_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(Result<(string, string)> pair, string? title) =>
                CombineExtensions.Combine(pair, {|#0:Result.Ensure(title is not null, Missing("title"))|})
                    .Map((_, _, z) => 1);
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ReorderedNamedCombineArguments_UseTheParameterSlot()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                Result.Combine(r2: {|#0:Result.Ensure(title is not null, Missing("title"))|}, r1: tag.ToResult(Missing("tag")))
                    .Map((_, second) => 1);
            """);

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ApplicationExtensionConsumer_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Consume((_, _) => 1);
            """) + """

            public static class AppExtensions
            {
                public static Result<int> Consume(this Result<(Unit, string)> result, Func<Unit, string, int> map) => default;
            }
            """;

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task ApplicationToResultInstanceMethod_ReportsWithoutOfferingAFix()
    {
        var source = Wrap("""
            public Result<int> Check(Tag? tag, string? other) =>
                {|#0:Result.Ensure(tag is not null, Missing("tag"))|}
                    .Combine(other.ToResult(Missing("other")))
                    .Map((_, _) => 1);
            """) + """

            public sealed class Tag
            {
                public Result<Tag> ToResult(Error error) => default;
            }
            """;

        var test = FixTest(source, source);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_PreservesCommentsOnTheCheckedValueAndTheError()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(/* required */ title is not null, /* why */ Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                /* required */ title.ToResult(/* why */ Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_PreservesSingleLineCommentAfterCheckedValue()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title // checked field
                    is not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title // checked field
                    .ToResult(Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_PreservesCommentInsideNullPattern()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is /* pure null check */ not null, Missing("title"))|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title /* pure null check */.ToResult(Missing("title"))
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task Fix_PreservesSingleLineCommentAfterError()
    {
        var source = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                {|#0:Result.Ensure(title is not null, Missing("title") // chosen error
                )|}
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);
        var fixedSource = Wrap("""
            public Result<int> Check(string? title, string? tag) =>
                title.ToResult(Missing("title") // chosen error
                )
                    .Combine(tag.ToResult(Missing("tag")))
                    .Map((_, _) => 1);
            """);

        var test = FixTest(source, fixedSource);
        test.ExpectedDiagnostics.Add(Expect(0));
        await test.RunAsync();
    }
}
