namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Classifies database exceptions across providers (SQL Server, PostgreSQL, SQLite, MySQL/MariaDB).
/// Used internally by both <see cref="DbContextExtensions.SaveChangesResultAsync(DbContext, CancellationToken)"/>
/// and <see cref="DbContextExtensions.SaveChangesResultAsync(DbContext, bool, CancellationToken)"/> overloads.
/// Also available for direct use in repositories that need custom error messages per exception type.
/// </summary>
public static class DbExceptionClassifier
{
    /// <summary>
    /// Returns true if the exception represents a unique constraint violation (duplicate key).
    /// </summary>
    /// <param name="ex">The <see cref="DbUpdateException"/> to classify.</param>
    /// <returns><c>true</c> if this is a duplicate key violation; otherwise <c>false</c>.</returns>
    public static bool IsDuplicateKey(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null)
            return false;

        var message = inner.Message;
        var typeName = inner.GetType().Name;

        // SQL Server: SqlException with Number 2601 (unique index) or 2627 (unique constraint)
        if (typeName == "SqlException" && TryGetSqlServerNumber(inner, out var number))
            return number is 2601 or 2627;

        // PostgreSQL: PostgresException with SqlState "23505"
        if (typeName == "PostgresException" && TryGetPostgresSqlState(inner, out var state))
            return state == "23505";

        // SQLite: duplicate key can surface as UNIQUE or PRIMARY KEY constraint failures
        if (typeName == "SqliteException")
            return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("PRIMARY KEY constraint failed", StringComparison.OrdinalIgnoreCase);

        // MySQL/MariaDB: MySqlException with Number 1062 (ER_DUP_ENTRY) or "Duplicate entry"
        // message form. SQLSTATE "23000" is **not** a sufficient signal on its own — MySQL
        // reuses 23000 for foreign-key violations as well, so trusting it here would let
        // FK violations be misclassified as duplicate-key conflicts (SaveChangesResultAsync
        // checks IsDuplicateKey before IsForeignKeyViolation).
        // The provider type lives in the consumer's MySql.Data.* / MySqlConnector package, so
        // detect by name (matches the SQL Server / PostgreSQL pattern above).
        if (typeName == "MySqlException")
        {
            if (TryGetMySqlNumber(inner, out var mysqlNumber) && mysqlNumber == 1062)
                return true;
            // Fallback message form ("Duplicate entry '...' for key '...'") for older drivers
            // that don't surface Number.
            if (message.StartsWith("Duplicate entry", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Fallback: message-based detection for unknown providers
        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the exception represents a foreign key constraint violation.
    /// </summary>
    /// <param name="ex">The <see cref="DbUpdateException"/> to classify.</param>
    /// <returns><c>true</c> if this is a foreign key violation; otherwise <c>false</c>.</returns>
    public static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null)
            return false;

        var message = inner.Message;
        var typeName = inner.GetType().Name;

        // SQL Server: SqlException with Number 547
        if (typeName == "SqlException" && TryGetSqlServerNumber(inner, out var number))
            return number == 547;

        // PostgreSQL: PostgresException with SqlState "23503"
        if (typeName == "PostgresException" && TryGetPostgresSqlState(inner, out var state))
            return state == "23503";

        // SQLite
        if (typeName == "SqliteException")
            return message.Contains("FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase);

        // MySQL/MariaDB: MySqlException with Number 1452 (ER_NO_REFERENCED_ROW_2) or 1451
        // (ER_ROW_IS_REFERENCED_2). Message form starts with "Cannot add or update a child row"
        // or "Cannot delete or update a parent row". Note that SQLSTATE "23000" alone is not a
        // sufficient signal — MySQL reuses 23000 for duplicate-key violations as well — so the
        // message prefix is checked unconditionally rather than only inside the SQLSTATE branch.
        if (typeName == "MySqlException")
        {
            if (TryGetMySqlNumber(inner, out var mysqlNumber) && mysqlNumber is 1451 or 1452)
                return true;
            if (message.StartsWith("Cannot add or update a child row", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("Cannot delete or update a parent row", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Fallback
        return message.Contains("FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("violates foreign key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to extract a human-readable constraint detail from the exception.
    /// Returns the constraint name or violated column if available, otherwise <c>null</c>.
    /// </summary>
    /// <param name="ex">The <see cref="DbUpdateException"/> to extract details from.</param>
    /// <returns>A constraint detail string, or <c>null</c> if no detail can be extracted.</returns>
    public static string? ExtractConstraintDetail(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null)
            return null;

        var typeName = inner.GetType().Name;

        // PostgreSQL: try ConstraintName property
        if (typeName == "PostgresException")
        {
            var constraintProp = inner.GetType().GetProperty("ConstraintName");
            if (constraintProp?.GetValue(inner) is string constraintName && !string.IsNullOrEmpty(constraintName))
                return $"Constraint: {constraintName}";
        }

        // For other providers the message itself is the most specific detail available.
        // IMPORTANT: this value is intended for logging/diagnostics only — do not surface it
        // directly in Error.Detail or API responses, as it may contain schema information
        // (table names, index names, rejected values). Use a safe generic message for end-users.
        return inner.Message;
    }

    private static bool TryGetSqlServerNumber(Exception ex, out int number)
    {
        number = 0;
        var prop = ex.GetType().GetProperty("Number");
        if (prop?.GetValue(ex) is int n)
        {
            number = n;
            return true;
        }

        return false;
    }

    private static bool TryGetPostgresSqlState(Exception ex, out string? state)
    {
        state = null;
        var prop = ex.GetType().GetProperty("SqlState");
        state = prop?.GetValue(ex) as string;
        return state is not null;
    }

    private static bool TryGetMySqlNumber(Exception ex, out int number)
    {
        number = 0;
        // MySql.Data.MySqlClient.MySqlException exposes a Number property; MySqlConnector's
        // MySqlException exposes ErrorCode (an enum convertible to int) and Number.
        var type = ex.GetType();
        var numberProp = type.GetProperty("Number");
        if (numberProp?.GetValue(ex) is int n)
        {
            number = n;
            return true;
        }

        var errorCodeProp = type.GetProperty("ErrorCode");
        if (errorCodeProp?.GetValue(ex) is { } errorCode)
        {
            try
            {
                number = Convert.ToInt32(errorCode, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception convertException) when (convertException is FormatException or InvalidCastException or OverflowException)
            {
                // Fall through; some driver versions expose ErrorCode as a non-numeric type.
            }
        }

        return false;
    }
}