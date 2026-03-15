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
    /// Has a SqlState property for PostgreSQL error code detection.
    /// </summary>
    private class PostgresException : Exception
    {
        public string SqlState { get; } = string.Empty;

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

    #endregion
}