namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Tests for the <see cref="CompositeValueObjectConvention"/> guard that turns EF Core's cryptic
/// "No suitable constructor was found" model-build failure into an actionable
/// <see cref="TrellisPersistenceMappingException"/> when a composite value object reached by an
/// ownership navigation has no parameterless constructor.
/// </summary>
public class CompositeValueObjectMissingConstructorGuardTests
{
    [Fact]
    public void CompositeValueObject_WithoutParameterlessConstructor_ThrowsActionableError()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MissingCtorDbContext>()
            .UseSqlite(connection).IgnoreManyServiceProvidersCreatedWarning()
            .Options;
        using var ctx = new MissingCtorDbContext(options);

        var act = () => _ = ctx.Model;

        act.Should().Throw<TrellisPersistenceMappingException>()
            .WithMessage("*MissingCtorVo*")
            .WithMessage("*parameterless constructor*")
            .WithMessage("*OwnedEntity*");
    }

    [Fact]
    public void CompositeValueObject_WithHandWrittenPrivateConstructor_ModelBuildsSuccessfully()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HandWrittenCtorDbContext>()
            .UseSqlite(connection).IgnoreManyServiceProvidersCreatedWarning()
            .Options;
        using var ctx = new HandWrittenCtorDbContext(options);

        var act = () => _ = ctx.Model;

        act.Should().NotThrow();
    }

    private sealed class MissingCtorVo : ValueObject
    {
        public MissingCtorVo(string first, string second) => Value = $"{first}|{second}";

        public string Value { get; }

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Value;
        }
    }

    private sealed class MissingCtorEntity
    {
        public int Id { get; set; }

        public MissingCtorVo Detail { get; set; } = null!;
    }

    private sealed class MissingCtorDbContext(DbContextOptions<MissingCtorDbContext> options) : DbContext(options)
    {
        public DbSet<MissingCtorEntity> Items => Set<MissingCtorEntity>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventionsCore([], [typeof(MissingCtorVo)]);
    }

    private sealed class HandWrittenCtorVo : ValueObject
    {
        private HandWrittenCtorVo() => Label = null!;

        public HandWrittenCtorVo(string label) => Label = label;

        public string Label { get; private set; }

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Label;
        }
    }

    private sealed class HandWrittenCtorEntity
    {
        public int Id { get; set; }

        public HandWrittenCtorVo Detail { get; set; } = null!;
    }

    private sealed class HandWrittenCtorDbContext(DbContextOptions<HandWrittenCtorDbContext> options) : DbContext(options)
    {
        public DbSet<HandWrittenCtorEntity> Items => Set<HandWrittenCtorEntity>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventionsCore([], [typeof(HandWrittenCtorVo)]);
    }
}
