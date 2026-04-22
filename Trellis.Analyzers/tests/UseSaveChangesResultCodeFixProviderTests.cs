namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for UseSaveChangesResultCodeFixProvider (TRLS020).
/// Verifies that SaveChangesAsync/SaveChanges is correctly replaced with SaveChangesResultUnitAsync.
/// </summary>
public class UseSaveChangesResultCodeFixProviderTests
{
    [Fact]
    public async Task SaveChangesAsync_ReplacedWith_SaveChangesResultUnitAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesResultUnitAsync(ct);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 30)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithConfigureAwait_Standalone_ReplacedWith_SaveChangesResultUnitAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesResultUnitAsync(ct).ConfigureAwait(false);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 30)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithReturnValueUsed_ReplacedWith_SaveChangesResultAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    var count = await _dbContext.SaveChangesAsync(ct);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    var count = await _dbContext.SaveChangesResultAsync(ct);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 42)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithConfigureAwait_ReturnValueUsed_ReplacedWith_SaveChangesResultAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    var count = await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    var count = await _dbContext.SaveChangesResultAsync(ct).ConfigureAwait(false);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 42)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_Sync_ReplacedWith_SaveChangesResultUnitAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public void DoWork()
                {
                    _dbContext.SaveChanges();
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork()
                {
                    await _dbContext.SaveChangesResultUnitAsync();
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(17, 24)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_InAssignment_NoCodeFixOffered()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public void DoWork()
                {
                    var count = _dbContext.SaveChanges();
                }
            }
            """;

        // The code fix should not be offered, so fixedSource == source (no transformation)
        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(17, 36)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_InNonVoidMethod_NoCodeFixOffered()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public int DoWork()
                {
                    _dbContext.SaveChanges();
                    return 42;
                }
            }
            """;

        // The code fix should not be offered for sync SaveChanges in a non-void method
        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(17, 24)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_InReturnStatement_NoCodeFixOffered()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public int DoWork()
                {
                    return _dbContext.SaveChanges();
                }
            }
            """;

        // The code fix should not be offered — return statement is not a standalone ExpressionStatement
        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(17, 31)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_InReturnStatement_NoCodeFixOffered()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task<int> DoWork(CancellationToken ct)
                {
                    return await _dbContext.SaveChangesAsync(ct);
                }
            }
            """;

        // The code fix should not be offered — return type would need to change from Task<int> to Task<Result<int>>
        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 37)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithAcceptAllChangesOnSuccess_ReplacedWith_SaveChangesResultUnitAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesAsync(false, ct);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesResultUnitAsync(false, ct);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 30)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithAcceptAllChangesOnSuccess_ReturnValueUsed_ReplacedWith_SaveChangesResultAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    var count = await _dbContext.SaveChangesAsync(false, ct);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    var count = await _dbContext.SaveChangesResultAsync(false, ct);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(19, 42)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_WithAcceptAllChangesOnSuccess_ReplacedWith_SaveChangesResultUnitAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public void DoWork()
                {
                    _dbContext.SaveChanges(false);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork()
                {
                    await _dbContext.SaveChangesResultUnitAsync(false);
                }
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(17, 24)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_Sync_AddsUsingDirective_WhenMissing()
    {
        // Full source without using System.Threading.Tasks — bypasses CodeFixTestHelper.WrapInNamespace
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            namespace TestNamespace
            {
                public class TestService
                {
                    private readonly DbContext _dbContext;
                    public TestService(DbContext dbContext) => _dbContext = dbContext;

                    public void DoWork()
                    {
                        _dbContext.SaveChanges();
                    }
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public class TestService
                {
                    private readonly DbContext _dbContext;
                    public TestService(DbContext dbContext) => _dbContext = dbContext;

                    public async Task DoWork()
                    {
                        await _dbContext.SaveChangesResultUnitAsync();
                    }
                }
            }
            """;

        var test = new CSharpCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(13, 24)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));

        await test.RunAsync();
    }

    /// <summary>
    /// Minimal Trellis Result stubs for tests that bypass CodeFixTestHelper.
    /// Unit is already defined in EfCoreTestStubs.Source.
    /// </summary>
    private const string TrellisResultStubSource = """
        namespace Trellis
        {
            public readonly struct Result<T>
            {
                private readonly T _value;
                private Result(T value) { _value = value; }
                public bool IsFailure => false;
                public bool IsSuccess => true;
                public T Value => _value;
                public static implicit operator Result<T>(T value) => new Result<T>(value);
            }

            public static class Result
            {
                public static Result<T> Ok<T>(T value) => value;
            }
        }
        """;

    [Fact]
    public async Task SaveChangesAsync_AddsEfCoreUsing_WhenMissing()
    {
        // Source has no 'using Trellis.EntityFrameworkCore;' — the code fix should add it
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public class TestService
                {
                    private readonly DbContext _dbContext;
                    public TestService(DbContext dbContext) => _dbContext = dbContext;

                    public async Task DoWork(CancellationToken ct)
                    {
                        await _dbContext.SaveChangesAsync(ct);
                    }
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;
            using Trellis.EntityFrameworkCore;

            namespace TestNamespace
            {
                public class TestService
                {
                    private readonly DbContext _dbContext;
                    public TestService(DbContext dbContext) => _dbContext = dbContext;

                    public async Task DoWork(CancellationToken ct)
                    {
                        await _dbContext.SaveChangesResultUnitAsync(ct);
                    }
                }
            }
            """;

        var test = new CSharpCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(14, 30)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_UnqualifiedInSubclass_AddsThisPrefix()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class AppDbContext : DbContext
            {
                public async Task DoSomething(CancellationToken ct)
                {
                    await SaveChangesAsync(ct);
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class AppDbContext : DbContext
            {
                public async Task DoSomething(CancellationToken ct)
                {
                    await this.SaveChangesResultUnitAsync(ct);
                }
            }
            """;

        var test = new CSharpCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(10, 15)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_UnqualifiedInSubclass_AddsThisPrefixAndMakesAsync()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class AppDbContext : DbContext
            {
                public void DoSomething()
                {
                    SaveChanges();
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading.Tasks;

            public class AppDbContext : DbContext
            {
                public async Task DoSomething()
                {
                    await this.SaveChangesResultUnitAsync();
                }
            }
            """;

        var test = new CSharpCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(8, 9)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_InsideLocalFunction_NoCodeFixOffered()
    {
        // SaveChanges() inside a non-void local function — code fix should not be offered
        // because the local function returns int, not the outer method
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public void DoWork()
                {
                    int LocalSave()
                    {
                        _dbContext.SaveChanges();
                        return 42;
                    }
                    LocalSave();
                }
            }
            """;

        var test = new CSharpCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(13, 24)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChanges_InsideVoidLocalFunction_FixAppliedToLocalFunction()
    {
        // SaveChanges() inside a void local function — the fix should make the local function async, not the outer method
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public void DoWork()
                {
                    void LocalSave()
                    {
                        _dbContext.SaveChanges();
                    }
                    LocalSave();
                }
            }
            """;

        const string fixedSource = """
            using Microsoft.EntityFrameworkCore;
            using Trellis.EntityFrameworkCore;
            using System.Threading.Tasks;

            public class TestService
            {
                private readonly DbContext _dbContext;
                public TestService(DbContext dbContext) => _dbContext = dbContext;

                public void DoWork()
                {
                    async Task LocalSave()
                    {
                        await _dbContext.SaveChangesResultUnitAsync();
                    }
                    LocalSave();
                }
            }
            """;

        var test = new CSharpCodeFixTest<UseSaveChangesResultAnalyzer, UseSaveChangesResultCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(13, 24)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));
        test.FixedState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));
        test.FixedState.Sources.Add(("TrellisStubs.cs", TrellisResultStubSource));

        await test.RunAsync();
    }
}