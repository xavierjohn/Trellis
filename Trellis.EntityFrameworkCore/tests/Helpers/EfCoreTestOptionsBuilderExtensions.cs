namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Test-only options-builder extensions.
/// </summary>
/// <remarks>
/// EF Core raises <see cref="CoreEventId.ManyServiceProvidersCreatedWarning"/> as an error after twenty
/// distinct internal service providers have been created in a process. This assembly intentionally
/// creates many isolated <see cref="DbContextOptions"/> shapes — one per test fixture, each with its
/// own in-memory SQLite connection and a private DbContext subclass — so test runs can cross the
/// twenty-provider threshold purely as a consequence of the per-test isolation strategy. Suppressing
/// the warning on every test options builder removes the order-dependent failure without changing
/// the production-vs-test fidelity (no <c>UseInternalServiceProvider</c> coupling).
/// </remarks>
internal static class EfCoreTestOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder<TContext> IgnoreManyServiceProvidersCreatedWarning<TContext>(
        this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
    {
        builder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return builder;
    }

    public static DbContextOptionsBuilder IgnoreManyServiceProvidersCreatedWarning(
        this DbContextOptionsBuilder builder)
    {
        builder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        return builder;
    }
}