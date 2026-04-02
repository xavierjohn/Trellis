namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Trellis.EntityFrameworkCore.Tests.Helpers;
using Trellis.Primitives;

/// <summary>
/// Tests for the composite <see cref="ValueObject"/> auto-owned convention.
/// Validates that non-scalar ValueObject types (types deriving from <see cref="ValueObject"/>
/// but not implementing <c>IScalarValue</c>) are automatically registered as EF Core owned types
/// when <see cref="ModelConfigurationBuilderExtensions.ApplyTrellisConventions"/> is called.
/// </summary>
public partial class CompositeValueObjectConventionTests : IDisposable
{
    private CompositeVoTestDbContext? _context;
    private SqliteConnection? _connection;

    private CompositeVoTestDbContext Context
    {
        get
        {
            if (_context is not null)
                return _context;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<CompositeVoTestDbContext>()
                .UseSqlite(_connection)
                .Options;
            _context = new CompositeVoTestDbContext(options);
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

    #region Required composite VO — auto-owned

    [Fact]
    public void RequiredAddress_AutoMapped_AsOwnedType()
    {
        var entityType = Context.Model.FindEntityType(typeof(AddressEntity))!;
        var navigation = entityType.FindNavigation(nameof(AddressEntity.ShippingAddress));

        navigation.Should().NotBeNull();
        navigation!.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public void RequiredAddress_ColumnsUsePropertyNames()
    {
        var ownedType = GetOwnedType<AddressEntity>(nameof(AddressEntity.ShippingAddress));

        // EF Core's default owned-type column naming uses property names directly
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.Street))!)
            .Should().Be("Street");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.City))!)
            .Should().Be("City");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.State))!)
            .Should().Be("State");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.ZipCode))!)
            .Should().Be("ZipCode");
    }

    [Fact]
    public void RequiredAddress_ColumnsAreRequired()
    {
        var ownedType = GetOwnedType<AddressEntity>(nameof(AddressEntity.ShippingAddress));

        ownedType.FindProperty(nameof(TestAddress.Street))!.IsNullable.Should().BeFalse();
        ownedType.FindProperty(nameof(TestAddress.City))!.IsNullable.Should().BeFalse();
        ownedType.FindProperty(nameof(TestAddress.State))!.IsNullable.Should().BeFalse();
        ownedType.FindProperty(nameof(TestAddress.ZipCode))!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public async Task RequiredAddress_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new AddressEntity
        {
            Id = 1,
            ShippingAddress = TestAddress.Create("123 Main St", "Springfield", "IL", "62701")
        };

        Context.AddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.AddressEntities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.ShippingAddress.Street.Should().Be("123 Main St");
        loaded.ShippingAddress.City.Should().Be("Springfield");
        loaded.ShippingAddress.State.Should().Be("IL");
        loaded.ShippingAddress.ZipCode.Should().Be("62701");
    }

    #endregion

    #region Maybe<composite VO> — nullable owned columns

    [Fact]
    public void MaybeAddress_AutoMapped_AsOwnedType()
    {
        var entityType = Context.Model.FindEntityType(typeof(MaybeAddressEntity))!;
        // Maybe<T> ownership is created via the source-generated backing field
        var navigation = entityType.FindNavigation("_shippingAddress");

        navigation.Should().NotBeNull();
        navigation!.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public void MaybeAddress_ColumnsAreNullable()
    {
        var ownedType = GetOwnedMaybeType<MaybeAddressEntity>("_shippingAddress");

        ownedType.FindProperty(nameof(TestAddress.Street))!.IsNullable.Should().BeTrue();
        ownedType.FindProperty(nameof(TestAddress.City))!.IsNullable.Should().BeTrue();
        ownedType.FindProperty(nameof(TestAddress.State))!.IsNullable.Should().BeTrue();
        ownedType.FindProperty(nameof(TestAddress.ZipCode))!.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void MaybeAddress_ColumnNaming_UsesOriginalPropertyPrefix()
    {
        var ownedType = GetOwnedMaybeType<MaybeAddressEntity>("_shippingAddress");

        // Convention should use the original property name "ShippingAddress" as the prefix,
        // not the backing field name "_shippingAddress"
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.Street))!)
            .Should().Be("ShippingAddress_Street");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.City))!)
            .Should().Be("ShippingAddress_City");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.State))!)
            .Should().Be("ShippingAddress_State");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.ZipCode))!)
            .Should().Be("ShippingAddress_ZipCode");
    }

    [Fact]
    public async Task MaybeAddress_WithValue_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MaybeAddressEntity
        {
            Id = 1,
            ShippingAddress = Maybe.From(TestAddress.Create("456 Oak Ave", "Portland", "OR", "97201"))
        };

        Context.MaybeAddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MaybeAddressEntities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.ShippingAddress.HasValue.Should().BeTrue();
        loaded.ShippingAddress.Value.Street.Should().Be("456 Oak Ave");
        loaded.ShippingAddress.Value.City.Should().Be("Portland");
        loaded.ShippingAddress.Value.State.Should().Be("OR");
        loaded.ShippingAddress.Value.ZipCode.Should().Be("97201");
    }

    [Fact]
    public async Task MaybeAddress_WithNone_RoundTripPreservesNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MaybeAddressEntity { Id = 2 };

        Context.MaybeAddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MaybeAddressEntities.FindAsync([2], ct);
        loaded.Should().NotBeNull();
        loaded!.ShippingAddress.HasValue.Should().BeFalse();
    }

    #endregion

    #region Multiple composite VO properties on same entity

    [Fact]
    public void MultipleAddresses_ProduceDistinctOwnedNavigations()
    {
        var entityType = Context.Model.FindEntityType(typeof(MultiAddressEntity))!;

        var billing = entityType.FindNavigation(nameof(MultiAddressEntity.BillingAddress));
        var shipping = entityType.FindNavigation(nameof(MultiAddressEntity.ShippingAddress));

        billing.Should().NotBeNull();
        shipping.Should().NotBeNull();
        billing!.TargetEntityType.IsOwned().Should().BeTrue();
        shipping!.TargetEntityType.IsOwned().Should().BeTrue();

        // Each navigation targets a distinct owned entity type instance
        billing.TargetEntityType.Should().NotBeSameAs(shipping.TargetEntityType);
    }

    [Fact]
    public async Task MultipleAddresses_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MultiAddressEntity
        {
            Id = 1,
            BillingAddress = TestAddress.Create("100 Billing Ln", "New York", "NY", "10001"),
            ShippingAddress = TestAddress.Create("200 Shipping Dr", "Los Angeles", "CA", "90001")
        };

        Context.MultiAddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MultiAddressEntities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.BillingAddress.Street.Should().Be("100 Billing Ln");
        loaded.BillingAddress.City.Should().Be("New York");
        loaded.ShippingAddress.Street.Should().Be("200 Shipping Dr");
        loaded.ShippingAddress.City.Should().Be("Los Angeles");
    }

    #endregion

    #region Maybe<composite VO> — update transitions

    [Fact]
    public async Task MaybeAddress_Update_SomeToNone_ClearsAllColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MaybeAddressEntity
        {
            Id = 10,
            ShippingAddress = Maybe.From(TestAddress.Create("789 Elm St", "Seattle", "WA", "98101"))
        };

        Context.MaybeAddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);

        // Update to None
        entity.ShippingAddress = Maybe<TestAddress>.None;
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MaybeAddressEntities.FindAsync([10], ct);
        loaded.Should().NotBeNull();
        loaded!.ShippingAddress.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task MaybeAddress_Update_NoneToSome_PersistsAllColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MaybeAddressEntity { Id = 11 };

        Context.MaybeAddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);

        // Update from None to Some
        Context.ChangeTracker.Clear();
        var tracked = await Context.MaybeAddressEntities.FindAsync([11], ct);
        tracked!.ShippingAddress = Maybe.From(TestAddress.Create("321 Pine Rd", "Denver", "CO", "80201"));
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MaybeAddressEntities.FindAsync([11], ct);
        loaded.Should().NotBeNull();
        loaded!.ShippingAddress.HasValue.Should().BeTrue();
        loaded.ShippingAddress.Value.City.Should().Be("Denver");
    }

    #endregion

    #region Composite VO containing a scalar VO

    [Fact]
    public void CompositeContainingScalarVo_AutoMapped_AsOwnedType()
    {
        var entityType = Context.Model.FindEntityType(typeof(RichAddressEntity))!;
        var navigation = entityType.FindNavigation(nameof(RichAddressEntity.Address));

        navigation.Should().NotBeNull();
        navigation!.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public async Task CompositeContainingScalarVo_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new RichAddressEntity
        {
            Id = 1,
            Address = TestRichAddress.Create("500 Market St", "San Francisco", "CA", "94105")
        };

        Context.RichAddressEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.RichAddressEntities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.Address.Street.Should().Be("500 Market St");
        loaded.Address.City.Should().Be("San Francisco");
        ((string)loaded.Address.State).Should().Be("CA");
        loaded.Address.ZipCode.Should().Be("94105");
    }

    #endregion

    #region Nested composite VO (composite containing Money)

    [Fact]
    public void NestedComposite_RequiredAddressWithMoney_AutoMapped()
    {
        var entityType = Context.Model.FindEntityType(typeof(NestedCompositeEntity))!;
        var navigation = entityType.FindNavigation(nameof(NestedCompositeEntity.Destination));

        navigation.Should().NotBeNull();
        navigation!.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public async Task NestedComposite_Required_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new NestedCompositeEntity
        {
            Id = 1,
            Destination = TestAddressWithMoney.Create("100 Dock St", "Boston", 15.00m, "USD")
        };

        Context.NestedCompositeEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.NestedCompositeEntities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.Destination.Street.Should().Be("100 Dock St");
        loaded.Destination.City.Should().Be("Boston");
        loaded.Destination.DeliveryFee.Amount.Should().Be(15.00m);
        loaded.Destination.DeliveryFee.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void MaybeNestedComposite_ColumnsAreNotNullable_InSeparateTable()
    {
        var entityType = Context.Model.FindEntityType(typeof(MaybeNestedCompositeEntity))!;
        var addressNav = entityType.FindNavigation("_destination");
        addressNav.Should().NotBeNull();

        var addressType = addressNav!.TargetEntityType;

        // With separate-table strategy, columns stay NOT NULL because
        // the row's existence indicates presence (no sentinel needed).
        addressType.FindProperty(nameof(TestAddressWithMoney.Street))!.IsNullable.Should().BeFalse();
        addressType.FindProperty(nameof(TestAddressWithMoney.City))!.IsNullable.Should().BeFalse();

        // Nested Money columns are also NOT NULL in the separate table
        var moneyNav = addressType.FindNavigation(nameof(TestAddressWithMoney.DeliveryFee));
        moneyNav.Should().NotBeNull();
        var moneyType = moneyNav!.TargetEntityType;
        moneyType.FindProperty(nameof(Money.Amount))!.IsNullable.Should().BeFalse();
        moneyType.FindProperty(nameof(Money.Currency))!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void MaybeNestedComposite_UsesOwnTable()
    {
        var entityType = Context.Model.FindEntityType(typeof(MaybeNestedCompositeEntity))!;
        var addressNav = entityType.FindNavigation("_destination")!;
        var addressType = addressNav.TargetEntityType;

        // Should be in its own table, not table-split with the owner
        addressType.GetTableName().Should().Be("MaybeNestedCompositeEntity_Destination");
    }

    [Fact]
    public async Task MaybeNestedComposite_WithValue_RoundTripWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MaybeNestedCompositeEntity
        {
            Id = 1,
            Destination = Maybe.From(TestAddressWithMoney.Create("200 Harbor Dr", "Miami", 25.50m, "USD"))
        };

        Context.MaybeNestedCompositeEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MaybeNestedCompositeEntities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.Destination.HasValue.Should().BeTrue();
        loaded.Destination.Value.Street.Should().Be("200 Harbor Dr");
        loaded.Destination.Value.DeliveryFee.Amount.Should().Be(25.50m);
        loaded.Destination.Value.DeliveryFee.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public async Task MaybeNestedComposite_WithNone_RoundTripPreservesNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new MaybeNestedCompositeEntity { Id = 2 };

        Context.MaybeNestedCompositeEntities.Add(entity);
        await Context.SaveChangesAsync(ct);
        Context.ChangeTracker.Clear();

        var loaded = await Context.MaybeNestedCompositeEntities.FindAsync([2], ct);
        loaded.Should().NotBeNull();
        loaded!.Destination.HasValue.Should().BeFalse();
    }

    #endregion

    #region Unrelated owned entity not modified by convention

    [Fact]
    public void UnrelatedOwnedEntity_NotAValueObject_IsNotModifiedByConvention()
    {
        // OwnedMetadata is configured via explicit OwnsOne, is NOT a ValueObject subclass.
        // The convention must not interfere with it.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<MixedOwnedDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new MixedOwnedDbContext(options);
        context.Database.EnsureCreated();

        var entityType = context.Model.FindEntityType(typeof(EntityWithMixedOwned))!;
        var metadataNav = entityType.FindNavigation(nameof(EntityWithMixedOwned.Metadata));

        metadataNav.Should().NotBeNull();
        // Metadata columns should remain required (convention must not mark them nullable)
        metadataNav!.TargetEntityType.FindProperty(nameof(OwnedMetadata.CreatedBy))!
            .IsNullable.Should().BeFalse();
    }

    #endregion

    #region Maybe<composite VO> with non-nullable value-type properties

    [Fact]
    public void MaybeDateRange_WithValueTypeProperties_ModelBuildsSuccessfully()
    {
        // DateRange has DateTime (non-nullable value type) properties.
        // IsRequired(false) throws on value types, so the convention must
        // fall back to the separate-table strategy.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DateRangeDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new DateRangeDbContext(options);
        context.Database.EnsureCreated();

        var entityType = context.Model.FindEntityType(typeof(MaybeDateRangeEntity))!;
        var nav = entityType.FindNavigation("_availability");
        nav.Should().NotBeNull();
        nav!.TargetEntityType.IsOwned().Should().BeTrue();
    }

    [Fact]
    public async Task MaybeDateRange_WithValue_RoundTripWorks()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DateRangeDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new DateRangeDbContext(options);
        context.Database.EnsureCreated();

        var ct = TestContext.Current.CancellationToken;
        var range = TestDateRange.Create(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), "FY2026");
        var entity = new MaybeDateRangeEntity
        {
            Id = 1,
            Availability = Maybe.From(range)
        };

        context.Entities.Add(entity);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        var loaded = await context.Entities.FindAsync([1], ct);
        loaded.Should().NotBeNull();
        loaded!.Availability.HasValue.Should().BeTrue();
        loaded.Availability.Value.Label.Should().Be("FY2026");
    }

    #endregion

    #region Open generic ValueObject subclasses excluded from scanning

    [Fact]
    public void OpenGenericValueObject_NotRegisteredAsOwned() =>
        // GenericRange<T> : ValueObject is an open generic — it must not be
        // registered as an owned type (EF Core can't map open generics).
        TrellisTypeScanner.IsCompositeValueObject(typeof(GenericRange<>))
            .Should().BeFalse();

    [Fact]
    public void ClosedGenericValueObject_IsRegisteredAsOwned() =>
        // GenericRange<int> is a concrete closed generic — it should be detected.
        TrellisTypeScanner.IsCompositeValueObject(typeof(GenericRange<int>))
            .Should().BeTrue();

    #endregion

    #region Explicit OwnsOne overrides convention

    [Fact]
    public void ExplicitOwnsOne_IsNotOverwrittenByConvention()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ExplicitAddressDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new ExplicitAddressDbContext(options);
        context.Database.EnsureCreated();

        var entityType = context.Model.FindEntityType(typeof(AddressEntity))!;
        var ownedType = entityType.FindNavigation(nameof(AddressEntity.ShippingAddress))!.TargetEntityType;

        GetColumnName(ownedType.FindProperty(nameof(TestAddress.Street))!)
            .Should().Be("addr_street");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.City))!)
            .Should().Be("addr_city");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.State))!)
            .Should().Be("addr_state");
        GetColumnName(ownedType.FindProperty(nameof(TestAddress.ZipCode))!)
            .Should().Be("addr_zip");
    }

    #endregion

    #region Helpers

    private IReadOnlyEntityType GetOwnedType<TEntity>(string navigationName)
    {
        var entityType = Context.Model.FindEntityType(typeof(TEntity))!;
        return entityType.FindNavigation(navigationName)!.TargetEntityType;
    }

    private IReadOnlyEntityType GetOwnedMaybeType<TEntity>(string backingFieldName)
    {
        var entityType = Context.Model.FindEntityType(typeof(TEntity))!;
        return entityType.FindNavigation(backingFieldName)!.TargetEntityType;
    }

    private static string? GetColumnName(IReadOnlyProperty property) =>
        property.GetColumnName();

    #endregion

    #region Test entities and contexts

    private class AddressEntity
    {
        public int Id { get; set; }
        public TestAddress ShippingAddress { get; set; } = null!;
    }

    private partial class MaybeAddressEntity
    {
        public int Id { get; set; }
        public partial Maybe<TestAddress> ShippingAddress { get; set; }
    }

    private class MultiAddressEntity
    {
        public int Id { get; set; }
        public TestAddress BillingAddress { get; set; } = null!;
        public TestAddress ShippingAddress { get; set; } = null!;
    }

    private class RichAddressEntity
    {
        public int Id { get; set; }
        public TestRichAddress Address { get; set; } = null!;
    }

    private class NestedCompositeEntity
    {
        public int Id { get; set; }
        public TestAddressWithMoney Destination { get; set; } = null!;
    }

    private partial class MaybeNestedCompositeEntity
    {
        public int Id { get; set; }
        public partial Maybe<TestAddressWithMoney> Destination { get; set; }
    }

    /// <summary>
    /// Non-ValueObject owned type — must not be affected by the composite VO convention.
    /// </summary>
    private class OwnedMetadata
    {
        public string CreatedBy { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }

    private class EntityWithMixedOwned
    {
        public int Id { get; set; }
        public TestAddress ShippingAddress { get; set; } = null!;
        public OwnedMetadata Metadata { get; set; } = null!;
    }

    private class CompositeVoTestDbContext : DbContext
    {
        public DbSet<AddressEntity> AddressEntities => Set<AddressEntity>();
        public DbSet<MaybeAddressEntity> MaybeAddressEntities => Set<MaybeAddressEntity>();
        public DbSet<MultiAddressEntity> MultiAddressEntities => Set<MultiAddressEntity>();
        public DbSet<RichAddressEntity> RichAddressEntities => Set<RichAddressEntity>();
        public DbSet<NestedCompositeEntity> NestedCompositeEntities => Set<NestedCompositeEntity>();
        public DbSet<MaybeNestedCompositeEntity> MaybeNestedCompositeEntities => Set<MaybeNestedCompositeEntity>();

        public CompositeVoTestDbContext(DbContextOptions<CompositeVoTestDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestAddress).Assembly);
    }

    private class ExplicitAddressDbContext : DbContext
    {
        public DbSet<AddressEntity> AddressEntities => Set<AddressEntity>();

        public ExplicitAddressDbContext(DbContextOptions<ExplicitAddressDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestAddress).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<AddressEntity>(b => b.OwnsOne(e => e.ShippingAddress, a =>
            {
                a.Property(x => x.Street).HasColumnName("addr_street");
                a.Property(x => x.City).HasColumnName("addr_city");
                a.Property(x => x.State).HasColumnName("addr_state");
                a.Property(x => x.ZipCode).HasColumnName("addr_zip");
            }));
    }

    private class MixedOwnedDbContext : DbContext
    {
        public DbSet<EntityWithMixedOwned> Entities => Set<EntityWithMixedOwned>();

        public MixedOwnedDbContext(DbContextOptions<MixedOwnedDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestAddress).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<EntityWithMixedOwned>(b =>
                b.OwnsOne(e => e.Metadata));
    }

    private partial class MaybeDateRangeEntity
    {
        public int Id { get; set; }
        public partial Maybe<TestDateRange> Availability { get; set; }
    }

    private class DateRangeDbContext : DbContext
    {
        public DbSet<MaybeDateRangeEntity> Entities => Set<MaybeDateRangeEntity>();

        public DateRangeDbContext(DbContextOptions<DateRangeDbContext> options) : base(options) { }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestDateRange).Assembly);
    }

    /// <summary>
    /// Open generic ValueObject for testing that the scanner excludes open generics.
    /// </summary>
    private class GenericRange<T> : ValueObject where T : IComparable<T>, IComparable
    {
        public T From { get; private set; } = default!;
        public T To { get; private set; } = default!;

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return From;
            yield return To;
        }
    }

    #endregion
}
