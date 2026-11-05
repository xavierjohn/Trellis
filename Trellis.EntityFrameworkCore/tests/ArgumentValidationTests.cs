namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;
using Trellis.Testing;
using NullGuard = Trellis.Testing.ArgumentNullAssertionsExtensions;

/// <summary>
/// Argument-null-guard tests for the public extension methods covered by the GPT-5.5 review's
/// Minor finding #4. Each public extension method on <see cref="IServiceCollection"/> /
/// <see cref="DbContext"/> / <see cref="DbContextOptionsBuilder"/> / <see cref="IQueryable{T}"/>
/// / <see cref="ModelConfigurationBuilder"/> must throw <see cref="ArgumentNullException"/> with
/// the offending parameter name when called with a null argument — matching the framework
/// discipline established by Trellis.Core 2.3-2 and tightened in PR #458 / PR #459.
/// </summary>
public class ArgumentValidationTests
{
    #region IUnitOfWork / EfUnitOfWork constructors

    [Fact]
    public void EfUnitOfWork_NullContext_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(() => new EfUnitOfWork<TestContext>(context: null!), "context");

    #endregion

    #region DbContextExtensions.SaveChangesResultAsync overloads

    [Fact]
    public Task SaveChangesResultAsync_NullContext_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(() => DbContextExtensions.SaveChangesResultAsync(context: null!), "context");

    [Fact]
    public Task SaveChangesResultAsync_AcceptAllChanges_NullContext_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => DbContextExtensions.SaveChangesResultAsync(context: null!, acceptAllChangesOnSuccess: true),
            "context");

    [Fact]
    public Task SaveChangesResultUnitAsync_NullContext_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => DbContextExtensions.SaveChangesResultUnitAsync(context: null!),
            "context");

    [Fact]
    public Task SaveChangesResultUnitAsync_AcceptAllChanges_NullContext_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => DbContextExtensions.SaveChangesResultUnitAsync(context: null!, acceptAllChangesOnSuccess: true),
            "context");

    #endregion

    #region DbContextOptionsBuilderExtensions.AddTrellisInterceptors overloads

    [Fact]
    public void AddTrellisInterceptors_Generic_NullBuilder_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(
            () => DbContextOptionsBuilderExtensions.AddTrellisInterceptors<TestContext>(optionsBuilder: null!),
            "optionsBuilder");

    [Fact]
    public void AddTrellisInterceptors_NonGeneric_NullBuilder_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(
            () => DbContextOptionsBuilderExtensions.AddTrellisInterceptors(optionsBuilder: null!),
            "optionsBuilder");

    [Fact]
    public void AddTrellisInterceptors_Generic_TimeProvider_NullBuilder_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(() => DbContextOptionsBuilderExtensions
            .AddTrellisInterceptors<TestContext>(optionsBuilder: null!, timeProvider: TimeProvider.System), "optionsBuilder");

    [Fact]
    public void AddTrellisInterceptors_NonGeneric_TimeProvider_NullBuilder_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(() => DbContextOptionsBuilderExtensions
            .AddTrellisInterceptors(optionsBuilder: null!, timeProvider: TimeProvider.System), "optionsBuilder");

    #endregion

    #region QueryableExtensions overloads

    [Fact]
    public Task FirstOrDefaultMaybeAsync_NullQuery_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => QueryableExtensions.FirstOrDefaultMaybeAsync<string>(query: null!),
            "query");

    [Fact]
    public Task FirstOrDefaultMaybeAsync_Predicate_NullQuery_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => QueryableExtensions.FirstOrDefaultMaybeAsync<string>(query: null!, predicate: x => true),
            "query");

    [Fact]
    public Task FirstOrDefaultMaybeAsync_Predicate_NullPredicate_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => Array.Empty<string>().AsQueryable().FirstOrDefaultMaybeAsync(predicate: null!),
            "predicate");

    [Fact]
    public Task SingleOrDefaultMaybeAsync_NullQuery_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => QueryableExtensions.SingleOrDefaultMaybeAsync<string>(query: null!),
            "query");

    [Fact]
    public Task SingleOrDefaultMaybeAsync_Predicate_NullPredicate_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => Array.Empty<string>().AsQueryable().SingleOrDefaultMaybeAsync(predicate: null!),
            "predicate");

    [Fact]
    public Task FirstOrDefaultResultAsync_NullQuery_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(() => QueryableExtensions.FirstOrDefaultResultAsync<string>(
            query: null!,
            notFoundError: new Error.NotFound(ResourceRef.For<string>("x"))), "query");

    [Fact]
    public Task FirstOrDefaultResultAsync_NullError_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(
            () => Array.Empty<string>().AsQueryable().FirstOrDefaultResultAsync(notFoundError: null!),
            "notFoundError");

    [Fact]
    public Task FirstOrDefaultResultAsync_Predicate_NullError_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNullAsync(() => Array.Empty<string>().AsQueryable()
            .FirstOrDefaultResultAsync(predicate: x => true, notFoundError: null!), "notFoundError");

    #endregion

    #region UnitOfWorkServiceCollectionExtensions overloads

    [Fact]
    public void AddTrellisUnitOfWork_NullServices_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(() => UnitOfWorkServiceCollectionExtensions
            .AddTrellisUnitOfWork<TestContext>(services: null!), "services");

    [Fact]
    public void AddTrellisUnitOfWorkWithoutBehavior_NullServices_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(() => UnitOfWorkServiceCollectionExtensions
            .AddTrellisUnitOfWorkWithoutBehavior<TestContext>(services: null!), "services");

    #endregion

    #region ModelConfigurationBuilderExtensions

    [Fact]
    public void ApplyTrellisConventions_NullBuilder_ThrowsArgumentNullException() =>
        NullGuard.ShouldRejectNull(
            () => ModelConfigurationBuilderExtensions
                .ApplyTrellisConventions(configurationBuilder: null!, assemblies: typeof(ArgumentValidationTests).Assembly),
            "configurationBuilder");

    /// <summary>
    /// The null-element-in-assemblies guard fires inside <c>ApplyTrellisConventions</c> after the
    /// <c>configurationBuilder</c> null check; we exercise it by routing through a real
    /// <see cref="DbContext"/>'s <c>ConfigureConventions</c> hook (the only public way to obtain
    /// a <see cref="ModelConfigurationBuilder"/>). The <see cref="ArgumentException"/> propagates
    /// out through model build with its <c>ParamName</c> intact and the index-tagged message.
    /// </summary>
    [Fact]
    public void ApplyTrellisConventions_NullAssemblyElement_ThrowsArgumentExceptionWithIndex()
    {
        var act = () =>
        {
            using var ctx = new NullElementContext();
            // Force model build so ConfigureConventions runs.
            _ = ctx.Model;
        };

        act.ShouldRejectArgument("assemblies").WithMessage("*[1]*");
    }

    #endregion

    #region Helpers

    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> options) : base(options) { }
    }

    /// <summary>
    /// DbContext whose <c>ConfigureConventions</c> calls
    /// <c>ApplyTrellisConventions</c> with a null element in the assemblies array — used by
    /// <see cref="ApplyTrellisConventions_NullAssemblyElement_ThrowsArgumentExceptionWithIndex"/>.
    /// </summary>
    private sealed class NullElementContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:")).IgnoreManyServiceProvidersCreatedWarning();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(ArgumentValidationTests).Assembly, null!);
    }

    #endregion
}