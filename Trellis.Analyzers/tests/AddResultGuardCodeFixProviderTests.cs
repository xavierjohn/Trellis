namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="AddResultGuardCodeFixProvider"/> (TRLS003 — Maybe.Value).
/// The Result-side fixes for TRLS003 / TRLS004 were removed in v2 along with the analyzers
/// themselves: <c>Result&lt;T&gt;.Value</c> no longer exists, and <c>Result&lt;T&gt;.Error</c>
/// is nullable so NRT handles the unsafe access at the language level.
/// </summary>
public class AddResultGuardCodeFixProviderTests
{
    #region TRLS003 - Maybe.Value Access Tests

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
                .WithLocation(11, 31));

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
                .WithLocation(11, 31));

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
                .WithLocation(11, 26));

        await test.RunAsync();
    }

    #endregion
}
