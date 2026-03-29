namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="Maybe{T}"/> with composite ValueObjects like <see cref="Money"/>.
/// <para>
/// Level 1: <see cref="MaybeConvention"/> should not crash when encountering
/// <c>Maybe&lt;Money&gt;</c> — a composite ValueObject that is not a scalar type.
/// </para>
/// <para>
/// Level 2: <c>Maybe&lt;Money&gt;</c> should auto-configure as an optional owned type,
/// producing nullable Amount and Currency columns that follow <see cref="MoneyConvention"/>
/// naming, and round-trip through EF Core with both <c>Some</c> and <c>None</c> values.
/// </para>
/// </summary>
public partial class MaybeMoneyTests : IDisposable
{
    private MaybeMoneyDbContext? _context;
    private SqliteConnection? _connection;

    private MaybeMoneyDbContext Context
    {
        get
        {
            if (_context is not null)
                return _context;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<MaybeMoneyDbContext>()
                .UseSqlite(_connection)
                .Options;
            _context = new MaybeMoneyDbContext(options);
            _context.Database.EnsureCreated();
            return _context;
        }
    }

    public void Dispose()
    {
        _context?.Dispose();
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Level 1: MaybeConvention does not crash on Maybe<Money>

    [Fact]
    public void MaybeMoney_ModelBuilds_WithoutException()
    {
        var model = Context.Model;
        model.Should().NotBeNull();
    }

    [Fact]
    public void MaybeMoney_DiagnosticsApi_ReportsAsMapped()
    {
        var mappings = Context.GetMaybePropertyMappings();

        var fineMapping = mappings.SingleOrDefault(m => m.PropertyName == "MonetaryFinePaid");
        fineMapping.Should().NotBeNull();
        fineMapping!.IsMapped.Should().BeTrue("Maybe<Money> should report as mapped in diagnostics");
        fineMapping.IsNullable.Should().BeTrue("Maybe<Money> should report as nullable in diagnostics");
        fineMapping.ColumnName.Should().Be("MonetaryFinePaid");
    }

    #endregion

    #region Level 2: Maybe<Money> auto-configures as optional owned type

    [Fact]
    public void MaybeMoney_IsMappedAsOwnedNavigation()
    {
        var entityType = Context.Model.FindEntityType(typeof(PenaltyEntity))!;

        // Ownership is created via the backing field _monetaryFinePaid, so the
        // EF Core navigation name is the field name (not the CLR property name).
        var navigation = entityType.FindNavigation("_monetaryFinePaid");
        navigation.Should().NotBeNull("Maybe<Money> should produce an owned navigation via backing field");
        navigation!.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public void MaybeMoney_ColumnNaming_FollowsMoneyConvention()
    {
        var entityType = Context.Model.FindEntityType(typeof(PenaltyEntity))!;
        var ownedType = entityType.FindNavigation("_monetaryFinePaid")!.TargetEntityType;

        // Column names use the original property name (MonetaryFinePaid), not the field name
        ownedType.FindProperty(nameof(Money.Amount))!.GetColumnName()
            .Should().Be("MonetaryFinePaid");
        ownedType.FindProperty(nameof(Money.Currency))!.GetColumnName()
            .Should().Be("MonetaryFinePaidCurrency");
    }

    [Fact]
    public void RequiredMoney_CoexistsWithOptionalMoney()
    {
        var entityType = Context.Model.FindEntityType(typeof(PenaltyEntity))!;

        // Required Money (MonetaryFine) should be a non-nullable owned navigation
        var requiredNav = entityType.FindNavigation("MonetaryFine");
        requiredNav.Should().NotBeNull("required Money should still be an owned navigation");
        requiredNav!.TargetEntityType.IsOwned().Should().BeTrue();

        var requiredAmount = requiredNav.TargetEntityType.FindProperty(nameof(Money.Amount))!;
        requiredAmount.IsNullable.Should().BeFalse("required Money amount should not be nullable");
    }

    [Fact]
    public async Task MaybeMoney_RoundTrip_WithValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var penalty = new PenaltyEntity
        {
            Id = 1,
            Description = "Late payment",
            MonetaryFine = Money.Create(500.00m, "USD"),
            MonetaryFinePaid = Maybe.From(Money.Create(500.00m, "USD"))
        };

        Context.Penalties.Add(penalty);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.Penalties.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.MonetaryFinePaid.HasValue.Should().BeTrue();
        loaded.MonetaryFinePaid.Value.Amount.Should().Be(500.00m);
        loaded.MonetaryFinePaid.Value.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public async Task MaybeMoney_RoundTrip_WithNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var penalty = new PenaltyEntity
        {
            Id = 2,
            Description = "Warning only",
            MonetaryFine = Money.Create(100.00m, "USD")
            // MonetaryFinePaid defaults to Maybe<Money>.None
        };

        Context.Penalties.Add(penalty);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.Penalties.FindAsync([2], ct);
        loaded.Should().NotBeNull();
        loaded!.MonetaryFinePaid.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task MaybeMoney_RequiredAndOptional_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var penalty = new PenaltyEntity
        {
            Id = 3,
            Description = "Fine with partial payment",
            MonetaryFine = Money.Create(1000.00m, "USD"),
            MonetaryFinePaid = Maybe.From(Money.Create(250.00m, "USD"))
        };

        Context.Penalties.Add(penalty);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.Penalties.FindAsync([3], ct);
        loaded.Should().NotBeNull();
        loaded!.MonetaryFine.Amount.Should().Be(1000.00m);
        loaded.MonetaryFinePaid.HasValue.Should().BeTrue();
        loaded.MonetaryFinePaid.Value.Amount.Should().Be(250.00m);
    }

    [Fact]
    public async Task MaybeMoney_RequiredPresent_OptionalNone_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var penalty = new PenaltyEntity
        {
            Id = 4,
            Description = "Fine issued, not yet paid",
            MonetaryFine = Money.Create(750.00m, "EUR")
            // MonetaryFinePaid stays None
        };

        Context.Penalties.Add(penalty);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.Penalties.FindAsync([4], ct);
        loaded.Should().NotBeNull();
        loaded!.MonetaryFine.Amount.Should().Be(750.00m);
        loaded.MonetaryFine.Currency.Value.Should().Be("EUR");
        loaded.MonetaryFinePaid.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task MaybeMoney_Update_SomeToNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var penalty = new PenaltyEntity
        {
            Id = 5,
            Description = "Refunded fine",
            MonetaryFine = Money.Create(500.00m, "USD"),
            MonetaryFinePaid = Maybe.From(Money.Create(500.00m, "USD"))
        };

        Context.Penalties.Add(penalty);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        // Update: clear the payment
        var tracked = await Context.Penalties.FindAsync([5], ct);
        tracked!.MonetaryFinePaid = Maybe<Money>.None;
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var reloaded = await Context.Penalties.FindAsync([5], ct);
        reloaded.Should().NotBeNull();
        reloaded!.MonetaryFinePaid.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task MaybeMoney_Update_NoneToSome()
    {
        var ct = TestContext.Current.CancellationToken;
        var penalty = new PenaltyEntity
        {
            Id = 6,
            Description = "Payment received later",
            MonetaryFine = Money.Create(300.00m, "GBP")
            // MonetaryFinePaid starts as None
        };

        Context.Penalties.Add(penalty);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        // Update: record the payment
        var tracked = await Context.Penalties.FindAsync([6], ct);
        tracked!.MonetaryFinePaid = Maybe.From(Money.Create(300.00m, "GBP"));
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var reloaded = await Context.Penalties.FindAsync([6], ct);
        reloaded.Should().NotBeNull();
        reloaded!.MonetaryFinePaid.HasValue.Should().BeTrue();
        reloaded.MonetaryFinePaid.Value.Amount.Should().Be(300.00m);
        reloaded.MonetaryFinePaid.Value.Currency.Value.Should().Be("GBP");
    }

    #endregion

    #region ExecuteUpdate guard for owned Maybe<T>

    [Fact]
    public void SetMaybeValue_OnOwnedMoney_ThrowsWithClearMessage()
    {
        // ExecuteUpdate cannot target owned navigations — only scalar properties.
        // SetMaybeValue should throw early with a helpful message instead of
        // failing deep inside EF Core's query translator.
        var act = () => Context.Penalties
            .Where(p => p.Id == 1)
            .ExecuteUpdate(b => b.SetMaybeValue(
                p => p.MonetaryFinePaid,
                Money.Create(100m, "USD")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*composite owned type*");
    }

    [Fact]
    public void SetMaybeNone_OnOwnedMoney_ThrowsWithClearMessage()
    {
        var act = () => Context.Penalties
            .Where(p => p.Id == 1)
            .ExecuteUpdate(b => b.SetMaybeNone<PenaltyEntity, Money>(p => p.MonetaryFinePaid));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*composite owned type*");
    }

    #endregion

    public partial class PenaltyEntity
    {
        public int Id { get; set; }
        public string Description { get; set; } = null!;
        public Money MonetaryFine { get; set; } = null!;
        public partial Maybe<Money> MonetaryFinePaid { get; set; }
    }

    private class MaybeMoneyDbContext : DbContext
    {
        public DbSet<PenaltyEntity> Penalties => Set<PenaltyEntity>();

        public MaybeMoneyDbContext(DbContextOptions<MaybeMoneyDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();
    }
}