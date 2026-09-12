namespace Trellis.Analyzers.Tests;

using System.Text.RegularExpressions;
using Xunit;

public class ApiReferenceExampleTests
{
    [Fact]
    public async Task LinqProjection_PublishedFix_BothAnalyzers_NoDiagnostics()
    {
        var example = ReadFirstCSharpBlock("trellis-api-anti-patterns.md",
            "## TRLS013 — Unsafe `Maybe<T>.Value` in LINQ projection");
        var fixStart = example.IndexOf("// FIX 1", StringComparison.Ordinal);
        fixStart.Should().BeGreaterThanOrEqualTo(0);

        var source = WrapProjection(example[fixStart..]);
        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer, UnsafeValueAccessAnalyzer>(source);

        await test.RunAsync();
    }

    [Fact]
    public async Task LinqProjection_UnfilteredValue_BothAnalyzers_ReportDiagnostics()
    {
        var source = WrapProjection("IEnumerable<int> numbers = values.Select(m => m.{|#0:Value|});");
        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer, UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueInLinq)
                .WithArguments("Maybe.Value", "HasValue").WithLocation(0),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task LinqProjection_FilteredValue_BothAnalyzers_StillReportsUnsafeAccess()
    {
        var source = WrapProjection(
            "IEnumerable<int> numbers = values.Where(m => m.HasValue).Select(m => m.{|#0:Value|});");
        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer, UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task Suppression_PublishedExample_WithoutAnalyzerCompileReference_NoDiagnostics()
    {
        var example = ReadFirstCSharpBlock("trellis-api-analyzers.md", "## Constants — `TrellisDiagnosticIds`");
        var source = $$"""
            using System.Diagnostics.CodeAnalysis;

            public class Address
            {
                public string City { get; set; } = "";
            }

            public class TestClass
            {
                {{example}}
            }
            """;
        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);

        await test.RunAsync();
    }

    private static string WrapProjection(string statements) =>
        $$"""
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public IEnumerable<int> Project(IEnumerable<Maybe<int>> values)
                {
                    {{statements}}
                    return numbers;
                }
            }
            """;

    private static string ReadFirstCSharpBlock(string fileName, string heading)
    {
        var document = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ApiReference", fileName));
        var sectionStart = document.IndexOf(heading, StringComparison.Ordinal);
        sectionStart.Should().BeGreaterThanOrEqualTo(0);
        var match = Regex.Match(document[sectionStart..], @"```csharp\r?\n(?<code>[\s\S]*?)```");
        match.Success.Should().BeTrue();
        return match.Groups["code"].Value.Trim();
    }
}
