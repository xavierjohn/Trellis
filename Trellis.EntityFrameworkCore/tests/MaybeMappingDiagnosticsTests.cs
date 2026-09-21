namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;
using Trellis.Primitives;

public partial class MaybeMappingDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMaybePropertyMappings_Scalar_ReportsResolvedColumn(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var mapping = Find(context, nameof(DiagnosticEntity.Note));

        mapping.StorageKind.Should().Be(MaybeStorageKind.Scalar);
        mapping.TableName.Should().Be("Diagnostics");
        mapping.Schema.Should().Be(sqlServer ? "app" : null);
        var column = mapping.Columns.Items.Should().ContainSingle().Which;
        column.ColumnName.Should().Be("Note");
        column.TableName.Should().Be("Diagnostics");
        column.Schema.Should().Be(mapping.Schema);
        column.IsNullable.Should().BeTrue();
        column.ColumnType.Should().Be(sqlServer ? "nvarchar(max)" : "TEXT");
        mapping.StorageReason.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMaybePropertyMappings_TableSplitComposite_ReportsEveryColumn(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var mapping = Find(context, nameof(DiagnosticEntity.Address));

        mapping.StorageKind.Should().Be(MaybeStorageKind.TableSplit);
        mapping.TableName.Should().Be("Diagnostics");
        mapping.Schema.Should().Be(sqlServer ? "app" : null);
        mapping.Columns.Items.Select(column => column.ColumnName).Should().BeEquivalentTo(
            ["Id", "Address_Street", "Address_City", "Address_State", "Address_ZipCode"]);
        mapping.Columns.Items.Where(column => column.ColumnName != "Id")
            .Should().OnlyContain(column => column.IsNullable);
        mapping.Columns.Items.Should().OnlyContain(column => column.TableName == "Diagnostics");
        mapping.ColumnName.Should().Be("Address_City");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMaybePropertyMappings_ValueTypeComposite_ReportsSeparateTableAndReason(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var mapping = Find(context, nameof(DiagnosticEntity.Period));

        mapping.StorageKind.Should().Be(MaybeStorageKind.SeparateTable);
        mapping.TableName.Should().Be("DiagnosticEntity_Period");
        mapping.Columns.Items.Select(column => column.ColumnName).Should().Contain(["Start", "End", "Label"]);
        mapping.Columns.Items.Should().OnlyContain(column => column.TableName == "DiagnosticEntity_Period");
        mapping.Columns.Items.Single(column => column.PropertyPath == "Start").IsNullable.Should().BeFalse();
        mapping.StorageReason.Should().Contain("non-nullable").And.Contain("Start").And.Contain("End");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMaybePropertyMappings_NestedOwnedComposite_IncludesNestedColumns(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var mapping = Find(context, nameof(DiagnosticEntity.Destination));

        mapping.StorageKind.Should().Be(MaybeStorageKind.SeparateTable);
        mapping.TableName.Should().Be("DiagnosticEntity_Destination");
        mapping.StorageReason.Should().Contain("nested owned").And.Contain("DeliveryFee");
        var amount = mapping.Columns.Items.Single(column => column.PropertyPath == "DeliveryFee.Amount");
        amount.TableName.Should().Be(mapping.TableName);
        amount.ColumnName.Should().Be("DeliveryFee");
        amount.IsNullable.Should().BeFalse();
        mapping.Columns.Items.Single(column => column.PropertyPath == "DeliveryFee.Currency")
            .ColumnName.Should().Be("DeliveryFeeCurrency");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMaybePropertyMappings_Money_UsesSpecializedTableSplitMapping(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var mapping = Find(context, nameof(DiagnosticEntity.Price));

        mapping.StorageKind.Should().Be(MaybeStorageKind.TableSplit);
        mapping.TableName.Should().Be("Diagnostics");
        mapping.ColumnName.Should().Be("Price");
        mapping.Columns.Items.Single(column => column.PropertyPath == "Amount")
            .ColumnName.Should().Be("Price");
        mapping.Columns.Items.Single(column => column.PropertyPath == "Currency")
            .ColumnName.Should().Be("PriceCurrency");
        mapping.Columns.Items.Where(column => column.ColumnName != "Id")
            .Should().OnlyContain(column => column.IsNullable);
        mapping.StorageReason.Should().BeNull("Money does not use the separate-table fallback");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetMaybePropertyMappings_ExplicitOverrides_ReportActualTableSchemaAndColumn(bool sqlServer)
    {
        using var context = CreateContext(sqlServer);

        var mapping = Find(context, nameof(DiagnosticEntity.CustomPeriod));

        mapping.StorageKind.Should().Be(MaybeStorageKind.SeparateTable);
        mapping.TableName.Should().Be("CustomPeriods");
        mapping.Schema.Should().Be(sqlServer ? "custom" : null);
        mapping.Columns.Items.Single(column => column.PropertyPath == "Start")
            .ColumnName.Should().Be("period_start");
        mapping.Columns.Items.Should().OnlyContain(column => column.TableName == "CustomPeriods");
        mapping.StorageReason.Should().BeNull("an explicit table override is not a convention decision");
    }

    [Fact]
    public void GetMaybePropertyMappings_SameTableNameDifferentSchema_IsSeparateTable()
    {
        using var context = CreateContext(sqlServer: true);

        var mapping = Find(context, nameof(DiagnosticEntity.OtherSchemaPeriod));

        mapping.StorageKind.Should().Be(MaybeStorageKind.SeparateTable);
        mapping.TableName.Should().Be("Diagnostics");
        mapping.Schema.Should().Be("other");
    }

    [Fact]
    public void GetMaybePropertyMappings_NestedSeparateTable_PreservesEachColumnDestination()
    {
        using var context = CreateContext(sqlServer: true);

        var mapping = Find(context, nameof(DiagnosticEntity.CustomDestination));
        var fee = mapping.Columns.Items.Single(column => column.PropertyPath == "DeliveryFee.Amount");

        mapping.TableName.Should().Be("CustomDestinations");
        fee.TableName.Should().Be("DeliveryFees");
        fee.Schema.Should().Be("custom");
        fee.ColumnName.Should().Be("fee_amount");
    }

    [Fact]
    public void ToMaybeMappingDebugString_CompositeMappings_ReportsStrategiesReasonsAndColumns()
    {
        using var context = CreateContext(sqlServer: true);

        var debug = context.ToMaybeMappingDebugString();

        debug.Should().Contain("storage=TableSplit").And.Contain("storage=SeparateTable")
            .And.Contain("table=app.Diagnostics")
            .And.Contain("Address_Street").And.Contain("Address_ZipCode")
            .And.Contain("table=custom.CustomPeriods")
            .And.Contain("period_start").And.Contain("non-nullable")
            .And.Contain("DeliveryFee.Amount").And.Contain("fee_amount");
        context.Model.ToMaybeMappingDebugString().Should().Be(debug);
    }

    [Fact]
    public void GetMaybePropertyMappings_RepeatedInspection_PreservesRecordValueEquality()
    {
        using var context = CreateContext();

        var first = context.GetMaybePropertyMappings();
        var second = context.Model.GetMaybePropertyMappings();

        first.Should().Equal(second);
        first.Select(mapping => mapping.GetHashCode())
            .Should().Equal(second.Select(mapping => mapping.GetHashCode()));
    }

    [Fact]
    public void MaybePropertyMapping_OriginalConstructorAndDeconstruction_RemainCompatible()
    {
        var mapping = new MaybePropertyMapping("Entity", typeof(DiagnosticEntity), "Note", "_note",
            typeof(string), typeof(string), true, true, "Note", null);

        var (entityName, entityType, property, backingField, innerType, storeType, mapped, nullable, column, provider) = mapping;

        entityName.Should().Be("Entity");
        entityType.Should().Be<DiagnosticEntity>();
        property.Should().Be("Note");
        backingField.Should().Be("_note");
        innerType.Should().Be<string>();
        storeType.Should().Be<string>();
        mapped.Should().BeTrue();
        nullable.Should().BeTrue();
        column.Should().Be("Note");
        provider.Should().BeNull();
        mapping.Columns.IsEmpty.Should().BeTrue();
    }

    private static MaybePropertyMapping Find(DiagnosticsDbContext context, string propertyName) =>
        context.GetMaybePropertyMappings().Single(mapping =>
            mapping.EntityClrType == typeof(DiagnosticEntity) && mapping.PropertyName == propertyName);

    private static DiagnosticsDbContext CreateContext(bool sqlServer = false)
    {
        var options = new DbContextOptionsBuilder<DiagnosticsDbContext>();
        if (sqlServer)
            options.UseSqlServer("Server=localhost;Database=MappingDiagnostics;Integrated Security=True");
        else
            options.UseSqlite("Data Source=:memory:");
        return new DiagnosticsDbContext(options.IgnoreManyServiceProvidersCreatedWarning().Options);
    }

    private partial class DiagnosticEntity
    {
        public int Id { get; set; }
        public partial Maybe<string> Note { get; set; }
        public partial Maybe<TestAddress> Address { get; set; }
        public partial Maybe<TestDateRange> Period { get; set; }
        public partial Maybe<TestAddressWithMoney> Destination { get; set; }
        public partial Maybe<Money> Price { get; set; }
        public partial Maybe<TestDateRange> CustomPeriod { get; set; }
        public partial Maybe<TestDateRange> OtherSchemaPeriod { get; set; }
        public partial Maybe<TestAddressWithMoney> CustomDestination { get; set; }
    }

    private class DiagnosticsDbContext(DbContextOptions<DiagnosticsDbContext> options) : DbContext(options)
    {
        public DbSet<DiagnosticEntity> Diagnostics => Set<DiagnosticEntity>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyTrellisConventions(typeof(TestAddress).Assembly);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var schema = Database.IsSqlServer() ? "custom" : null;
            if (Database.IsSqlServer())
                modelBuilder.HasDefaultSchema("app");

            modelBuilder.Entity<DiagnosticEntity>(entity =>
            {
                entity.OwnsOne<TestDateRange>("_customPeriod", owned =>
                {
                    owned.ToTable("CustomPeriods", schema);
                    owned.Property(period => period.Start).HasColumnName("period_start");
                });
                entity.OwnsOne<TestDateRange>("_otherSchemaPeriod", owned =>
                    owned.ToTable(Database.IsSqlServer() ? "Diagnostics" : "OtherPeriods",
                        Database.IsSqlServer() ? "other" : null));
                entity.OwnsOne<TestAddressWithMoney>("_customDestination", owned =>
                {
                    owned.ToTable("CustomDestinations", schema);
                    owned.OwnsOne(address => address.DeliveryFee, fee =>
                    {
                        fee.ToTable("DeliveryFees", schema);
                        fee.Property(value => value.Amount).HasColumnName("fee_amount");
                    });
                });
            });
        }
    }
}
