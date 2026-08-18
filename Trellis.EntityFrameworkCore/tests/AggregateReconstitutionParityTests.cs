namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Conformance test pinning EF Core materialization and the non-EF reconstitution contract
/// (<see cref="IReconstitutionStampable"/> + an author-written <c>Reconstitute</c> factory) to identical
/// results for the same stored state. EF and non-EF persistence are two implementations of the same
/// reconstitution semantics; this proves they agree, so a divergence is caught immediately.
/// </summary>
public sealed class AggregateReconstitutionParityTests
{
    [Fact]
    public async Task EfLoad_and_Reconstitute_produce_equivalent_aggregates()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);
        await using var context = new GadgetContext(connection);
        await context.Database.EnsureCreatedAsync(ct);

        // Arrange — create and save through EF so its interceptors stamp the ETag and timestamps.
        var saved = Gadget.New("g-1", "widget", "alpha", "beta");
        // Preset CreatedAt: the interceptor preserves a non-default CreatedAt and stamps LastModified = now,
        // so the parent's two timestamps differ and a swapped timestamp path would be caught.
        saved.CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        context.Add(saved);
        await context.SaveChangesAsync(ct);
        saved.CreatedAt.Should().NotBe(saved.LastModified, "the timestamp parity assertions are only meaningful if the two differ");

        // Capture the persisted state from the saved instance.
        var id = saved.Id;
        var name = saved.Name;
        var etag = saved.ETag;
        var createdAt = saved.CreatedAt;
        var lastModified = saved.LastModified;
        var storedParts = saved.Parts
            .Select(p => GadgetPart.Reconstitute(p.Id, p.OwnerId, p.Label, p.CreatedAt, p.LastModified))
            .ToList();

        context.ChangeTracker.Clear();

        // Act — load the same row through EF, and rebuild it through the reconstitution contract.
        var efLoaded = await context.Set<Gadget>()
            .Include(g => g.Parts)
            .SingleAsync(g => g.Id == id, ct);

        var reconstituted = Gadget.Reconstitute(id, name, storedParts);
        ((IReconstitutionStampable)reconstituted).StampReconstitutedState(createdAt, lastModified, etag);

        // Assert — EF materialization and the reconstitution contract agree on every observable dimension.
        efLoaded.IsChanged.Should().BeFalse("EF materialization must not raise creation events");
        efLoaded.UncommittedEvents().Should().BeEmpty();

        reconstituted.Id.Should().Be(efLoaded.Id);
        reconstituted.Name.Should().Be(efLoaded.Name);
        reconstituted.ETag.Should().Be(efLoaded.ETag);
        reconstituted.CreatedAt.Should().Be(efLoaded.CreatedAt);
        reconstituted.LastModified.Should().Be(efLoaded.LastModified);
        reconstituted.IsChanged.Should().Be(efLoaded.IsChanged);
        reconstituted.UncommittedEvents().Should().BeEquivalentTo(efLoaded.UncommittedEvents());
        reconstituted.Parts.Select(p => new { p.Id, p.OwnerId, p.Label, p.CreatedAt, p.LastModified })
            .Should().BeEquivalentTo(efLoaded.Parts.Select(p => new { p.Id, p.OwnerId, p.Label, p.CreatedAt, p.LastModified }));
    }
}

// ── Fixtures ─────────────────────────────────────────────────────────────────

// Child entity with encapsulated (private-set) state and a reconstitution factory.
internal sealed class GadgetPart : Entity<string>
{
    private GadgetPart() : base(default!) { }

    private GadgetPart(string id, string ownerId, string label) : base(id)
    {
        OwnerId = ownerId;
        Label = label;
    }

    public string OwnerId { get; private set; } = null!;

    public string Label { get; private set; } = null!;

    internal static GadgetPart New(string ownerId, string label) =>
        // Preset CreatedAt to a fixed past value: the timestamp interceptor preserves a non-default
        // CreatedAt and stamps LastModified = now, so the two child timestamps stay distinct.
        new(Guid.NewGuid().ToString("N"), ownerId, label)
        {
            CreatedAt = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

    internal static GadgetPart Reconstitute(
        string id, string ownerId, string label, DateTimeOffset createdAt, DateTimeOffset lastModified) =>
        new(id, ownerId, label)
        {
            CreatedAt = createdAt,
            LastModified = lastModified,
        };
}

// Aggregate with an encapsulated child collection; create-time and reconstitution factories.
internal sealed class Gadget : Aggregate<string>
{
    private readonly List<GadgetPart> _parts = [];

    private Gadget() : base(default!) { }

    private Gadget(string id, string name, IEnumerable<GadgetPart> parts) : base(id)
    {
        Name = name;
        _parts.AddRange(parts);
    }

    public string Name { get; private set; } = null!;

    public IReadOnlyList<GadgetPart> Parts => _parts.AsReadOnly();

    internal static Gadget New(string id, string name, params string[] labels) =>
        new(id, name, labels.Select(label => GadgetPart.New(id, label)));

    internal static Gadget Reconstitute(string id, string name, IEnumerable<GadgetPart> parts) =>
        new(id, name, parts);
}

internal sealed class GadgetContext(SqliteConnection connection) : DbContext
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(connection).AddTrellisInterceptors();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.ApplyTrellisConventions(typeof(GadgetContext).Assembly);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Gadget>(e =>
        {
            e.ToTable("gadgets");
            e.HasKey(g => g.Id);
            e.Property(g => g.Name).IsRequired();
            e.HasMany(g => g.Parts).WithOne().HasForeignKey(p => p.OwnerId);
        });

        builder.Entity<GadgetPart>(e =>
        {
            e.ToTable("gadget_parts");
            e.HasKey(p => p.Id);
            e.Property(p => p.Label).IsRequired();
        });
    }
}