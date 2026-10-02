namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Trellis.Primitives;

/// <summary>
/// Tests for <see cref="MoneyConvention"/>.
/// Validates that <see cref="Money"/> properties are automatically mapped as owned types
/// with standardized column naming when
/// <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/> is called.
/// </summary>
public partial class MoneyConventionTests : IDisposable
{
    private MoneyTestDbContext? _context;
    private SqliteConnection? _connection;

    private MoneyTestDbContext Context
    {
        get
        {
            if (_context is not null)
                return _context;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<MoneyTestDbContext>()
                .UseSqlite(_connection).IgnoreManyServiceProvidersCreatedWarning()
                .Options;
            _context = new MoneyTestDbContext(options);
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

    #region Single Money property auto-mapping

    [Fact]
    public void MoneyProperty_AutoMapped_AmountColumnNamedAfterProperty()
    {
        var ownedType = GetOwnedMoneyType<MoneyProduct>(nameof(MoneyProduct.UnitPrice));

        GetColumnName(ownedType.FindProperty(nameof(Money.Amount))!)
            .Should().Be("UnitPrice");
    }

    [Fact]
    public void MoneyProperty_AutoMapped_CurrencyColumnNamedPropertyPlusCurrency()
    {
        var ownedType = GetOwnedMoneyType<MoneyProduct>(nameof(MoneyProduct.UnitPrice));

        GetColumnName(ownedType.FindProperty(nameof(Money.Currency))!)
            .Should().Be("UnitPriceCurrency");
    }

    [Fact]
    public void MoneyProperty_AutoMapped_AmountHasPrecision18Scale3()
    {
        var ownedType = GetOwnedMoneyType<MoneyProduct>(nameof(MoneyProduct.UnitPrice));
        var amount = ownedType.FindProperty(nameof(Money.Amount))!;

        amount.GetPrecision().Should().Be(18);
        amount.GetScale().Should().Be(3);
    }

    [Fact]
    public void MoneyProperty_AutoMapped_CurrencyHasMaxLength3()
    {
        var ownedType = GetOwnedMoneyType<MoneyProduct>(nameof(MoneyProduct.UnitPrice));
        var currency = ownedType.FindProperty(nameof(Money.Currency))!;

        currency.GetMaxLength().Should().Be(3);
    }

    [Fact]
    public void ApplyTrellisConventions_MoneyMaybeMoneyAndUnconfiguredDecimal_OnlyWarnsForUnconfiguredDecimal()
    {
        var warnings = new List<string>();
        var options = new DbContextOptionsBuilder<MoneyWarningDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=TrellisMoneyWarningTests;Trusted_Connection=True;TrustServerCertificate=True")
            .LogTo(warnings.Add, LogLevel.Warning)
            .IgnoreManyServiceProvidersCreatedWarning()
            .Options;

        using var context = new MoneyWarningDbContext(options);
        _ = context.Model;

        warnings.Should().Contain(message =>
            message.Contains($"'{nameof(MoneyWarningEntity.UnconfiguredDecimal)}'", StringComparison.Ordinal));
        warnings.Should().NotContain(message =>
            message.Contains($"decimal property '{nameof(Money.Amount)}'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MoneyProperty_AutoMapped_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = new MoneyProduct
        {
            Id = 1,
            Name = "Widget",
            UnitPrice = Money.Create(29.99m, "USD")
        };

        Context.Products.Add(product);
        await Context.SaveChangesAsync(ct);

        Context.ChangeTracker.Clear();

        var loaded = await Context.Products.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.UnitPrice.Amount.Should().Be(29.99m);
        loaded.UnitPrice.Currency.Value.Should().Be("USD");
    }

    #endregion

    #region Multiple Money properties on same entity

    [Fact]
    public void MultipleMoneyProperties_ProduceDistinctColumnPairs()
    {
        var unitPriceOwned = GetOwnedMoneyType<MoneyLineItem>(nameof(MoneyLineItem.UnitPrice));
        var shippingOwned = GetOwnedMoneyType<MoneyLineItem>(nameof(MoneyLineItem.ShippingCost));

        GetColumnName(unitPriceOwned.FindProperty(nameof(Money.Amount))!)
            .Should().Be("UnitPrice");
        GetColumnName(unitPriceOwned.FindProperty(nameof(Money.Currency))!)
            .Should().Be("UnitPriceCurrency");

        GetColumnName(shippingOwned.FindProperty(nameof(Money.Amount))!)
            .Should().Be("ShippingCost");
        GetColumnName(shippingOwned.FindProperty(nameof(Money.Currency))!)
            .Should().Be("ShippingCostCurrency");
    }

    [Fact]
    public async Task MultipleMoneyProperties_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var lineItem = new MoneyLineItem
        {
            Id = 1,
            UnitPrice = Money.Create(49.99m, "USD"),
            ShippingCost = Money.Create(5.99m, "EUR")
        };

        Context.LineItems.Add(lineItem);
        await Context.SaveChangesAsync(ct);

        Context.ChangeTracker.Clear();

        var loaded = await Context.LineItems.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.UnitPrice.Amount.Should().Be(49.99m);
        loaded.UnitPrice.Currency.Value.Should().Be("USD");
        loaded.ShippingCost.Amount.Should().Be(5.99m);
        loaded.ShippingCost.Currency.Value.Should().Be("EUR");
    }

    [Fact]
    public async Task ThreeDecimalCurrency_RoundTripPreservesAllDigits()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = new MoneyProduct
        {
            Id = 2,
            Name = "Dinar Widget",
            UnitPrice = Money.Create(1.234m, "KWD")
        };

        Context.Products.Add(product);
        await Context.SaveChangesAsync(ct);

        Context.ChangeTracker.Clear();

        var loaded = await Context.Products.FindAsync([2], ct);
        loaded.Should().NotBeNull();
        loaded!.UnitPrice.Amount.Should().Be(1.234m);
        loaded.UnitPrice.Currency.Value.Should().Be("KWD");
    }

    #endregion

    #region Money properties inside owned collections

    [Fact]
    public void MoneyProperty_OnOwnedCollectionItem_AutoMapped_WithStandardColumnNames()
    {
        var ownedType = GetOwnedMoneyType<OwnedMoneyOrder>(nameof(OwnedMoneyOrder.LineItems), nameof(OwnedMoneyOrderLineItem.UnitPrice));

        GetColumnName(ownedType.FindProperty(nameof(Money.Amount))!)
            .Should().Be("UnitPrice");
        GetColumnName(ownedType.FindProperty(nameof(Money.Currency))!)
            .Should().Be("UnitPriceCurrency");
    }

    [Fact]
    public async Task MoneyProperty_OnOwnedCollectionItem_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = new OwnedMoneyOrder
        {
            Id = 1,
            LineItems =
            [
                new OwnedMoneyOrderLineItem
                {
                    Id = 1,
                    Sku = "SKU-1",
                    UnitPrice = Money.Create(12.345m, "BHD")
                }
            ]
        };

        Context.Orders.Add(order);
        await Context.SaveChangesAsync(ct);

        Context.ChangeTracker.Clear();

        var loaded = await Context.Orders
            .Include(x => x.LineItems)
            .SingleAsync(x => x.Id == 1, ct);

        loaded.LineItems.Should().HaveCount(1);
        loaded.LineItems[0].UnitPrice.Amount.Should().Be(12.345m);
        loaded.LineItems[0].UnitPrice.Currency.Value.Should().Be("BHD");
    }

    #endregion

    #region Explicit OwnsOne takes precedence

    [Fact]
    public void ExplicitOwnsOne_IsNotOverwrittenByConvention()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ExplicitMoneyDbContext>()
            .UseSqlite(connection).IgnoreManyServiceProvidersCreatedWarning()
            .Options;

        using var context = new ExplicitMoneyDbContext(options);
        context.Database.EnsureCreated();

        var entityType = context.Model.FindEntityType(typeof(MoneyProduct))!;
        var ownedType = entityType.FindNavigation(nameof(MoneyProduct.UnitPrice))!.TargetEntityType;

        // Should use explicit names, not convention names
        GetColumnName(ownedType.FindProperty(nameof(Money.Amount))!)
            .Should().Be("Price");
        GetColumnName(ownedType.FindProperty(nameof(Money.Currency))!)
            .Should().Be("PriceCurr");
        ownedType.FindProperty(nameof(Money.Amount))!.GetPrecision().Should().Be(12);
        ownedType.FindProperty(nameof(Money.Amount))!.GetScale().Should().Be(2);
    }

    #endregion

    #region Helpers

    private IReadOnlyEntityType GetOwnedMoneyType<TEntity>(string navigationName)
    {
        var entityType = Context.Model.FindEntityType(typeof(TEntity))!;
        return entityType.FindNavigation(navigationName)!.TargetEntityType;
    }

    private IReadOnlyEntityType GetOwnedMoneyType<TEntity>(string collectionNavigationName, string moneyNavigationName)
    {
        var entityType = Context.Model.FindEntityType(typeof(TEntity))!;
        var collectionOwnedType = entityType.FindNavigation(collectionNavigationName)!.TargetEntityType;
        return collectionOwnedType.FindNavigation(moneyNavigationName)!.TargetEntityType;
    }

    private static string? GetColumnName(IReadOnlyProperty property) =>
        property.GetColumnName();

    #endregion

    #region Test entities and contexts

    private class MoneyProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public Money UnitPrice { get; set; } = null!;
    }

    private class MoneyLineItem
    {
        public int Id { get; set; }
        public Money UnitPrice { get; set; } = null!;
        public Money ShippingCost { get; set; } = null!;
    }

    private class MoneyTestDbContext : DbContext
    {
        public DbSet<MoneyProduct> Products => Set<MoneyProduct>();
        public DbSet<MoneyLineItem> LineItems => Set<MoneyLineItem>();
        public DbSet<OwnedMoneyOrder> Orders => Set<OwnedMoneyOrder>();

        public MoneyTestDbContext(DbContextOptions<MoneyTestDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<OwnedMoneyOrder>().OwnsMany(x => x.LineItems);
    }

    private class OwnedMoneyOrder
    {
        public int Id { get; set; }
        public List<OwnedMoneyOrderLineItem> LineItems { get; set; } = [];
    }

    private class OwnedMoneyOrderLineItem
    {
        public int Id { get; set; }
        public string Sku { get; set; } = null!;
        public Money UnitPrice { get; set; } = null!;
    }

    private class ExplicitMoneyDbContext : DbContext
    {
        public DbSet<MoneyProduct> Products => Set<MoneyProduct>();

        public ExplicitMoneyDbContext(DbContextOptions<ExplicitMoneyDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<MoneyProduct>(b => b.OwnsOne(p => p.UnitPrice, m =>
            {
                m.Property(x => x.Amount).HasColumnName("Price").HasPrecision(12, 2);
                m.Property(x => x.Currency).HasColumnName("PriceCurr");
            }));
    }

    private partial class MoneyWarningEntity
    {
        public int Id { get; set; }
        public Money Price { get; set; } = null!;
        public partial Maybe<Money> OptionalPrice { get; set; }
        public decimal UnconfiguredDecimal { get; set; }
    }

    private class MoneyWarningDbContext(DbContextOptions<MoneyWarningDbContext> options) : DbContext(options)
    {
        public DbSet<MoneyWarningEntity> Items => Set<MoneyWarningEntity>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions();
    }

    #endregion
}