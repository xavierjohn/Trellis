namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="CombineLimitAnalyzer"/> (TRLS014).
/// Verifies that Combine chains exceeding 9 elements produce a diagnostic.
/// </summary>
public class CombineLimitAnalyzerTests
{
    [Fact]
    public async Task Combine_9Elements_NoDiagnostic()
    {
        // 9 elements is the maximum supported — no diagnostic
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> r1 = default;
                    Result<int> r2 = default;
                    Result<int> r3 = default;
                    Result<int> r4 = default;
                    Result<int> r5 = default;
                    Result<int> r6 = default;
                    Result<int> r7 = default;
                    Result<int> r8 = default;
                    Result<int> r9 = default;

                    var result = r1
                        .Combine(r2)
                        .Combine(r3)
                        .Combine(r4)
                        .Combine(r5)
                        .Combine(r6)
                        .Combine(r7)
                        .Combine(r8)
                        .Combine(r9);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CombineLimitAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Combine_10Elements_ReportsDiagnostic()
    {
        // 10th Combine exceeds the limit — the code won't compile (no overload),
        // but the analyzer should provide a helpful message explaining why.
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> r1 = default;
                    Result<int> r2 = default;
                    Result<int> r3 = default;
                    Result<int> r4 = default;
                    Result<int> r5 = default;
                    Result<int> r6 = default;
                    Result<int> r7 = default;
                    Result<int> r8 = default;
                    Result<int> r9 = default;
                    Result<int> r10 = default;

                    var result = r1
                        .Combine(r2)
                        .Combine(r3)
                        .Combine(r4)
                        .Combine(r5)
                        .Combine(r6)
                        .Combine(r7)
                        .Combine(r8)
                        .Combine(r9)
                        .{|#0:Combine|}(r10);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CombineLimitAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CombineChainTooLong)
                .WithLocation(0)
                .WithArguments(10));

        await test.RunAsync();
    }

    [Fact]
    public async Task Combine_2Elements_NoDiagnostic()
    {
        // Simple 2-element combine — well within limit
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<string> r1 = default;
                    Result<int> r2 = default;

                    var result = r1.Combine(r2);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CombineLimitAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Combine_5Elements_NoDiagnostic()
    {
        // 5-element chain — within limit
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<string> r1 = default;
                    Result<int> r2 = default;
                    Result<bool> r3 = default;
                    Result<double> r4 = default;
                    Result<long> r5 = default;

                    var result = r1
                        .Combine(r2)
                        .Combine(r3)
                        .Combine(r4)
                        .Combine(r5);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CombineLimitAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NonCombineMethod_NoDiagnostic()
    {
        // Calls to non-Combine methods should not trigger
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<string> r1 = default;
                    var result = r1.Map(x => x.Length);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<CombineLimitAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Combine_10ElementsAcrossIntermediateVariable_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> r1 = default;
                    Result<int> r2 = default;
                    Result<int> r3 = default;
                    Result<int> r4 = default;
                    Result<int> r5 = default;
                    Result<int> r6 = default;
                    Result<int> r7 = default;
                    Result<int> r8 = default;
                    Result<int> r9 = default;
                    Result<int> r10 = default;

                    var temp = r1
                        .Combine(r2)
                        .Combine(r3)
                        .Combine(r4)
                        .Combine(r5);

                    var result = temp
                        .Combine(r6)
                        .Combine(r7)
                        .Combine(r8)
                        .Combine(r9)
                        .{|#0:Combine|}(r10);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<CombineLimitAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.CombineChainTooLong)
                .WithLocation(0)
                .WithArguments(10));

        await test.RunAsync();
    }
}