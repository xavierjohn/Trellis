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
                    var value = maybe.{|#0:Value|};
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
                .WithLocation(0));

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
                    var value = maybe.{|#0:Value|};
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
                .WithLocation(0));

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
                    return maybe.{|#0:Value|};
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
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeValue_InReturnStatement_NonNullableReferenceType_OmitsSynthesizedReturn()
    {
        const string source = """
            #nullable enable
            public class TestClass
            {
                public string TestMethod(Maybe<string> maybe)
                {
                    return maybe.{|#0:Value|};
                }
            }
            """;

        // No 'return default;' is synthesized: default is null for a non-nullable reference
        // return type. CS0161 forces an explicit decision instead of a silent null escaping.
        const string fixedSource = """
            #nullable enable
            public class TestClass
            {
                public string {|CS0161:TestMethod|}(Maybe<string> maybe)
                {
                    if (maybe.HasValue)
                    {
                        return maybe.Value;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeValue_InReturnStatement_NullableReferenceType_KeepsSynthesizedReturn()
    {
        const string source = """
            #nullable enable
            public class TestClass
            {
                public string? TestMethod(Maybe<string> maybe)
                {
                    return maybe.{|#0:Value|};
                }
            }
            """;

        const string fixedSource = """
            #nullable enable
            public class TestClass
            {
                public string? TestMethod(Maybe<string> maybe)
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
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeValue_InReturnStatement_AsyncNonNullableReferenceType_OmitsSynthesizedReturn()
    {
        const string source = """
            #nullable enable
            public class TestClass
            {
                public async Task<string> TestMethod(Maybe<string> maybe)
                {
                    await Task.Delay(1);
                    return maybe.{|#0:Value|};
                }
            }
            """;

        // An async method returns default(string), not default(Task<string>), so the async
        // return type must be unwrapped before deciding whether default is usable.
        const string fixedSource = """
            #nullable enable
            public class TestClass
            {
                public async Task<string> {|CS0161:TestMethod|}(Maybe<string> maybe)
                {
                    await Task.Delay(1);
                    if (maybe.HasValue)
                    {
                        return maybe.Value;
                    }
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UnsafeValueAccessAnalyzer, AddResultGuardCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeValue_InReturnStatement_AsyncValueType_KeepsSynthesizedReturn()
    {
        const string source = """
            #nullable enable
            public class TestClass
            {
                public async Task<int> TestMethod(Maybe<int> maybe)
                {
                    await Task.Delay(1);
                    return maybe.{|#0:Value|};
                }
            }
            """;

        const string fixedSource = """
            #nullable enable
            public class TestClass
            {
                public async Task<int> TestMethod(Maybe<int> maybe)
                {
                    await Task.Delay(1);
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
                .WithLocation(0));

        await test.RunAsync();
    }

    #endregion
}