namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Classifies database exceptions across providers (SQL Server, PostgreSQL, SQLite).
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
}