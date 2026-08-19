namespace Trellis.EntityFrameworkCore.Tests;

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Guards <see cref="DbExceptionClassifier"/> against provider drift.
/// </summary>
/// <remarks>
/// <para>
/// The classifier deliberately takes no driver dependency and matches provider exceptions by
/// <see cref="Type.Name"/>, reading error codes through reflection. That trade-off avoids
/// transitive dependencies on every supported driver, but it means a provider renaming its
/// exception type — or removing the property the classifier reads — turns a duplicate-key
/// violation into an unhandled <see cref="DbUpdateException"/> at runtime. It fails open, and
/// silently.
/// </para>
/// <para>
/// <see cref="DbExceptionClassifierTests"/> cannot catch that: it uses locally-declared fakes
/// named after the providers, so it asserts the classifier matches strings the tests themselves
/// supply. These tests reference the <b>real</b> driver types instead (test-only package
/// references; nothing ships), so a rename or property removal breaks the build on package
/// upgrade rather than in production.
/// </para>
/// <para>
/// Where a driver exposes a public constructor (PostgreSQL) the real exception is classified
/// end to end. SQLite goes further and provokes a genuine constraint violation through EF Core
/// against an in-memory database, so the real driver's real message is parsed. SQL Server and
/// MySQL expose no public constructor, so those are pinned at the type/property contract level.
/// </para>
/// </remarks>
public class DbExceptionClassifierProviderContractTests
{
    #region Real driver type contracts — a rename must break the build

    [Fact]
    public void SqlServer_exception_still_carries_the_name_and_number_the_classifier_reads()
    {
        var type = typeof(Microsoft.Data.SqlClient.SqlException);

        type.Name.Should().Be("SqlException",
            "DbExceptionClassifier matches SQL Server by this exact type name");
        DeclaredProperty(type, "Number")!.PropertyType.Should().Be<int>(
            "the classifier reads Number to match 2601 / 2627 / 547");
    }

    [Fact]
    public void Postgres_exception_still_carries_the_name_and_properties_the_classifier_reads()
    {
        var type = typeof(Npgsql.PostgresException);

        type.Name.Should().Be("PostgresException",
            "DbExceptionClassifier matches PostgreSQL by this exact type name");
        DeclaredProperty(type, "SqlState")!.PropertyType.Should().Be<string>(
            "the classifier reads SqlState to match 23505 / 23503");
        DeclaredProperty(type, "ConstraintName")!.PropertyType.Should().Be<string>();
        DeclaredProperty(type, "TableName")!.PropertyType.Should().Be<string>();
        DeclaredProperty(type, "SchemaName")!.PropertyType.Should().Be<string>();
    }

    [Fact]
    public void Sqlite_exception_still_carries_the_name_the_classifier_reads() =>
        typeof(SqliteException).Name.Should().Be("SqliteException",
            "DbExceptionClassifier matches SQLite by this exact type name, then parses the message");

    [Fact]
    public void MySql_exception_still_carries_the_name_and_number_the_classifier_reads()
    {
        var type = typeof(MySqlConnector.MySqlException);

        type.Name.Should().Be("MySqlException",
            "DbExceptionClassifier matches MySQL/MariaDB by this exact type name");
        DeclaredProperty(type, "Number")!.PropertyType.Should().Be<int>(
            "the classifier reads Number to match 1062 / 1451 / 1452");
    }

    [Fact]
    public void MySql_exception_shadows_ErrorCode_with_a_different_type()
    {
        // Pins the reason the classifier must not use a plain Type.GetProperty("ErrorCode"):
        // MySqlConnector re-declares ErrorCode as an enum over ExternalException's int, and
        // reflection reports that as an ambiguous match. This test documents the real shape
        // that MySqlExceptionWithoutNumber below imitates.
        var type = typeof(MySqlConnector.MySqlException);

        DeclaredProperty(type, "ErrorCode")!.PropertyType.IsEnum.Should().BeTrue();
        typeof(ExternalException).GetProperty("ErrorCode")!.PropertyType.Should().Be<int>();

        var ambiguous = () => type.GetProperty("ErrorCode");
        ambiguous.Should().Throw<AmbiguousMatchException>(
            "the classifier must resolve shadowed properties per declaring type, not by name alone");
    }

    #endregion

    #region Real PostgreSQL exceptions — constructed, then classified

    [Fact]
    public void IsDuplicateKey_RealPostgresException_ReturnsTrue()
    {
        var ex = new DbUpdateException("save failed", NewPostgresException("23505"));

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeTrue();
    }

    [Fact]
    public void IsForeignKeyViolation_RealPostgresException_ReturnsTrue()
    {
        var ex = new DbUpdateException("save failed", NewPostgresException("23503"));

        DbExceptionClassifier.IsForeignKeyViolation(ex).Should().BeTrue();
    }

    [Fact]
    public void IsDuplicateKey_RealPostgresForeignKeyException_ReturnsFalse()
    {
        var ex = new DbUpdateException("save failed", NewPostgresException("23503"));

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeFalse(
            "a foreign-key violation must not be reported as a conflict");
    }

    [Fact]
    public void ExtractConstraintIdentity_RealPostgresException_ReadsTypedProperties()
    {
        var ex = new DbUpdateException("save failed", NewPostgresException("23505"));

        var (constraintName, tableName) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        constraintName.Should().Be("ix_users_email");
        tableName.Should().Be("public.users");
    }

    #endregion

    #region Real SQLite violations — provoked through EF Core, then classified

    [Fact]
    public async Task IsDuplicateKey_RealSqliteUniqueViolation_ReturnsTrue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = NewProbeContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        context.Users.Add(new ProbeUser { Id = 1, Email = "a@example.com" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Users.Add(new ProbeUser { Id = 2, Email = "a@example.com" });

        var act = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var ex = (await act.Should().ThrowAsync<DbUpdateException>()).Which;

        ex.InnerException.Should().BeOfType<SqliteException>(
            "the assertions below must run against the driver's real exception, not an EF wrapper");
        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeTrue();
        DbExceptionClassifier.IsForeignKeyViolation(ex).Should().BeFalse();
        DbExceptionClassifier.ExtractConstraintIdentity(ex).TableName.Should().Be("Users");
    }

    [Fact]
    public async Task IsForeignKeyViolation_RealSqliteForeignKeyViolation_ReturnsTrue()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = NewProbeContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        context.Orders.Add(new ProbeOrder { Id = 1, UserId = 404 });

        var act = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var ex = (await act.Should().ThrowAsync<DbUpdateException>()).Which;

        ex.InnerException.Should().BeOfType<SqliteException>();
        DbExceptionClassifier.IsForeignKeyViolation(ex).Should().BeTrue();
        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeFalse(
            "SaveChangesResultAsync checks IsDuplicateKey first, so a false positive here would "
            + "map a foreign-key violation to 409 Conflict");
    }

    #endregion

    #region Shadowed ErrorCode — the classifier must not throw out of an exception handler

    [Fact]
    public void IsDuplicateKey_MySqlExceptionExposingOnlyShadowedErrorCode_ReturnsTrue()
    {
        // Drivers that surface the code only as a shadowing enum (the shape pinned above) must
        // still classify. Reading the property by name alone throws AmbiguousMatchException,
        // which would escape IsDuplicateKey — called from inside a catch — and turn a 409 into
        // a 500.
        var inner = new MySqlException("Error 1062 raised by the server.", MySqlErrorCode.DuplicateKeyEntry);
        var ex = new DbUpdateException("save failed", inner);

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeTrue();
    }

    [Fact]
    public void IsForeignKeyViolation_MySqlExceptionExposingOnlyShadowedErrorCode_ReturnsTrue()
    {
        var inner = new MySqlException("Error 1452 raised by the server.", MySqlErrorCode.NoReferencedRow2);
        var ex = new DbUpdateException("save failed", inner);

        DbExceptionClassifier.IsForeignKeyViolation(ex).Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static PropertyInfo? DeclaredProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property is not null)
                return property;
        }

        return null;
    }

    private static Npgsql.PostgresException NewPostgresException(string sqlState) =>
        new(
            messageText: "constraint violation",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            detail: null!,
            hint: null!,
            position: 0,
            internalPosition: 0,
            internalQuery: null!,
            where: null!,
            schemaName: "public",
            tableName: "users",
            columnName: "email",
            dataTypeName: null!,
            constraintName: "ix_users_email",
            file: null!,
            line: null!,
            routine: null!);

    private static ProbeContext NewProbeContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ProbeContext>()
            .UseSqlite(connection)
            .IgnoreManyServiceProvidersCreatedWarning()
            .Options);

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeUser> Users => Set<ProbeUser>();

        public DbSet<ProbeOrder> Orders => Set<ProbeOrder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeUser>(b =>
            {
                b.ToTable("Users");
                b.HasKey(u => u.Id);
                b.Property(u => u.Id).ValueGeneratedNever();
                b.HasIndex(u => u.Email).IsUnique();
            });

            modelBuilder.Entity<ProbeOrder>(b =>
            {
                b.ToTable("Orders");
                b.HasKey(o => o.Id);
                b.Property(o => o.Id).ValueGeneratedNever();
                b.HasOne<ProbeUser>().WithMany().HasForeignKey(o => o.UserId);
            });
        }
    }

    private sealed class ProbeUser
    {
        public int Id { get; set; }

        public string Email { get; set; } = string.Empty;
    }

    private sealed class ProbeOrder
    {
        public int Id { get; set; }

        public int UserId { get; set; }
    }

    private enum MySqlErrorCode
    {
        DuplicateKeyEntry = 1062,
        NoReferencedRow2 = 1452,
    }

    /// <summary>
    /// Imitates the real <c>MySqlConnector.MySqlException</c> shape pinned by
    /// <see cref="MySql_exception_shadows_ErrorCode_with_a_different_type"/>: named
    /// <c>MySqlException</c>, no <c>Number</c>, and <c>ErrorCode</c> re-declared as an enum
    /// over <see cref="ExternalException"/>'s <see cref="int"/>. The real type cannot be
    /// constructed — it exposes no public constructor.
    /// </summary>
    private sealed class MySqlException : ExternalException
    {
        public MySqlException()
        {
        }

        public MySqlException(string message) : base(message)
        {
        }

        public MySqlException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public MySqlException(string message, int errorCode) : base(message, errorCode)
        {
        }

        public MySqlException(string message, MySqlErrorCode errorCode) : base(message) => ErrorCode = errorCode;

        public new MySqlErrorCode ErrorCode { get; }
    }

    #endregion
}
