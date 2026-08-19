namespace Trellis.EntityFrameworkCore;

using System.Text.RegularExpressions;
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
            var constraintProp = FindProperty(inner.GetType(), "ConstraintName");
            if (constraintProp?.GetValue(inner) is string constraintName && !string.IsNullOrEmpty(constraintName))
                return $"Constraint: {constraintName}";
        }

        // For other providers the message itself is the most specific detail available.
        // IMPORTANT: this value is intended for logging/diagnostics only — do not surface it
        // directly in Error.Detail or API responses, as it may contain schema information
        // (table names, index names, rejected values). Use a safe generic message for end-users.
        return inner.Message;
    }

    /// <summary>
    /// Attempts to extract the database constraint identity (constraint name and table name)
    /// associated with a <see cref="DbUpdateException"/>, on a best-effort basis across
    /// SQL Server, PostgreSQL, SQLite, and MySQL/MariaDB providers. Returns
    /// <c>(null, null)</c> when no identity can be determined (unknown provider, missing
    /// inner exception, message format the parser does not recognise, localised provider
    /// message, etc.).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Typed extraction is preferred where the provider exposes the names as properties
    /// (PostgreSQL's <c>PostgresException.ConstraintName</c> / <c>TableName</c>); the
    /// SQL Server / SQLite / MySQL paths parse the message because those providers do
    /// not expose typed identity properties.
    /// </para>
    /// <para>
    /// <b>Telemetry-only.</b> Constraint and table names can reveal schema details and
    /// should not be surfaced directly in API responses. Use the returned values for
    /// structured logging and observability dimensions (for example as the
    /// <see cref="Error.Conflict.ConstraintName"/> / <see cref="Error.Conflict.ConstraintTableName"/>
    /// payload on the <see cref="Error.Conflict"/> produced by
    /// <c>DbContext.TryInsertUniqueAsync</c>).
    /// </para>
    /// </remarks>
    /// <param name="ex">The <see cref="DbUpdateException"/> to extract identity from.</param>
    /// <returns>
    /// A tuple of <c>(ConstraintName, TableName)</c> where each element may independently
    /// be <see langword="null"/> if the provider did not surface it or the message could
    /// not be parsed.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ex"/> is null.</exception>
    public static (string? ConstraintName, string? TableName) ExtractConstraintIdentity(DbUpdateException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var inner = ex.InnerException;
        if (inner is null)
            return (null, null);

        try
        {
            var typeName = inner.GetType().Name;
            var message = inner.Message ?? string.Empty;

            return typeName switch
            {
                "PostgresException" => ExtractPostgresIdentity(inner),
                "SqlException" => ParseSqlServerMessage(message),
                "SqliteException" => ParseSqliteMessage(message),
                "MySqlException" => ParseMySqlMessage(message),
                _ => (null, null),
            };
        }
        catch (Exception parseException) when (
            parseException is not OutOfMemoryException
            and not StackOverflowException)
        {
            // Defensive: a malformed message or a property surface that throws on access
            // must not propagate out of a diagnostic helper used inside an exception handler.
            return (null, null);
        }
    }

    private static bool TryGetSqlServerNumber(Exception ex, out int number)
    {
        number = 0;
        var prop = FindProperty(ex.GetType(), "Number");
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
        var prop = FindProperty(ex.GetType(), "SqlState");
        state = prop?.GetValue(ex) as string;
        return state is not null;
    }

    /// <summary>
    /// Resolves a public instance property by walking the type hierarchy most-derived first,
    /// asking each level for its own declarations.
    /// </summary>
    /// <remarks>
    /// <see cref="Type.GetProperty(string)"/> throws <see cref="System.Reflection.AmbiguousMatchException"/>
    /// when a derived type shadows a base property with a different type — which real drivers do:
    /// <c>MySqlConnector.MySqlException</c> re-declares <c>ErrorCode</c> as an enum over
    /// <see cref="System.Runtime.InteropServices.ExternalException"/>'s <see cref="int"/>. These
    /// helpers run inside exception handlers, so an escaping reflection failure would turn a
    /// classified conflict into an unhandled error.
    /// </remarks>
    private static System.Reflection.PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(
                name,
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly);
            if (property is not null)
                return property;
        }

        return null;
    }

    private static bool TryGetMySqlNumber(Exception ex, out int number)
    {
        number = 0;
        // MySql.Data.MySqlClient.MySqlException exposes a Number property; MySqlConnector's
        // MySqlException exposes ErrorCode (an enum convertible to int) and Number.
        var type = ex.GetType();
        var numberProp = FindProperty(type, "Number");
        if (numberProp?.GetValue(ex) is int n)
        {
            number = n;
            return true;
        }

        var errorCodeProp = FindProperty(type, "ErrorCode");
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

    private static (string? ConstraintName, string? TableName) ExtractPostgresIdentity(Exception inner)
    {
        var type = inner.GetType();
        var name = FindProperty(type, "ConstraintName")?.GetValue(inner) as string;
        var table = FindProperty(type, "TableName")?.GetValue(inner) as string;
        var schema = FindProperty(type, "SchemaName")?.GetValue(inner) as string;

        if (string.IsNullOrEmpty(name))
            name = null;

        string? qualifiedTable = (string.IsNullOrEmpty(schema), string.IsNullOrEmpty(table)) switch
        {
            (false, false) => $"{schema}.{table}",
            (true, false) => table,
            _ => null,
        };

        return (name, qualifiedTable);
    }

    // SQL Server 2627: "Violation of <KIND> constraint '<name>'. Cannot insert duplicate key in object '<table>'."
    // SQL Server 2601: "Cannot insert duplicate key row in object '<table>' with unique index '<name>'."
    // SQL Server 547 (FK): "The INSERT/UPDATE/DELETE statement conflicted with the (REFERENCE|FOREIGN KEY) constraint '<name>'. The conflict occurred in database '<db>', table '<table>', column '<col>'."
    private static readonly Regex SqlServer2627Regex = new(
        @"constraint\s+'(?<name>[^']+)'\.\s*Cannot\s+insert\s+duplicate\s+key\s+in\s+object\s+'(?<table>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private static readonly Regex SqlServer2601Regex = new(
        @"Cannot\s+insert\s+duplicate\s+key\s+row\s+in\s+object\s+'(?<table>[^']+)'\s+with\s+unique\s+index\s+'(?<name>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private static readonly Regex SqlServerFkRegex = new(
        @"(?:FOREIGN\s+KEY|REFERENCE)\s+constraint\s+[""'](?<name>[^""']+)[""'].*?table\s+[""'](?<table>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(50));

    private static (string? ConstraintName, string? TableName) ParseSqlServerMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return (null, null);

        var match = SqlServer2627Regex.Match(message);
        if (match.Success)
            return (match.Groups["name"].Value, match.Groups["table"].Value);

        match = SqlServer2601Regex.Match(message);
        if (match.Success)
            return (match.Groups["name"].Value, match.Groups["table"].Value);

        match = SqlServerFkRegex.Match(message);
        if (match.Success)
            return (match.Groups["name"].Value, match.Groups["table"].Value);

        return (null, null);
    }

    // SQLite "UNIQUE constraint failed: <Table>.<Column>[, <Table>.<Column>...]"
    // SQLite "PRIMARY KEY constraint failed: <Table>.<Column>"
    // SQLite "FOREIGN KEY constraint failed" (no table info — refuse to guess).
    private static readonly Regex SqliteUniqueOrPkRegex = new(
        @"(?:UNIQUE|PRIMARY\s+KEY)\s+constraint\s+failed:\s*(?<table>[^.\s,]+)\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private static (string? ConstraintName, string? TableName) ParseSqliteMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return (null, null);

        var match = SqliteUniqueOrPkRegex.Match(message);
        if (match.Success)
            return (ConstraintName: null, TableName: match.Groups["table"].Value);

        return (null, null);
    }

    // MySQL 8 / MariaDB 10.6+: "Duplicate entry '<value>' for key '<table>.<key>'"
    // Older MySQL: "Duplicate entry '<value>' for key '<key>'"
    private static readonly Regex MySqlDuplicateEntryRegex = new(
        @"Duplicate\s+entry\s+'[^']*'\s+for\s+key\s+'(?<key>[^']+)'",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private static (string? ConstraintName, string? TableName) ParseMySqlMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return (null, null);

        var match = MySqlDuplicateEntryRegex.Match(message);
        if (!match.Success)
            return (null, null);

        var key = match.Groups["key"].Value;
        var dot = key.IndexOf('.');
        if (dot > 0 && dot < key.Length - 1)
            return (ConstraintName: key[(dot + 1)..], TableName: key[..dot]);

        return (ConstraintName: key, TableName: null);
    }
}