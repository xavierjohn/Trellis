namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for AddResultGuardCodeFixProvider (TRLS003, TRLS004, TRLS006).
/// Verifies that unsafe Result.Value, Result.Error, and Maybe.Value access
/// is correctly wrapped with appropriate guard statements.
/// </summary>
public class AddResultGuardCodeFixProviderTests
{
    #region TRLS003 - Result.Value Access Tests

    [Fact]
    public async Task ResultValue_SingleStatement_AddsIsSuccessGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Value;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 32));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_MultipleConsecutiveStatements_WrapsAllInSingleGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Value;
                    var doubled = value * 2;
                    Console.WriteLine(doubled);
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                        var doubled = value * 2;
                        Console.WriteLine(doubled);
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 32));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_WithStatementAfterDerivedUsage_StopsAtUnrelatedStatement()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Value;
                    Console.WriteLine(value);
                    var unrelated = 42;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                        Console.WriteLine(value);
                    }

                    var unrelated = 42;
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 32));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_InReturnStatement_WrapsReturn()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return result.Value;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        return result.Value;
                    }

                    return default;
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 27));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_InNestedReturnBlock_DoesNotAddDefaultReturnInsideInnerBlock()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result, bool condition)
                {
                    if (condition)
                    {
                        return result.Value;
                    }

                    return 42;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public int TestMethod(Result<int> result, bool condition)
                {
                    if (condition)
                    {
                        if (result.IsSuccess)
                        {
                            return result.Value;
                        }
                    }

                    return 42;
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(13, 31));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_InMethodArgument_WrapsStatement()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    ProcessValue(result.Value);
                }

                private void ProcessValue(int value) { }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        ProcessValue(result.Value);
                    }
                }

                private void ProcessValue(int value) { }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 33)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_MultipleAccessesInSameStatement_WrapsOnce()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var sum = result.Value + result.Value;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var sum = result.Value + result.Value;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 30)
                ,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 45)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_NestedMemberAccess_AddsGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    var length = result.Value.Length;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<string> result)
                {
                    if (result.IsSuccess)
                    {
                        var length = result.Value.Length;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 33)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_WithLeadingTrivia_PreservesIndentation()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    // This is a comment
                    var value = result.Value;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    // This is a comment
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(12, 32)
                );

        await test.RunAsync();
    }

    #endregion

    #region TRLS004 - Result.Error Access Tests

    [Fact]
    public async Task ResultError_SingleStatement_AddsIsFailureGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var error = result.Error;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsFailure)
                    {
                        var error = result.Error;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultErrorAccess)
                .WithLocation(11, 32)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultError_MultipleStatements_WrapsAllInIsFailureGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var error = result.Error;
                    var message = error.Detail;
                    Console.WriteLine(message);
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsFailure)
                    {
                        var error = result.Error;
                        var message = error.Detail;
                        Console.WriteLine(message);
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultErrorAccess)
                .WithLocation(11, 32)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultError_InReturnStatement_WrapsReturn()
    {
        const string source = """
            public class TestClass
            {
                public Error TestMethod(Result<int> result)
                {
                    return result.Error;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public Error TestMethod(Result<int> result)
                {
                    if (result.IsFailure)
                    {
                        return result.Error;
                    }

                    return default;
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultErrorAccess)
                .WithLocation(11, 27)
                );

        await test.RunAsync();
    }

    #endregion

    #region TRLS006 - Maybe.Value Access Tests

    [Fact]
    public async Task MaybeValue_SingleStatement_AddsHasValueGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    var value = maybe.Value;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue)
                    {
                        var value = maybe.Value;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(11, 31)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeValue_MultipleStatements_WrapsAllInHasValueGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    var value = maybe.Value;
                    var doubled = value * 2;
                    Console.WriteLine(doubled);
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue)
                    {
                        var value = maybe.Value;
                        var doubled = value * 2;
                        Console.WriteLine(doubled);
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(11, 31)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeValue_InReturnStatement_WrapsReturn()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    return maybe.Value;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue)
                    {
                        return maybe.Value;
                    }

                    return default;
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(11, 26)
                );

        await test.RunAsync();
    }

    #endregion

    #region Variable Tracking Tests

    [Fact]
    public async Task ResultValue_TracksVariableDerivedFromValue_IncludesInGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Value;
                    var squared = value * value;
                    var message = $"Result: {squared}";
                    Console.WriteLine(message);
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                        var squared = value * value;
                        var message = $"Result: {squared}";
                        Console.WriteLine(message);
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 32)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_StopsAtStatementNotUsingTrackedVariables()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Value;
                    Console.WriteLine(value);
                    var independent = GetOtherValue();
                    Console.WriteLine(independent);
                }

                private int GetOtherValue() => 42;
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                        Console.WriteLine(value);
                    }

                    var independent = GetOtherValue();
                    Console.WriteLine(independent);
                }

                private int GetOtherValue() => 42;
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 32)
                );

        await test.RunAsync();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ResultValue_ComplexExpression_AddsGuard()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var calculated = (result.Value * 2) + 10;
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var calculated = (result.Value * 2) + 10;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 38)
                );

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultValue_WithExistingStatementsAfter_PreservesThose()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    Console.WriteLine("Before");
                    var value = result.Value;
                    Console.WriteLine("After");
                }
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    Console.WriteLine("Before");
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                    }

                    Console.WriteLine("After");
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(12, 32)
                );

        await test.RunAsync();
    }

    #endregion
}