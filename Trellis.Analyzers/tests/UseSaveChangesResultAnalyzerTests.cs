namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for UseSaveChangesResultAnalyzer (TRLS015).
/// Verifies that direct SaveChangesAsync/SaveChanges calls on DbContext produce a warning.
/// </summary>
public class UseSaveChangesResultAnalyzerTests
{
    #region SaveChangesAsync produces warning

    [Fact]
    public async Task SaveChangesAsync_OnDbContext_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
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

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseSaveChangesResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(18, 26)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WithAcceptAllChangesOnSuccess_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
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

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseSaveChangesResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(18, 26)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region SaveChanges (sync) produces warning

    [Fact]
    public async Task SaveChanges_Sync_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;

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

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseSaveChangesResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(16, 20)
                .WithArguments("SaveChanges"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region SaveChangesResultUnitAsync does not produce warning

    [Fact]
    public async Task SaveChangesResultUnitAsync_NoDiagnostic()
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
                    await _dbContext.SaveChangesResultUnitAsync(ct);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseSaveChangesResultAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region SaveChangesResultAsync does not produce warning

    [Fact]
    public async Task SaveChangesResultAsync_NoDiagnostic()
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
                    await _dbContext.SaveChangesResultAsync(ct);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseSaveChangesResultAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region Non-DbContext SaveChangesAsync does not produce warning

    [Fact]
    public async Task NonDbContext_SaveChangesAsync_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            public class MyRepository
            {
                public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
            }

            public class TestService
            {
                private readonly MyRepository _repo;
                public TestService(MyRepository repo) => _repo = repo;

                public async Task DoWork(CancellationToken ct)
                {
                    await _repo.SaveChangesAsync(ct);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseSaveChangesResultAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region DbContext subclass produces warning

    [Fact]
    public async Task SaveChangesAsync_OnDbContextSubclass_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public class AppDbContext : DbContext { }

            public class TestService
            {
                private readonly AppDbContext _dbContext;
                public TestService(AppDbContext dbContext) => _dbContext = dbContext;

                public async Task DoWork(CancellationToken ct)
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseSaveChangesResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(20, 26)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region Unqualified call from within DbContext subclass

    [Fact]
    public async Task SaveChangesAsync_UnqualifiedInSubclass_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
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

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseSaveChangesResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseSaveChangesResult)
                .WithLocation(15, 15)
                .WithArguments("SaveChangesAsync"));
        test.TestState.Sources.Add(("EfCoreStubs.cs", EfCoreTestStubs.Source));

        await test.RunAsync();
    }

    #endregion

    #region No warning without Trellis.EntityFrameworkCore reference

    [Fact]
    public async Task SaveChangesAsync_WithoutTrellisEfCore_NoDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
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

        // Only add DbContext stub, NOT the Trellis.EntityFrameworkCore stub
        const string dbContextOnlyStub = """
            namespace Microsoft.EntityFrameworkCore
            {
                using System.Threading;
                using System.Threading.Tasks;

                public class DbContext
                {
                    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                        => Task.FromResult(0);

                    public virtual int SaveChanges() => 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseSaveChangesResultAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreStubs.cs", dbContextOnlyStub));

        await test.RunAsync();
    }

    #endregion
}