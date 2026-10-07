namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

internal static class DbUpdateErrorFactory
{
    internal static Error.Conflict ConcurrentModification(DbUpdateConcurrencyException exception) =>
        new(Code: FaultCodes.ConcurrentModification)
        {
            Detail = $"One or more entities were modified by another process. {exception.Entries.Count} entities affected.",
        };

    internal static Error.Conflict DuplicateKey(DbUpdateException exception) =>
        ConstraintConflict(exception, FaultCodes.DuplicateKey, "A record with the same unique value already exists.");

    internal static Error.Conflict ForeignKeyViolation(DbUpdateException exception) =>
        ConstraintConflict(exception, FaultCodes.ReferentialIntegrity, "Operation violates a referential integrity constraint.");

    private static Error.Conflict ConstraintConflict(DbUpdateException exception, string code, string detail)
    {
        var (constraintName, constraintTable) = DbExceptionClassifier.ExtractConstraintIdentity(exception);
        return new Error.Conflict(Code: code)
        {
            Detail = detail,
            ConstraintName = constraintName,
            ConstraintTableName = constraintTable,
        };
    }
}
