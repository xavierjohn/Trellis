namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for UseFunctionalValueOrDefaultCodeFixProvider (TRLS013).
/// Verifies that ternary operators are correctly replaced with GetValueOrDefault() or Match().
/// </summary>
public class UseFunctionalValueOrDefaultCodeFixProviderTests
{
    [Fact]
    public async Task TernaryWithNull_ReplacedWithGetValueOrDefault_CodeAction0()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var value = result.IsSuccess ? result.Value : null;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var value = result.GetValueOrDefault();
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithDefault_ReplacedWithGetValueOrDefault_CodeAction0()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.IsSuccess ? result.Value : default;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.GetValueOrDefault();
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithNumericZero_ReplacedWithGetValueOrDefault()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.IsSuccess ? result.Value : 0;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.GetValueOrDefault();
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithFalse_ReplacedWithGetValueOrDefault()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<bool> result)
                {
                    var value = result.IsSuccess ? result.Value : false;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<bool> result)
                {
                    var value = result.GetValueOrDefault();
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithNull_ReplacedWithMatch_CodeAction1()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var value = result.IsSuccess ? result.Value : null;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var value = result.Match(value => value, _ => null);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 1,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithCustomDefaultValue_ReplacedWithGetValueOrDefault()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.IsSuccess ? result.Value : 42;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.GetValueOrDefault(42);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithStringDefaultValue_ReplacedWithGetValueOrDefault()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var value = result.IsSuccess ? result.Value : "fallback";
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var value = result.GetValueOrDefault("fallback");
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithCustomDefaultValue_ReplacedWithMatch_CodeAction1()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.IsSuccess ? result.Value : 42;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Match(value => value, _ => 42);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 1,  // Match is code action index 1 when GetValueOrDefault is also offered
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(11, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithComments_PreservesTrivia()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    // Extract value
                    var value = result.IsSuccess ? result.Value : null; // With fallback
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    // Extract value
                    var value = result.GetValueOrDefault(); // With fallback
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(12, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithOuterErrorVariable_ReplacedWithMatch_WithoutShadowing()
    {
        const string source = """
            public class TestClass
            {
                public object TestMethod(Result<int> result)
                {
                    object error = "outer error";
                    return result.IsSuccess ? result.Value : error;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public object TestMethod(Result<int> result)
                {
                    object error = "outer error";
                    return result.Match(value => value, _ => error);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<TernaryValueOrDefaultAnalyzer, UseFunctionalValueOrDefaultCodeFixProvider>(
            source,
            fixedSource,
            codeActionIndex: 0,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault).WithLocation(12, 20));

        await test.RunAsync();
    }
}