namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;

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
        FluentActions
            .Invoking(() => new EfUnitOfWork<TestContext>(context: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "context");

    #endregion

    #region DbContextExtensions.SaveChangesResultAsync overloads

    [Fact]
    public Task SaveChangesResultAsync_NullContext_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextExtensions.SaveChangesResultAsync(context: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "context");

    [Fact]
    public Task SaveChangesResultAsync_AcceptAllChanges_NullContext_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextExtensions.SaveChangesResultAsync(context: null!, acceptAllChangesOnSuccess: true))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "context");

    [Fact]
    public Task SaveChangesResultUnitAsync_NullContext_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextExtensions.SaveChangesResultUnitAsync(context: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "context");

    [Fact]
    public Task SaveChangesResultUnitAsync_AcceptAllChanges_NullContext_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextExtensions.SaveChangesResultUnitAsync(context: null!, acceptAllChangesOnSuccess: true))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "context");

    #endregion

    #region DbContextOptionsBuilderExtensions.AddTrellisInterceptors overloads

    [Fact]
    public void AddTrellisInterceptors_Generic_NullBuilder_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextOptionsBuilderExtensions.AddTrellisInterceptors<TestContext>(optionsBuilder: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "optionsBuilder");

    [Fact]
    public void AddTrellisInterceptors_NonGeneric_NullBuilder_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextOptionsBuilderExtensions.AddTrellisInterceptors(optionsBuilder: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "optionsBuilder");

    [Fact]
    public void AddTrellisInterceptors_Generic_TimeProvider_NullBuilder_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextOptionsBuilderExtensions
                .AddTrellisInterceptors<TestContext>(optionsBuilder: null!, timeProvider: TimeProvider.System))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "optionsBuilder");

    [Fact]
    public void AddTrellisInterceptors_NonGeneric_TimeProvider_NullBuilder_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => DbContextOptionsBuilderExtensions
                .AddTrellisInterceptors(optionsBuilder: null!, timeProvider: TimeProvider.System))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "optionsBuilder");

    #endregion

    #region QueryableExtensions overloads

    [Fact]
    public Task FirstOrDefaultMaybeAsync_NullQuery_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => QueryableExtensions.FirstOrDefaultMaybeAsync<string>(query: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "query");

    [Fact]
    public Task FirstOrDefaultMaybeAsync_Predicate_NullQuery_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => QueryableExtensions.FirstOrDefaultMaybeAsync<string>(query: null!, predicate: x => true))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "query");

    [Fact]
    public Task FirstOrDefaultMaybeAsync_Predicate_NullPredicate_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => Array.Empty<string>().AsQueryable().FirstOrDefaultMaybeAsync(predicate: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "predicate");

    [Fact]
    public Task SingleOrDefaultMaybeAsync_NullQuery_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => QueryableExtensions.SingleOrDefaultMaybeAsync<string>(query: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "query");

    [Fact]
    public Task SingleOrDefaultMaybeAsync_Predicate_NullPredicate_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => Array.Empty<string>().AsQueryable().SingleOrDefaultMaybeAsync(predicate: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "predicate");

    [Fact]
    public Task FirstOrDefaultResultAsync_NullQuery_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => QueryableExtensions.FirstOrDefaultResultAsync<string>(
                query: null!,
                notFoundError: new Error.NotFound(ResourceRef.For<string>("x"))))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "query");

    [Fact]
    public Task FirstOrDefaultResultAsync_NullError_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => Array.Empty<string>().AsQueryable().FirstOrDefaultResultAsync(notFoundError: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "notFoundError");

    [Fact]
    public Task FirstOrDefaultResultAsync_Predicate_NullError_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => Array.Empty<string>().AsQueryable()
                .FirstOrDefaultResultAsync(predicate: x => true, notFoundError: null!))
            .Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "notFoundError");

    #endregion

    #region UnitOfWorkServiceCollectionExtensions overloads

    [Fact]
    public void AddTrellisUnitOfWork_NullServices_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => UnitOfWorkServiceCollectionExtensions
                .AddTrellisUnitOfWork<TestContext>(services: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    [Fact]
    public void AddTrellisUnitOfWorkWithoutBehavior_NullServices_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => UnitOfWorkServiceCollectionExtensions
                .AddTrellisUnitOfWorkWithoutBehavior<TestContext>(services: null!))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "services");

    #endregion

    #region ModelConfigurationBuilderExtensions

    [Fact]
    public void ApplyTrellisConventions_NullBuilder_ThrowsArgumentNullException() =>
        FluentActions
            .Invoking(() => ModelConfigurationBuilderExtensions
                .ApplyTrellisConventions(configurationBuilder: null!, assemblies: typeof(ArgumentValidationTests).Assembly))
            .Should().Throw<ArgumentNullException>()
            .Where(exception => exception.ParamName == "configurationBuilder");

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

        act.Should().Throw<ArgumentException>()
            .WithMessage("*[1]*")
            .Where(exception => exception.ParamName == "assemblies");
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
            optionsBuilder.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:"));

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(ArgumentValidationTests).Assembly, null!);
    }

    #endregion
}
