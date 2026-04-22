namespace Trellis.Analyzers.Tests;

/// <summary>
/// Shared stub source for Entity Framework Core types used in TRLS015 analyzer and code fix tests.
/// </summary>
public static class EfCoreTestStubs
{
    /// <summary>
    /// Stub source providing DbContext, DbContextExtensions, and related Trellis types.
    /// </summary>
    public const string Source = """
        namespace Trellis
        {
            public record struct Unit;
        }

        namespace Microsoft.EntityFrameworkCore
        {
            using System.Threading;
            using System.Threading.Tasks;

            public class DbContext
            {
                public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                    => Task.FromResult(0);

                public virtual Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
                    => Task.FromResult(0);

                public virtual int SaveChanges() => 0;

                public virtual int SaveChanges(bool acceptAllChangesOnSuccess) => 0;
            }
        }

        namespace Trellis.EntityFrameworkCore
        {
            using Microsoft.EntityFrameworkCore;
            using System.Threading;
            using System.Threading.Tasks;

            public static class DbContextExtensions
            {
                public static Task<Trellis.Result<int>> SaveChangesResultAsync(
                    this DbContext context,
                    CancellationToken cancellationToken = default)
                    => Task.FromResult(Trellis.Result.Ok(0));

                public static Task<Trellis.Result<int>> SaveChangesResultAsync(
                    this DbContext context,
                    bool acceptAllChangesOnSuccess,
                    CancellationToken cancellationToken = default)
                    => Task.FromResult(Trellis.Result.Ok(0));

                public static Task<Trellis.Result<Trellis.Unit>> SaveChangesResultUnitAsync(
                    this DbContext context,
                    CancellationToken cancellationToken = default)
                    => Task.FromResult(Trellis.Result.Ok(default(Trellis.Unit)));

                public static Task<Trellis.Result<Trellis.Unit>> SaveChangesResultUnitAsync(
                    this DbContext context,
                    bool acceptAllChangesOnSuccess,
                    CancellationToken cancellationToken = default)
                    => Task.FromResult(Trellis.Result.Ok(default(Trellis.Unit)));
            }
        }
        """;
}