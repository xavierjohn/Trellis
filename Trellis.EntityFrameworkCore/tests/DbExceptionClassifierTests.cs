namespace Trellis.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Tests for <see cref="DbExceptionClassifier"/>.
/// Uses simulated exceptions to test classification logic across providers.
/// </summary>
public class DbExceptionClassifierTests
{
    #region IsDuplicateKey — SQLite "UNIQUE constraint failed"

    [Fact]
    public void IsDuplicateKey_SqliteUniqueConstraint_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("SQLite Error 19: 'UNIQUE constraint failed: Customers.Email'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsDuplicateKey_SqlitePrimaryKeyConstraint_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("SQLite Error 19: 'PRIMARY KEY constraint failed: Customers.Id'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsDuplicateKey — message-based fallback for "duplicate key"

    [Fact]
    public void IsDuplicateKey_FallbackDuplicateKeyMessage_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("Violation of UNIQUE KEY constraint 'UQ_Email'. Cannot insert duplicate key.");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsDuplicateKey — message-based fallback for "unique constraint"

    [Fact]
    public void IsDuplicateKey_FallbackUniqueConstraintMessage_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("duplicate key value violates unique constraint \"IX_Users_Email\"");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsDuplicateKey — unrelated exception

    [Fact]
    public void IsDuplicateKey_UnrelatedDbUpdateException_ReturnsFalse()
    {
        // Arrange
        var inner = new InvalidOperationException("Connection timeout");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsDuplicateKey_NoInnerException_ReturnsFalse()
    {
        // Arrange
        var ex = CreateDbUpdateException(innerException: null);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsForeignKeyViolation — SQLite "FOREIGN KEY constraint"

    [Fact]
    public void IsForeignKeyViolation_SqliteForeignKeyConstraint_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("SQLite Error 19: 'FOREIGN KEY constraint failed'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsForeignKeyViolation — message-based fallback

    [Fact]
    public void IsForeignKeyViolation_FallbackMessage_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_Orders_Customers\".");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsForeignKeyViolation_PostgresFallbackMessage_ReturnsTrue()
    {
        // Arrange
        var inner = new InvalidOperationException("insert or update on table \"orders\" violates foreign key constraint \"fk_orders_customers\"");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsForeignKeyViolation — unrelated exception

    [Fact]
    public void IsForeignKeyViolation_UnrelatedDbUpdateException_ReturnsFalse()
    {
        // Arrange
        var inner = new InvalidOperationException("Connection timeout");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsForeignKeyViolation_NoInnerException_ReturnsFalse()
    {
        // Arrange
        var ex = CreateDbUpdateException(innerException: null);

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsDuplicateKey — SQL Server error codes via reflection mock

    [Fact]
    public void IsDuplicateKey_SqlServerError2601_ReturnsTrue()
    {
        // Arrange
        var inner = new SqlException(2601, "Violation of UNIQUE KEY constraint");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsDuplicateKey_SqlServerError2627_ReturnsTrue()
    {
        // Arrange
        var inner = new SqlException(2627, "Violation of PRIMARY KEY constraint");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsForeignKeyViolation — SQL Server error 547

    [Fact]
    public void IsForeignKeyViolation_SqlServerError547_ReturnsTrue()
    {
        // Arrange
        var inner = new SqlException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsDuplicateKey — PostgreSQL SqlState 23505

    [Fact]
    public void IsDuplicateKey_PostgresSqlState23505_ReturnsTrue()
    {
        // Arrange
        var inner = new PostgresException("23505", "duplicate key value violates unique constraint");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsDuplicateKey(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IsForeignKeyViolation — PostgreSQL SqlState 23503

    [Fact]
    public void IsForeignKeyViolation_PostgresSqlState23503_ReturnsTrue()
    {
        // Arrange
        var inner = new PostgresException("23503", "insert or update violates foreign key constraint");
        var ex = CreateDbUpdateException(inner);

        // Act
        var result = DbExceptionClassifier.IsForeignKeyViolation(ex);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region ExtractConstraintDetail

    [Fact]
    public void ExtractConstraintDetail_WithInnerException_ReturnsMessage()
    {
        // Arrange — ExtractConstraintDetail is a diagnostic utility for logging;
        // callers (e.g. SaveChangesResultAsync) are responsible for not surfacing
        // this raw message in user-facing Error.Detail.
        var inner = new InvalidOperationException("UNIQUE constraint failed: Customers.Email");
        var ex = CreateDbUpdateException(inner);

        // Act
        var detail = DbExceptionClassifier.ExtractConstraintDetail(ex);

        // Assert
        detail.Should().Be("UNIQUE constraint failed: Customers.Email");
    }

    [Fact]
    public void ExtractConstraintDetail_NoInnerException_ReturnsNull()
    {
        // Arrange
        var ex = CreateDbUpdateException(innerException: null);

        // Act
        var detail = DbExceptionClassifier.ExtractConstraintDetail(ex);

        // Assert
        detail.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static DbUpdateException CreateDbUpdateException(Exception? innerException, string? overrideTypeName = null)
    {
        if (innerException is not null && overrideTypeName is not null)
        {
            // For SQLite-style exceptions, create a named fake
            innerException = overrideTypeName switch
            {
                "SqliteException" => new SqliteException(innerException.Message),
                _ => innerException
            };
        }

        return innerException is not null
            ? new DbUpdateException("An error occurred while saving.", innerException)
            : new DbUpdateException("An error occurred while saving.");
    }

    /// <summary>
    /// Fake exception named "SqlException" so GetType().Name returns "SqlException".
    /// Has a Number property for SQL Server error code detection.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private class SqlException : Exception
    {
        public int Number { get; }

        public SqlException() { }
        public SqlException(string message) : base(message) { }
        public SqlException(string message, Exception innerException) : base(message, innerException) { }
        public SqlException(int number, string message) : base(message) => Number = number;
    }

    /// <summary>
    /// Fake exception named "PostgresException" so GetType().Name returns "PostgresException".
    /// Has a SqlState property for PostgreSQL error code detection, plus ConstraintName /
    /// TableName / SchemaName properties matching the real Npgsql shape so the constraint-identity
    /// extractor can pick them up via reflection.
    /// </summary>
    private class PostgresException : Exception
    {
        public string SqlState { get; } = string.Empty;

        public string? ConstraintName { get; set; }

        public string? TableName { get; set; }

        public string? SchemaName { get; set; }

        public PostgresException() { }
        public PostgresException(string message) : base(message) { }
        public PostgresException(string message, Exception innerException) : base(message, innerException) { }
        public PostgresException(string sqlState, string message) : base(message) => SqlState = sqlState;
    }

    /// <summary>
    /// Fake exception named "SqliteException" so GetType().Name returns "SqliteException".
    /// </summary>
    private class SqliteException : Exception
    {
        public SqliteException() { }
        public SqliteException(string message) : base(message) { }
        public SqliteException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Fake exception named "MySqlException" so GetType().Name returns "MySqlException".
    /// Mirrors the shape of <c>MySql.Data.MySqlClient.MySqlException</c> /
    /// <c>MySqlConnector.MySqlException</c> via Number + SqlState properties.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    private class MySqlException : Exception
    {
        public int Number { get; }
        public string? SqlState { get; }

        public MySqlException() { }
        public MySqlException(string message) : base(message) { }
        public MySqlException(string message, Exception innerException) : base(message, innerException) { }
        public MySqlException(int number, string sqlState, string message) : base(message)
        {
            Number = number;
            SqlState = sqlState;
        }
    }

    #endregion

    #region IsDuplicateKey — MySQL/MariaDB (PR #460 / GPT-5.5 review Major #2)

    /// <summary>
    /// Regression for the GPT-5.5 review finding (Major #2): MySQL duplicate-key violations
    /// were previously not classified, so <c>SaveChangesResultAsync</c> would let a
    /// <c>DbUpdateException</c> escape instead of converting to <c>Error.Conflict</c>.
    /// </summary>
    [Fact]
    public void IsDuplicateKey_MySqlNumber1062_ReturnsTrue()
    {
        var inner = new MySqlException(1062, "23000", "Duplicate entry 'foo' for key 'PRIMARY'");
        var ex = CreateDbUpdateException(inner);

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeTrue();
    }

    /// <summary>
    /// Regression: SQLSTATE "23000" alone is **not** a sufficient duplicate-key signal —
    /// MySQL reuses 23000 for both unique-constraint and foreign-key violations. The
    /// classifier must require error number 1062 or a "Duplicate entry" message; otherwise
    /// `SaveChangesResultAsync` (which checks <c>IsDuplicateKey</c> before
    /// <c>IsForeignKeyViolation</c>) would surface FK violations as
    /// <c>new Error.Conflict(Resource: null, Code: "duplicate.key")</c>.
    /// </summary>
    [Fact]
    public void IsDuplicateKey_MySqlSqlState23000Alone_ReturnsFalse()
    {
        // Driver surfaces SqlState 23000 with no Number and a message that doesn't start
        // with "Duplicate entry" — could equally be an FK violation. Refuse to misclassify.
        var inner = new MySqlException(0, "23000", "Some message that doesn't start with 'Duplicate entry'");
        var ex = CreateDbUpdateException(inner);

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeFalse();
    }

    [Fact]
    public void IsDuplicateKey_MySqlMessageFallback_ReturnsTrue()
    {
        // Driver doesn't expose Number / SqlState; classifier falls back to message form.
        var inner = new MySqlException("Duplicate entry 'bar' for key 'IX_Customers_Email'");
        var ex = CreateDbUpdateException(inner);

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeTrue();
    }

    [Fact]
    public void IsDuplicateKey_MySqlOtherError_ReturnsFalse()
    {
        var inner = new MySqlException(2002, "HY000", "Connection refused");
        var ex = CreateDbUpdateException(inner);

        DbExceptionClassifier.IsDuplicateKey(ex).Should().BeFalse();
    }

    [Fact]
    public void IsForeignKeyViolation_MySqlNumber1452_ReturnsTrue()
    {
        var inner = new MySqlException(1452, "23000",
            "Cannot add or update a child row: a foreign key constraint fails ...");
        var ex = CreateDbUpdateException(inner);

        DbExceptionClassifier.IsForeignKeyViolation(ex).Should().BeTrue();
    }

    [Fact]
    public void IsForeignKeyViolation_MySqlNumber1451_ReturnsTrue()
    {
        var inner = new MySqlException(1451, "23000",
            "Cannot delete or update a parent row: a foreign key constraint fails ...");
        var ex = CreateDbUpdateException(inner);

        DbExceptionClassifier.IsForeignKeyViolation(ex).Should().BeTrue();
    }

    #endregion

    #region ExtractConstraintIdentity — typed extraction (PostgreSQL)

    [Fact]
    public void ExtractConstraintIdentity_Postgres_ReturnsTypedConstraintAndTable()
    {
        // Arrange — Npgsql exposes ConstraintName and TableName as typed string properties.
        var inner = new PostgresException("23505", "duplicate key value violates unique constraint \"uq_probes_url\"")
        {
            ConstraintName = "uq_probes_url",
            TableName = "probes",
            SchemaName = "public",
        };
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("uq_probes_url");
        // Schema-qualified when the provider surfaces SchemaName.
        table.Should().Be("public.probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_PostgresWithoutSchema_ReturnsBareTable()
    {
        var inner = new PostgresException("23505", "duplicate key value violates unique constraint")
        {
            ConstraintName = "uq_probes_url",
            TableName = "probes",
            SchemaName = null,
        };
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("uq_probes_url");
        table.Should().Be("probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_PostgresNoConstraintExposed_ReturnsNullPair()
    {
        var inner = new PostgresException("23505", "duplicate key value");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().BeNull();
    }

    #endregion

    #region ExtractConstraintIdentity — SQL Server message parsing

    [Fact]
    public void ExtractConstraintIdentity_SqlServer2627_ParsesViolationOfConstraintForm()
    {
        // 2627 form: "Violation of UNIQUE KEY constraint 'IX_Probes_Url'. Cannot insert
        //            duplicate key in object 'dbo.Probes'. The duplicate key value is (...)."
        var inner = new SqlException(
            2627,
            "Violation of UNIQUE KEY constraint 'IX_Probes_Url'. Cannot insert duplicate key in object 'dbo.Probes'. The duplicate key value is (https://example.com).");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("IX_Probes_Url");
        table.Should().Be("dbo.Probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqlServer2601_ParsesUniqueIndexForm()
    {
        // 2601 form: "Cannot insert duplicate key row in object 'dbo.Probes' with unique
        //            index 'IX_Probes_Url'. The duplicate key value is (...)."
        var inner = new SqlException(
            2601,
            "Cannot insert duplicate key row in object 'dbo.Probes' with unique index 'IX_Probes_Url'. The duplicate key value is (https://example.com).");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("IX_Probes_Url");
        table.Should().Be("dbo.Probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqlServerPrimaryKeyViolation_ParsesConstraintAndTable()
    {
        var inner = new SqlException(
            2627,
            "Violation of PRIMARY KEY constraint 'PK_Probes'. Cannot insert duplicate key in object 'dbo.Probes'. The duplicate key value is (1).");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("PK_Probes");
        table.Should().Be("dbo.Probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqlServerForeignKeyViolation_ParsesConstraintAndTable()
    {
        var inner = new SqlException(
            547,
            "The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_Orders_Customers\". The conflict occurred in database \"AppDb\", table \"dbo.Customers\", column 'Id'.");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("FK_Orders_Customers");
        table.Should().Be("dbo.Customers");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqlServerReferenceConstraint_ParsesConstraintAndTable()
    {
        // Parent-side delete/update emits "REFERENCE constraint" instead of "FOREIGN KEY
        // constraint" — same 547 code, different wording.
        var inner = new SqlException(
            547,
            "The DELETE statement conflicted with the REFERENCE constraint \"FK_Orders_Customers\". The conflict occurred in database \"AppDb\", table \"dbo.Orders\", column 'CustomerId'.");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("FK_Orders_Customers");
        table.Should().Be("dbo.Orders");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqlServerUnparseableMessage_ReturnsNullPair()
    {
        var inner = new SqlException(2627, "Some completely different message");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().BeNull();
    }

    #endregion

    #region ExtractConstraintIdentity — SQLite message parsing

    [Fact]
    public void ExtractConstraintIdentity_SqliteSingleColumnUnique_ParsesTableFromFirstQualifiedColumn()
    {
        // SQLite has no constraint name in the message; only "Table.Column".
        var inner = new InvalidOperationException("SQLite Error 19: 'UNIQUE constraint failed: Probes.Url'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().Be("Probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqliteCompositeUnique_ParsesTableFromFirstQualifiedColumn()
    {
        // Composite unique: "UNIQUE constraint failed: WebhookDeliveries.EventId, WebhookDeliveries.DestinationId"
        var inner = new InvalidOperationException(
            "SQLite Error 19: 'UNIQUE constraint failed: WebhookDeliveries.EventId, WebhookDeliveries.DestinationId'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().Be("WebhookDeliveries");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqlitePrimaryKeyConstraint_ParsesTable()
    {
        var inner = new InvalidOperationException("SQLite Error 19: 'PRIMARY KEY constraint failed: Probes.Id'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().Be("Probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_SqliteForeignKeyConstraint_ReturnsNullPair()
    {
        // FK form has no table info ("FOREIGN KEY constraint failed") — refuse to guess.
        var inner = new InvalidOperationException("SQLite Error 19: 'FOREIGN KEY constraint failed'.");
        var ex = CreateDbUpdateException(inner, "SqliteException");

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().BeNull();
    }

    #endregion

    #region ExtractConstraintIdentity — MySQL message parsing

    [Fact]
    public void ExtractConstraintIdentity_MySqlNewKeyForm_ParsesTableAndConstraint()
    {
        // MySQL 8 / MariaDB 10.6+ form: "Duplicate entry '...' for key '<table>.<key>'".
        var inner = new MySqlException(1062, "23000",
            "Duplicate entry 'https://example.com' for key 'probes.IX_Probes_Url'");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("IX_Probes_Url");
        table.Should().Be("probes");
    }

    [Fact]
    public void ExtractConstraintIdentity_MySqlOldKeyForm_ParsesConstraintOnly()
    {
        // Older MySQL form: "Duplicate entry '...' for key '<key>'" — no table prefix.
        var inner = new MySqlException(1062, "23000",
            "Duplicate entry 'https://example.com' for key 'IX_Probes_Url'");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().Be("IX_Probes_Url");
        table.Should().BeNull();
    }

    #endregion

    #region ExtractConstraintIdentity — defensive paths

    [Fact]
    public void ExtractConstraintIdentity_NoInnerException_ReturnsNullPair()
    {
        var ex = CreateDbUpdateException(innerException: null);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().BeNull();
    }

    [Fact]
    public void ExtractConstraintIdentity_UnknownProvider_ReturnsNullPair()
    {
        var inner = new InvalidOperationException("Some weird database error");
        var ex = CreateDbUpdateException(inner);

        var (name, table) = DbExceptionClassifier.ExtractConstraintIdentity(ex);

        name.Should().BeNull();
        table.Should().BeNull();
    }

    [Fact]
    public void ExtractConstraintIdentity_ThrowsNullArgumentException_WhenExIsNull()
    {
        var act = () => DbExceptionClassifier.ExtractConstraintIdentity(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}