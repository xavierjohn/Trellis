namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Authorization;

/// <summary>
/// Regression coverage for <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/>'s
/// default inclusion of the <c>Trellis.Authorization</c> assembly in the scan set, so that
/// aggregates carrying an <see cref="ActorId"/>-typed audit field (the canonical
/// <c>CreatedByActorId</c> shape) pick up the scalar converter without the consumer having to
/// add <c>typeof(ActorId).Assembly</c> to the <c>ApplyTrellisConventions</c> call.
/// </summary>
public class AuthorizationAssemblyScanTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ActorIdTestDbContext _context;

    public AuthorizationAssemblyScanTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new ActorIdTestDbContext(_connection);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ActorId_property_round_trips_without_explicit_authorization_assembly()
    {
        var ct = TestContext.Current.CancellationToken;
        var record = new ActorIdHolder
        {
            Id = 1,
            CreatedByActorId = ActorId.Create("alice"),
        };

        _context.Records.Add(record);
        await _context.SaveChangesAsync(ct);
        _context.ChangeTracker.Clear();

        var loaded = await _context.Records.FindAsync([1], ct);

        loaded.Should().NotBeNull();
        loaded!.CreatedByActorId.Should().Be(ActorId.Create("alice"));
    }

    [Fact]
    public void ActorId_column_is_mapped_as_text_not_as_unknown_clr_type()
    {
        // Without Trellis.Authorization in the default scan set, the model finalizer would
        // either omit the property entirely (no surface) or surface it as an unknown CLR type
        // EF Core cannot translate. Asserting the SQLite column type proves the scalar
        // converter is registered.
        var entityType = _context.Model.FindEntityType(typeof(ActorIdHolder))!;
        var property = entityType.FindProperty(nameof(ActorIdHolder.CreatedByActorId))!;

        property.ClrType.Should().Be<ActorId>();
        property.GetColumnType().Should().Be("TEXT");
    }

    private sealed class ActorIdHolder
    {
        public int Id { get; set; }

        public ActorId CreatedByActorId { get; set; } = null!;
    }

    private sealed class ActorIdTestDbContext : DbContext
    {
        public DbSet<ActorIdHolder> Records => Set<ActorIdHolder>();

        public ActorIdTestDbContext(SqliteConnection connection)
            : base(new DbContextOptionsBuilder<ActorIdTestDbContext>()
                .UseSqlite(connection).IgnoreManyServiceProvidersCreatedWarning()
                .Options)
        {
        }

        // Deliberately pass only this assembly. The default scan set must include
        // Trellis.Authorization so the ActorId property gets its scalar converter without
        // an explicit hand-in.
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(ActorIdTestDbContext).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ActorIdHolder>(b =>
            {
                b.HasKey(r => r.Id);
                b.Property(r => r.CreatedByActorId).IsRequired();
            });
    }
}